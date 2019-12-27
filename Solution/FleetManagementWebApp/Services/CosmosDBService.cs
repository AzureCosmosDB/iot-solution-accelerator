using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using CosmosDbIoTScenario.Common.Models;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Fluent;
using Microsoft.Azure.Cosmos.Linq;
using Microsoft.Azure.Documents.Linq;
using Microsoft.Extensions.Configuration;
using P.Pager;

namespace FleetManagementWebApp.Services
{
    public class CosmosDbService : ICosmosDbService
    {
        private readonly Dictionary<string, Container> _containers = new Dictionary<string, Container>();
        private readonly string _defaultContainerName;

        /// <summary>
        /// Creates a new Cosmos DB service reference to simplify working with the Cosmos DB SDK.
        /// </summary>
        /// <param name="dbClient">The CosmosClient object.</param>
        /// <param name="databaseName">The name of the Cosmos DB database.</param>
        /// <param name="containerNames">A collection of container names. The first container in
        /// the list will be used as the default container within this service's methods if a
        /// container name is not defined in the parameters.</param>
        public CosmosDbService(
            CosmosClient dbClient,
            string databaseName,
            IEnumerable<string> containerNames)
        {
            var names = containerNames.ToList();
            if (names.Any())
            {
                foreach (var containerName in names)
                {
                    _containers.Add(containerName, dbClient.GetContainer(databaseName, containerName));
                }

                _defaultContainerName = names[0];
            }
            else
            {
                throw new ArgumentNullException(nameof(containerNames), "You must specify at least one container name.");
            }
        }

        public async Task AddItemAsync<T>(T item, string partitionKey, string containerName = null) where T : class
        {
            var container = GetContainerByName(containerName);
            await container.CreateItemAsync<T>(item, new PartitionKey(partitionKey));
        }

        public async Task DeleteItemAsync<T>(string id, string partitionKey, string containerName = null) where T : class
        {
            var container = GetContainerByName(containerName);
            await container.DeleteItemAsync<T>(id, new PartitionKey(partitionKey));
        }

        public async Task<T> GetItemAsync<T>(string id, string partitionKey, string containerName = null) where T : class
        {
            var container = GetContainerByName(containerName);
            try
            {
                var response = await container.ReadItemAsync<T>(id, new PartitionKey(partitionKey));
                return response.Resource;
            }
            catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return null;
            }
        }

        public async Task<IEnumerable<T>> GetItemsAsync<T>(string queryString, string partitionKey = null, string containerName = null) where T : class
        {
            var container = GetContainerByName(containerName);
            var query = container.GetItemQueryIterator<T>(new QueryDefinition(queryString),
                requestOptions: !string.IsNullOrWhiteSpace(partitionKey) ? new QueryRequestOptions { PartitionKey = new PartitionKey(partitionKey) } : null);
            var results = new List<T>();
            while (query.HasMoreResults)
            {
                var response = await query.ReadNextAsync();

                results.AddRange(response.ToList());
            }

            return results;
        }

        public async Task<IEnumerable<T>> GetItemsAsync<T>(QueryDefinition queryDefinition, string partitionKey = null, string containerName = null) where T : class
        {
            var container = GetContainerByName(containerName);
            var query = container.GetItemQueryIterator<T>(queryDefinition,
                requestOptions: !string.IsNullOrWhiteSpace(partitionKey) ? new QueryRequestOptions { PartitionKey = new PartitionKey(partitionKey) } : null);
            var results = new List<T>();
            while (query.HasMoreResults)
            {
                var response = await query.ReadNextAsync();

                results.AddRange(response.ToList());
            }

            return results;
        }

        public async Task<IEnumerable<T>> GetItemsAsync<T>(Expression<Func<T, bool>> predicate, int? skip = null, int? take = null, string partitionKey = null, string containerName = null) where T : class
        {
            var container = GetContainerByName(containerName);
            FeedIterator<T> setIterator;
            var query = container.GetItemLinqQueryable<T>(requestOptions: !string.IsNullOrWhiteSpace(partitionKey) ? new QueryRequestOptions { PartitionKey = new PartitionKey(partitionKey) } : null);

            // Implement paging:
            if (skip.HasValue && take.HasValue)
            {
                setIterator = query.Where(predicate).Skip(skip.Value).Take(take.Value).ToFeedIterator();
            }
            else
            {
                setIterator = take.HasValue ? query.Where(predicate).Take(take.Value).ToFeedIterator() : query.Where(predicate).ToFeedIterator();
            }
            
            var results = new List<T>();
            while (setIterator.HasMoreResults)
            {
                var response = await setIterator.ReadNextAsync();

                results.AddRange(response.ToList());
            }

            return results;
        }

        public async Task<IPager<T>> GetItemsWithPagingAsync<T>(Expression<Func<T, bool>> predicate, int pageIndex, int pageSize, string partitionKey = null, string containerName = null) where T : class
        {
            var container = GetContainerByName(containerName);
            // Find the item index for the Skip command:
            var itemIndex = (pageIndex - 1) * pageSize;

            var query = container.GetItemLinqQueryable<T>(requestOptions: !string.IsNullOrWhiteSpace(partitionKey) ? new QueryRequestOptions { PartitionKey = new PartitionKey(partitionKey) } : null);

            // Implement paging:
            var setIterator = query.Where(predicate).Skip(itemIndex).Take(pageSize).ToFeedIterator();
            
            var list = new List<T>();
            while (setIterator.HasMoreResults)
            {
                var response = await setIterator.ReadNextAsync();

                list.AddRange(response.ToList());
            }

            // Get total item count from the database:
            var count = container.GetItemLinqQueryable<T>(allowSynchronousQueryExecution: true, requestOptions: !string.IsNullOrWhiteSpace(partitionKey) ? new QueryRequestOptions { PartitionKey = new PartitionKey(partitionKey) } : null)
                .Where(predicate).Count();

            var results = list.ToPagerList();
            results.TotalItemCount = count;
            results.CurrentPageIndex = pageIndex;
            results.PageSize = pageSize;

            return results;
        }

        public async Task UpdateItemAsync<T>(T item, string partitionKey, string containerName = null) where T : class
        {
            var container = GetContainerByName(containerName);
            await container.UpsertItemAsync<T>(item, new PartitionKey(partitionKey));
        }

        /// <summary>
        /// Retrieves a Cosmos DB Container from the <see cref="_containers"/> collection by container name.
        /// If the container name is not specified, the default container is retrieved.
        /// </summary>
        /// <param name="containerName">The name of the container to retrieve.</param>
        /// <returns></returns>
        private Container GetContainerByName(string containerName)
        {
            return string.IsNullOrWhiteSpace(containerName) ? _containers[_defaultContainerName] : _containers[containerName];
        }
    }
}
