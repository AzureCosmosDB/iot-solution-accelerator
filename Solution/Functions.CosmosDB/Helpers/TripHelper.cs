using System;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using CosmosDbIoTScenario.Common;
using CosmosDbIoTScenario.Common.Models;
using CosmosDbIoTScenario.Common.Models.Alerts;
using Microsoft.Azure.Cosmos;
using Newtonsoft.Json;

namespace Functions.CosmosDB.Helpers
{
    public class TripHelper
    {
        private readonly Trip _trip;
        private readonly Container _container;
        private readonly IHttpClientFactory _httpClientFactory;

        public TripHelper(Trip trip, Container container, IHttpClientFactory httpClientFactory)
        {
            _trip = trip;
            _container = container;
            _httpClientFactory = httpClientFactory;
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
        /// The trip and consignment records in the Cosmos DB container are updated as needed,
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
            var document = await _container.ReadItemAsync<Consignment>(_trip.consignmentId,
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
                await _container.ReplaceItemAsync(_trip, _trip.id, new PartitionKey(_trip.partitionKey));
            }

            if (updateConsignment)
            {
                await _container.ReplaceItemAsync(consignment, consignment.id, new PartitionKey(consignment.partitionKey));
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
            // Have the HttpClient factory create a new client instance.
            var httpClient = _httpClientFactory.CreateClient(NamedHttpClients.LogicAppClient);

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
                recipientEmail = Environment.GetEnvironmentVariable("RecipientEmail")
            };

            var postBody = JsonConvert.SerializeObject(payload);

            await httpClient.PostAsync(Environment.GetEnvironmentVariable("LogicAppUrl"), new StringContent(postBody, Encoding.UTF8, "application/json"));
        }
    }
}
