using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Authorization;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using CosmosDbIoTScenario.Common;
using CosmosDbIoTScenario.Common.Models.Alerts;
using Microsoft.AspNetCore.Mvc;
using FleetManagementWebApp.Models;
using FleetManagementWebApp.Services;
using FleetManagementWebApp.ViewModels;
using Microsoft.Extensions.Configuration;

namespace FleetManagementWebApp.Controllers
{
    [Authorize]
    public class HomeController : Controller
    {
        private readonly ICosmosDbService _cosmosDbService;
        private readonly IConfiguration _configuration;
        private readonly string _cosmosDbContainerName;

        public HomeController(ICosmosDbService cosmosDbService, IConfiguration configuration)
        {
            _cosmosDbService = cosmosDbService;
            _configuration = configuration;
            // Set the default Cosmos DB container name for the Cosmos DB service to the alerts container.
            _cosmosDbContainerName = configuration["AlertsContainerName"];
        }

        public IActionResult Index()
        {
            return View();
        }

        [ActionName("Settings")]
        public async Task<IActionResult> SettingsAsync()
        {
            // Retrieve the settings document from the Alerts container, if it exists.
            // Since this container has the partition key set to the /id field, we use the ID value
            // of "Settings" for both the ID and the partition key in our query. This results in a
            // very fast, low-cost point lookup.
            var settings = await _cosmosDbService.GetItemAsync<Settings>(WellKnown.EntityTypes.Settings,
                               WellKnown.EntityTypes.Settings, _cosmosDbContainerName) ?? new Settings();

            var vm = new SettingsViewModel
            {
                id = settings.id,
                recipientEmailAddress = settings.recipientEmailAddress,
                sendTripCompletedAlerts = settings.sendTripCompletedAlerts,
                sendTripDelayedAlerts = settings.sendTripDelayedAlerts,
                sendTripStartedAlerts = settings.sendTripStartedAlerts,
                sendAlertInterval = settings.sendAlertInterval
            };

            return View(vm);
        }

        [HttpPost]
        [ActionName("Settings")]
        [ValidateAntiForgeryToken]
        public async Task<ActionResult> SettingsAsync([Bind("id,recipientEmailAddress,sendTripCompletedAlerts,sendTripDelayedAlerts,sendTripStartedAlerts,sendAlertInterval")] SettingsViewModel item)
        {
            if (ModelState.IsValid)
            {
                var settings = await _cosmosDbService.GetItemAsync<Settings>(WellKnown.EntityTypes.Settings,
                                          WellKnown.EntityTypes.Settings, _cosmosDbContainerName) ?? new Settings();

                settings.recipientEmailAddress = item.recipientEmailAddress;
                settings.sendTripCompletedAlerts = item.sendTripCompletedAlerts;
                settings.sendTripDelayedAlerts = item.sendTripDelayedAlerts;
                settings.sendTripStartedAlerts = item.sendTripStartedAlerts;
                settings.sendAlertInterval = item.sendAlertInterval;

                // The UpdateItemAsync method performs an upsert. This means it creates the item if it doesn't exist, or updates an existing item.
                await _cosmosDbService.UpdateItemAsync(settings, settings.id, _cosmosDbContainerName);
                return RedirectToAction("Index");
            }

            // Invalid. Send back to the edit form.
            return View(item);
        }

        [AllowAnonymous]
        public IActionResult Privacy()
        {
            return View();
        }

        [AllowAnonymous]
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
