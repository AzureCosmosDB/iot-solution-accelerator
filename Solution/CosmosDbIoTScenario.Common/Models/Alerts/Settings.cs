using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;

namespace CosmosDbIoTScenario.Common.Models.Alerts
{
    public class Settings
    {
        /// <summary>
        /// We set the id value to Settings for rapid retrieval of this document
        /// within the alerts container in Cosmos DB. This container's partition
        /// key is set to /id.
        /// </summary>
        [JsonProperty] public string id => WellKnown.EntityTypes.Settings;
        /// <summary>
        /// The email address to which alerts should be sent.
        /// </summary>
        [JsonProperty] public string recipientEmailAddress { get; set; }

        /// <summary>
        /// Whether to send alerts when a trip starts.
        /// </summary>
        [JsonProperty] public bool sendTripStartedAlerts { get; set; } = true;

        /// <summary>
        /// Whether to send alerts when a trip ends.
        /// </summary>
        [JsonProperty] public bool sendTripCompletedAlerts { get; set; } = true;

        /// <summary>
        /// Whether to send alerts when a trip is delayed.
        /// </summary>
        [JsonProperty] public bool sendTripDelayedAlerts { get; set; } = true;

        /// <summary>
        /// A value defining alert-sending schedules in cron notation (https://crontab.guru/).
        /// An empty value denotes that the alerts should be sent individually, not as a scheduled summary.
        /// </summary>
        [JsonProperty] public string sendAlertInterval { get; set; }

        /// <summary>
        /// Readonly property returns true if any of the alert types are selected. This indicates that alerts must be sent.
        /// </summary>
        [JsonIgnore]
        public bool SendAlerts => sendTripStartedAlerts || sendTripDelayedAlerts || sendTripCompletedAlerts;
    }
}
