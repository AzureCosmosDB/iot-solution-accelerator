using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;

namespace CosmosDbIoTScenario.Common.Models.Alerts
{
    public class AlertSummaryHistory
    {
        /// <summary>
        /// We set the id value to AlertSummaryHistory for rapid retrieval of this
        /// document within the alerts container in Cosmos DB. This container's
        /// partition key is set to /id.
        /// </summary>
        [JsonProperty] public string id => WellKnown.EntityTypes.AlertSummaryHistory;
        /// <summary>
        /// The date and time the last alert was sent. This is used to determine,
        /// based on the sending schedule, when to send a new summary alert.
        /// </summary>
        [JsonProperty] public DateTime? summaryAlertLastSent { get; set; }
    }
}
