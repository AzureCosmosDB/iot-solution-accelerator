using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using CosmosDbIoTScenario.Common;
using CosmosDbIoTScenario.Common.Models;
using Functions.CosmosDB.Helpers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Linq;
using Microsoft.Azure.Documents;
using Microsoft.Azure.EventHubs;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Functions.CosmosDB
{
    public class Functions
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly CosmosClient _cosmosClient;

        // Use Dependency Injection to inject the HttpClientFactory service and Cosmos DB client that were configured in Startup.cs.
        public Functions(IHttpClientFactory httpClientFactory, CosmosClient cosmosClient)
        {
            _httpClientFactory = httpClientFactory;
            _cosmosClient = cosmosClient;
        }

        [FunctionName("TripProcessor")]
        public async Task TripProcessor([CosmosDBTrigger(
            databaseName: "ContosoAuto",
            collectionName: "telemetry",
            ConnectionStringSetting = "CosmosDBConnection",
            LeaseCollectionName = "leases",
            LeaseCollectionPrefix = "trips",
            CreateLeaseCollectionIfNotExists = true,
            StartFromBeginning = true)]IReadOnlyList<Document> vehicleEvents,
            ILogger log)
        {
            log.LogInformation($"Evaluating {vehicleEvents.Count} events from Cosmos DB to optionally update Trip and Consignment metadata.");

            // Retrieve the Trip records by VIN, compare the odometer reading to the starting odometer reading to calculate miles driven,
            // and update the Trip and Consignment status and send an alert if needed once completed.
            const string database = "ContosoAuto";
            const string metadataContainer = "metadata";

            if (vehicleEvents.Count > 0)
            {
                foreach (var group in vehicleEvents.GroupBy(singleEvent => singleEvent.GetPropertyValue<string>("vin")))
                {
                    var vin = group.Key;
                    var odometerHigh = group.Max(item => item.GetPropertyValue<double>("odometer"));
                    var averageRefrigerationUnitTemp =
                        group.Average(item => item.GetPropertyValue<double>("refrigerationUnitTemp"));

                    // First, retrieve the metadata Cosmos DB container reference:
                    var container = _cosmosClient.GetContainer(database, metadataContainer);

                    // Create a query, defining the partition key so we don't execute a fan-out query (saving RUs), where the entity type is a Trip and the status is not Completed, Canceled, or Inactive.
                    var query = container.GetItemLinqQueryable<Trip>(requestOptions: new QueryRequestOptions { PartitionKey = new Microsoft.Azure.Cosmos.PartitionKey(vin) })
                        .Where(p => p.status != WellKnown.Status.Completed
                                    && p.status != WellKnown.Status.Canceled
                                    && p.status != WellKnown.Status.Inactive
                                    && p.entityType == WellKnown.EntityTypes.Trip)
                        .ToFeedIterator();

                    if (query.HasMoreResults)
                    {
                        // Only retrieve the first result.
                        var trip = (await query.ReadNextAsync()).FirstOrDefault();
                        
                        if (trip != null)
                        {
                            var tripHelper = new TripHelper(trip, container, _httpClientFactory);

                            var sendTripAlert = await tripHelper.UpdateTripProgress(odometerHigh);

                            if (sendTripAlert)
                            {
                                // Send a trip alert.
                                await tripHelper.SendTripAlert(averageRefrigerationUnitTemp);
                            }
                        }
                    }
                }
            }
        }

        [FunctionName("ColdStorage")]
        public async Task ChangeColdStorageFeedTrigger([CosmosDBTrigger(
            databaseName: "ContosoAuto",
            collectionName: "telemetry",
            ConnectionStringSetting = "CosmosDBConnection",
            LeaseCollectionName = "leases",
            LeaseCollectionPrefix = "cold",
            CreateLeaseCollectionIfNotExists = true,
            StartFromBeginning = true)]IReadOnlyList<Document> vehicleEvents,
            Binder binder,
            ILogger log)
        {
            log.LogInformation($"Saving {vehicleEvents.Count} events from Cosmos DB to cold storage.");

            if (vehicleEvents.Count > 0)
            {
                // Use imperative binding to Azure Storage, as opposed to declarative binding.
                // This allows us to compute the binding parameters and set the file path dynamically during runtime.
                var attributes = new Attribute[]
                {
                    new BlobAttribute($"telemetry/custom/scenario1/{DateTime.UtcNow:yyyy/MM/dd/HH/mm/ss-fffffff}.json", FileAccess.ReadWrite),
                    new StorageAccountAttribute("ColdStorageAccount")
                };

                using (var fileOutput = await binder.BindAsync<TextWriter>(attributes))
                {
                    // Write the data to Azure Storage for cold storage and batch processing requirements.
                    // Please note: Application Insights will log Dependency errors with a 404 result code for each write.
                    // The error is harmless since the internal Storage SDK returns a 404 when it first checks if the file already exists.
                    // Application Insights cannot distinguish between "good" and "bad" 404 responses for these calls. These errors can be ignored for now.
                    // For more information, see https://github.com/Azure/azure-functions-durable-extension/issues/593
                    fileOutput.Write(JsonConvert.SerializeObject(vehicleEvents));
                }
            }
        }

        [FunctionName("SendToEventHubsForReporting")]
        public async Task SendToEventHubsForReporting([CosmosDBTrigger(
            databaseName: "ContosoAuto",
            collectionName: "telemetry",
            ConnectionStringSetting = "CosmosDBConnection",
            LeaseCollectionName = "leases",
            LeaseCollectionPrefix = "reporting",
            CreateLeaseCollectionIfNotExists = true,
            StartFromBeginning = true)]IReadOnlyList<Document> vehicleEvents,
            [EventHub("reporting", Connection = "EventHubsConnection")] IAsyncCollector<EventData> vehicleEventsOut,
            ILogger log)
        {
            log.LogInformation($"Sending {vehicleEvents.Count} Cosmos DB records to Event Hubs for reporting.");

            if (vehicleEvents.Count > 0)
            {
                foreach (var vehicleEvent in vehicleEvents)
                {
                    // Convert to a VehicleEvent class.
                    var vehicleEventOut = await vehicleEvent.ReadAsAsync<VehicleEvent>();
                    // Add to the Event Hub output collection.
                    await vehicleEventsOut.AddAsync(new EventData(
                        Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(vehicleEventOut))));
                }
            }
        }

        [FunctionName("HealthCheck")]
        public static async Task<IActionResult> HealthCheck(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = null)] HttpRequest req,
            ILogger log)
        {
            log.LogInformation("Performing health check on the Cosmos DB processing Function App.");

            // This is a very simple health check that ensures each configuration setting exists and has a value.
            // More thorough checks would validate each value against an expected format or by connecting to each service as required.
            // The function will return an HTTP status of 200 (OK) if all values contain non-zero strings.
            // If any are null or empty, the function will return an error, indicating which values are missing.

            var cosmosDbConnection = Environment.GetEnvironmentVariable("CosmosDBConnection");
            var coldStorageAccount = Environment.GetEnvironmentVariable("ColdStorageAccount");
            var eventHubsConnection = Environment.GetEnvironmentVariable("EventHubsConnection");
            var logicAppUrl = Environment.GetEnvironmentVariable("LogicAppUrl");
            var recipientEmail = Environment.GetEnvironmentVariable("RecipientEmail");

            var variableList = new List<string>();
            if (string.IsNullOrWhiteSpace(cosmosDbConnection)) variableList.Add("CosmosDBConnection");
            if (string.IsNullOrWhiteSpace(coldStorageAccount)) variableList.Add("ColdStorageAccount");
            if (string.IsNullOrWhiteSpace(eventHubsConnection)) variableList.Add("EventHubsConnection");
            if (string.IsNullOrWhiteSpace(logicAppUrl)) variableList.Add("LogicAppUrl");
            if (string.IsNullOrWhiteSpace(recipientEmail)) variableList.Add("RecipientEmail");

            if (variableList.Count > 0)
            {
                return new BadRequestObjectResult($"The service is missing one or more application settings: {string.Join(", ", variableList)}");
            }

            return new OkObjectResult($"The service contains expected application settings");
        }
    }
}
