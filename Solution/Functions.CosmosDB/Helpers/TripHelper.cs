using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using CosmosDbIoTScenario.Common;
using CosmosDbIoTScenario.Common.Models;
using CosmosDbIoTScenario.Common.Models.Alerts;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Storage;
using Microsoft.Azure.Storage.Queue;
using Microsoft.Extensions.Logging;
using NCrontab;
using Newtonsoft.Json;

namespace Functions.CosmosDB.Helpers
{
    public class TripHelper
    {
        private readonly Trip _trip;
        private readonly Container _metadataContainer;
        private readonly Container _alertsContainer;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IQueueResolver _queueResolver;
        private readonly ILogger _log;

        public TripHelper(Trip trip, Container metadataContainer, Container alertsContainer,
            IHttpClientFactory httpClientFactory, IQueueResolver queueResolver, ILogger log)
        {
            _trip = trip;
            _metadataContainer = metadataContainer;
            _alertsContainer = alertsContainer;
            _httpClientFactory = httpClientFactory;
            _queueResolver = queueResolver;
            _log = log;
        }

        /// <summary>
        /// Uses a Trip record and the aggregate odometer reading (<see cref="odometerHigh"/>)
        /// to retrieve a Consignment record from Cosmos DB and calculate the vehicle's trip
        /// progress, based on miles driven compared to the planned trip distance. If the number
        /// of miles driven are greater than or equal to the planned distance, the trip and
        /// consignment records are marked as complete. Otherwise, if the trip has not been
        /// completed and we are past the delivery due date, the trip and consignment are marked
        /// as delayed. Finally, if the trip record's start date (tripStarted) is null, the
        /// date/time is set to now and the trip and consignment records are marked as started.
        ///
        /// The trip and consignment records in the Cosmos DB metadataContainer are updated as needed,
        /// and if any of the three conditions are met (completed, delayed, or started), a
        /// boolean value of true is returned, indicating a trip alert should be sent.
        /// </summary>
        /// <param name="odometerHigh">The max aggregate odometer reading in the telemetry
        /// batch.</param>
        /// <returns>True if an alert needs to be sent.</returns>
        public async Task<bool> UpdateTripProgress(double odometerHigh)
        {
            var sendTripAlert = false;

            // Retrieve the Consignment record.
            var document = await _metadataContainer.ReadItemAsync<Consignment>(_trip.consignmentId,
                new PartitionKey(_trip.consignmentId));
            var consignment = document.Resource;
            var updateTrip = false;
            var updateConsignment = false;

            // Calculate how far along the vehicle is for this trip.
            var milesDriven = odometerHigh - _trip.odometerBegin;
            if (milesDriven >= _trip.plannedTripDistance)
            {
                // The trip is completed!
                _trip.status = WellKnown.Status.Completed;
                _trip.odometerEnd = odometerHigh;
                _trip.tripEnded = DateTime.UtcNow;
                consignment.status = WellKnown.Status.Completed;

                // Update the trip and consignment records.
                updateTrip = true;
                updateConsignment = true;

                sendTripAlert = true;
            }
            else
            {
                if (DateTime.UtcNow >= consignment.deliveryDueDate && _trip.status != WellKnown.Status.Delayed)
                {
                    // The trip is delayed!
                    _trip.status = WellKnown.Status.Delayed;
                    consignment.status = WellKnown.Status.Delayed;

                    // Update the trip and consignment records.
                    updateTrip = true;
                    updateConsignment = true;

                    sendTripAlert = true;
                }
            }

            if (_trip.tripStarted == null)
            {
                // Set the trip start date.
                _trip.tripStarted = DateTime.UtcNow;
                // Set the trip and consignment status to Active.
                _trip.status = WellKnown.Status.Active;
                consignment.status = WellKnown.Status.Active;

                updateTrip = true;
                updateConsignment = true;

                sendTripAlert = true;
            }

            // Update the trip and consignment records.
            if (updateTrip)
            {
                await _metadataContainer.ReplaceItemAsync(_trip, _trip.id, new PartitionKey(_trip.partitionKey));
            }

            if (updateConsignment)
            {
                await _metadataContainer.ReplaceItemAsync(consignment, consignment.id, new PartitionKey(consignment.partitionKey));
            }

            return sendTripAlert;
        }

