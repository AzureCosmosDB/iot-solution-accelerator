using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using CosmosDbIoTScenario.Common;
using Microsoft.AspNetCore.Mvc.Rendering;
using Newtonsoft.Json;

namespace FleetManagementWebApp.ViewModels
{
    public class SettingsViewModel
    {
        public SettingsViewModel()
        {
            var intervals = WellKnown.SendAlertInterval.Select(n =>
                new SelectListItem
                {
                    Value = n.Value,
                    Text = n.Key
                }).ToList();
            sendAlertIntervals = new SelectList(intervals, "Value", "Text");
        }

        public string id { get; set; }
        /// <summary>
        /// The email address to which alerts should be sent.
        /// </summary>
        [Required]
        [EmailAddress]
        [Display(Name = "Recipient Email Address")]
        public string recipientEmailAddress { get; set; }
        /// <summary>
        /// Whether to send alerts when a trip starts.
        /// </summary>
        [Display(Name = "Send Trip Started Alerts")]
        public bool sendTripStartedAlerts { get; set; }
        /// <summary>
        /// Whether to send alerts when a trip ends.
        /// </summary>
        [Display(Name = "Send Trip Completed Alerts")]
        public bool sendTripCompletedAlerts { get; set; }
        /// <summary>
        /// Whether to send alerts when a trip is delayed.
        /// </summary>
        [Display(Name = "Send Trip Delayed Alerts")]
        public bool sendTripDelayedAlerts { get; set; }

        /// <summary>
        /// Select list containing alert-sending schedules in cron notation (https://crontab.guru/).
        /// An empty value denotes that the alerts should be sent individually, not as a scheduled summary.
        /// </summary>
        [Display(Name = "Send Alert Interval")]
        public string sendAlertInterval { get; set; }
        public IEnumerable<SelectListItem> sendAlertIntervals { get; set; }
    }
}
