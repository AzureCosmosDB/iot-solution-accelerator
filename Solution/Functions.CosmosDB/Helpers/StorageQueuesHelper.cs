using System;
using System.Collections.Generic;
using System.Text;
using CosmosDbIoTScenario.Common;
using Microsoft.Azure.Storage;
using Microsoft.Azure.Storage.Queue;

namespace Functions.CosmosDB.Helpers
{
    public class StorageQueuesHelper
    {
        
        /// <summary>
        /// Creates known queues as needed.
        /// </summary>
        /// <param name="storageConnectionString">The Azure Storage connection string for the queues.</param>
        public static void CreateKnownAzureQueues(string storageConnectionString)
        {
            var storageAccount = CloudStorageAccount.Parse(storageConnectionString);
            var queueClient = storageAccount.CreateCloudQueueClient();

            // Add queue references to create new queues if they do not exist.
            queueClient.GetQueueReference(WellKnown.StorageQueues.AlertQueueName).CreateIfNotExists();
        }

    }
}