        /// <summary>
        /// Triggers the Logic App through the LogicAppUrl environment variable/app setting
        /// by instantiating a new <see cref="HttpClient"/> instance from the
        /// <see cref="HttpClientFactory"/> and POSTing payload data, including the
        /// <see cref="Trip"/> information, <see cref="averageRefrigerationUnitTemp"/>
        /// aggregate value, and the RecipientEmail environment variable.
        /// </summary>
        /// <param name="averageRefrigerationUnitTemp">Average refrigeration unit temperature
        /// reading in the telemetry batch.</param>
        /// <returns></returns>
        public async Task SendTripAlert(double averageRefrigerationUnitTemp)
        {
            var settings = await GetSettingsFromDatabase();

            // If the user has indicated that they want alerts and they have specified a recipient email address, send the alert as needed.
            if (settings != null && !string.IsNullOrWhiteSpace(settings.recipientEmailAddress) && settings.SendAlerts)
            {
                // Only send or queue the alert if the trip status type is enabled in the alert settings.
                if (settings.sendTripStartedAlerts && _trip.status == WellKnown.Status.Active ||
                    settings.sendTripCompletedAlerts && _trip.status == WellKnown.Status.Completed ||
                    settings.sendTripDelayedAlerts && _trip.status == WellKnown.Status.Delayed)
                {
                    // Create the payload to send to the Logic App.
                    var payload = new LogicAppAlert
                    {
                        consignmentId = _trip.consignmentId,
                        customer = _trip.consignment.customer,
                        deliveryDueDate = _trip.consignment.deliveryDueDate,
                        hasHighValuePackages = _trip.packages.Any(p => p.highValue),
                        id = _trip.id,
                        lastRefrigerationUnitTemperatureReading = averageRefrigerationUnitTemp,
                        location = _trip.location,
                        lowestPackageStorageTemperature = _trip.packages.Min(p => p.storageTemperature),
                        odometerBegin = _trip.odometerBegin,
                        odometerEnd = _trip.odometerEnd,
                        plannedTripDistance = _trip.plannedTripDistance,
                        tripStarted = _trip.tripStarted,
                        tripEnded = _trip.tripEnded,
                        status = _trip.status,
                        vin = _trip.vin,
                        temperatureSetting = _trip.temperatureSetting,
                        recipientEmail = settings.recipientEmailAddress
                    };

                    if (string.IsNullOrWhiteSpace(settings.sendAlertInterval))
                    {
                        // The send alert interval setting is null or empty, indicating that every alert should be sent individually.
                        payload.isSummary = false;
                        // Send the alert to the Logic App.
                        await SendAlertToLogicApp(payload);
                    }
                    else
                    {
                        // The settings specify that an alert summary should be sent on a schedule. Add the alert to a queue.
                        await QueueTripAlert(payload);
                    }
                }
            }
            
        }

        /// <summary>
        /// Adds alert messages to an Azure Storage queue. This allows alerts to be sent as a summary
        /// of events within a given period of time, as defined in the <see cref="Settings"/>.
        /// </summary>
        /// <param name="alert">The alert to queue.</param>
        /// <returns></returns>
        protected async Task QueueTripAlert(LogicAppAlert alert)
        {
            // Use the QueueResolver to retrieve a reference to the Azure Storage queue.
            var alertsQueue = _queueResolver.GetQueue(WellKnown.StorageQueues.AlertQueueName);

            // Create a message and add it to the queue.
            var message = new CloudQueueMessage(JsonConvert.SerializeObject(alert));
            await alertsQueue.AddMessageAsync(message);
        }

        /// <summary>
        /// Uses the Logic App's HTTP trigger to send the alert.
        /// </summary>
        /// <param name="alert"></param>
        /// <returns></returns>
        protected async Task SendAlertToLogicApp(LogicAppAlert alert)
        {
            // Have the HttpClient factory create a new client instance.
            var httpClient = _httpClientFactory.CreateClient(NamedHttpClients.LogicAppClient);

            var postBody = JsonConvert.SerializeObject(alert);

            // Post the alert to the Logic App.
            await httpClient.PostAsync(Environment.GetEnvironmentVariable("LogicAppUrl"), new StringContent(postBody, Encoding.UTF8, "application/json"));
        }

