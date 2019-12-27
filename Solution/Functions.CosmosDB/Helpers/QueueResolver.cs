using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Azure.Storage;
using Microsoft.Azure.Storage.Queue;
using Microsoft.Extensions.Options;

namespace Functions.CosmosDB.Helpers
{
    /// <summary>
    /// This class creates a <see cref="CloudQueueClient"/> reference, using the passed in
    /// Azure Storage connection string, then returns a queue reference by name through its
    /// GetQueue method.
    /// </summary>
    public class QueueResolver : IQueueResolver
    {
        private readonly CloudQueueClient _queueClient;

        public QueueResolver(IOptions<AzureStorageSettings> settings)
        {
            var storageAccount = CloudStorageAccount.Parse(settings.Value.ColdStorageAccount);
            _queueClient = storageAccount.CreateCloudQueueClient();
        }

        public CloudQueue GetQueue(string queueName)
        {
            return _queueClient.GetQueueReference(queueName);
        }
    }

    public interface IQueueResolver
    {
        CloudQueue GetQueue(string queueName);
    }
}