        public async Task SendAlertSummary()
        {
            var compareDate = DateTime.UtcNow;
            var settings = await GetSettingsFromDatabase();

            // If the user has indicated that they want alerts and they have specified a recipient email address, and
            // they have specified a summary alert interval (sendAlertInterval is not empty), then retrieve the
            // AlertSummaryHistory document from the database to determine if it is time to send a new alert summary.
            if (settings != null && !string.IsNullOrWhiteSpace(settings.recipientEmailAddress) && settings.SendAlerts &&
                !string.IsNullOrWhiteSpace(settings.sendAlertInterval))
            {
                var alertSummaryHistory = new AlertSummaryHistory();
                // Retrieve the alert summary history document from the alerts container.
                var response = await _alertsContainer.ReadItemAsync<AlertSummaryHistory>(WellKnown.EntityTypes.Settings,
                    new PartitionKey(WellKnown.EntityTypes.Settings));

                if (response.StatusCode == HttpStatusCode.OK)
                {
                    alertSummaryHistory = response.Resource;
                }

                // Retrieve the alert summary schedule from the defined cron notation.
                var schedule = CrontabSchedule.Parse(settings.sendAlertInterval);
                // Find the next schedule send date.
                var nextOccurrence =
                    schedule.GetNextOccurrence(alertSummaryHistory.summaryAlertLastSent ?? compareDate.AddDays(-1));

                if (nextOccurrence <= compareDate)
                {
                    // It is time to send a new alert summary.
                    _log.LogInformation($"Sending an alert summary based on next scheduled send date of {nextOccurrence}");

                    // Use the QueueResolver to retrieve a reference to the Azure Storage queue.
                    var alertsQueue = _queueResolver.GetQueue(WellKnown.StorageQueues.AlertQueueName);

                    // Fetch the queue attributes.
                    alertsQueue.FetchAttributes();

                    // Retrieve the cached approximate message count.
                    var cachedMessageCount = alertsQueue.ApproximateMessageCount;

                    _log.LogInformation($"Number of alert messages in the queue: {cachedMessageCount}");

                    if (cachedMessageCount.HasValue && cachedMessageCount.Value > 0)
                    {
                        // Set the batch size for number of messages to retrieve from the queue (max: 32).
                        var batchSize = 32;
                        // Determine how many loops we need to retrieve the messages, based on the batch size and number of messages.
                        var loops = (int)Math.Ceiling((double)cachedMessageCount.Value / batchSize);
                        var alerts = new List<LogicAppAlert>();

                        // Loop through the batch size of messages until they are all retrieved.
                        for (var loop = 0; loop < loops; loop++)
                        {
                            foreach (var message in await alertsQueue.GetMessagesAsync(batchSize))
                            {
                                var alert = JsonConvert.DeserializeObject<LogicAppAlert>(message.AsString);
                                alerts.Add(alert);
                                // Delete the message from the queue.
                                await alertsQueue.DeleteMessageAsync(message);
                            }
                        }

                        var delayed = alerts.Where(a => a.status == WellKnown.Status.Delayed).ToList();

                        var payload = new LogicAppAlert
                        {
                            tripsStarted = alerts.Count(a => a.status == WellKnown.Status.Active),
                            tripsCompleted = alerts.Count(a => a.status == WellKnown.Status.Completed),
                            tripsDelayed = delayed.Count,
                            delayedVINs = delayed.Count > 0 ? string.Join(", ", delayed.Select(d => d.vin)) : "None",
                            recipientEmail = settings.recipientEmailAddress,
                            isSummary = true
                        };

                        // Send the summarized alert to the Logic App via its HTTP trigger.
                        await SendAlertToLogicApp(payload);

                        // Update the alert summary history to keep track of when we sent the alert.
                        alertSummaryHistory.summaryAlertLastSent = compareDate;
                        await _alertsContainer.ReplaceItemAsync(alertSummaryHistory, alertSummaryHistory.id, new PartitionKey(alertSummaryHistory.id));
                    }
                }
            }
        }

        /// <summary>
        /// Retrieves the Settings document from the alerts container.
        /// </summary>
        /// <returns></returns>
        protected async Task<Settings> GetSettingsFromDatabase()
        {
            var settings = new Settings();
            // Retrieve the alert settings to see how and whether to send alerts.
            var response = await _alertsContainer.ReadItemAsync<Settings>(WellKnown.EntityTypes.Settings,
                new PartitionKey(WellKnown.EntityTypes.Settings));

            if (response.StatusCode == HttpStatusCode.OK)
            {
                settings = response.Resource;
            }

            return settings;
        }
    }
}
