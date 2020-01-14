Cosmos DB was created from the ground up to be a cloud-native, high-speed, globally distributed managed NoSQL database service that fits nicely at the core of many modern solutions today. Its ability to provide very low latency and high throughput that can be tuned for varying workloads makes it an ideal candidate for ingesting and serving telemetry, operational, and analytical data in our reference architecture.
The Cosmos DB database contains four SQL-based containers:
•	telemetry: Used for ingesting hot vehicle telemetry data with a 90-day lifespan (TTL).
•	metadata: Stores vehicle, consignment, package, trip, and aggregate event data.
•	maintenance: The batch battery failure predictions are stored here for reporting purposes.
•	alerts: Stores alert settings that control the types of alerts and frequency in which they are sent. Also stores a history document that keeps track of when summary alerts are sent.

Each of these containers is configured based on the type of data they hold, as well as the type of workload (read-heavy, write-heavy, occasional access, etc.). Let us evaluate the configuration for each container and the design decisions behind each setting.
Alerts container
The Throughput value for this container is set to 400 RU/s. This is the lowest setting for a container, which is sufficient for the throughput requirements for alert management-related data due to low read and write usage.
The Partition Key is set to /id. Having the partition key and ID share the same value allows us to perform rapid, low-cost point lookups of data, when coupled with the ReadItemAsync method on the Container object when using the Cosmos DB SDK.
The Indexing Policy is set to the default value, which automatically indexes all fields for each document stored in the container. This is because all paths are included (remember, since we are storing JSON documents, we use paths to identify the property since they can exist within child collections in the document) by setting the value of includedPaths to "path": "/*", and the only excluded path is the internal _etag property, which is used for versioning the documents. The default Indexing Policy is:

```json
{
    "indexingMode": "consistent",
    "automatic": true,
    "includedPaths": [
        {
            "path": "/*"
        }
    ],
    "excludedPaths": [
        {
            "path": "/\"_etag\"/?"
        }
    ]
}
```

Maintenance container
The Throughput value for this container is set to 400 RU/s, which is sufficient for the throughput requirements for maintenance data due to low read and write usage.
The Partition Key is set to /vin (VIN means vehicle identification number) so we can group maintenance data by vehicle, and because the vin field is used in most queries.
The Indexing Policy is set to the default value.
Metadata container
The Throughput value for this container is set to 50000 RU/s. We are initially setting the throughput on this container to this high number of RU/s because the data generator will perform a bulk insert of metadata the first time it runs. After inserting the data, it will programmatically reduce the throughput to 15000.
The Partition Key is set to /partitionKey. This is because we store several different types of documents (records) in this container. As such, the fields vary between document types. Each document has a partitionKey field added, and an entityType field to indicate the type of document, such as "Vehicle", "Package", or "Trip". The partitionKey field is set to a field property value appropriate to the document type, such as vin for Vehicle documents. Trip documents also use vin as the partition key since trip data is retrieved by the related vehicle's VIN and is often retrieved along with vehicle data. The entityType field can be used to filter by type of document within a given partition key.
The Indexing Policy is set to the default value.
Telemetry container
The Throughput value for this container is set to 15000 RU/s, which is optimal for handling the rate of vehicle telemetry data written to this container.
The Partition Key is set to /partitionKey. The partitionKey property represents a synthetic composite partition key for the Cosmos DB container, consisting of the VIN + current year/month. Using a composite key instead of simply the VIN provides us with the following benefits:
i.	Distributing the write workload at any given point in time over a high cardinality of partition keys.
ii.	Ensuring efficient routing on queries on a given VIN - you can spread these across time, e.g. SELECT * FROM c WHERE c.partitionKey IN (“VIN123-2019-01”, “VIN123-2019-02”, …).
iii.	Scale beyond the 10GB quota for a single partition key value.
Notice that the Time to Live setting is set to On (no default). This was turned off for the other containers. Time to Live (TTL) tells Cosmos DB when to expire, or delete, the document(s) automatically. This setting can help save storage costs by removing what you no longer need. Typically, this is used on hot data or data that must be expired after a period of time due to regulatory requirements. Turning the Time to Live setting on with no default allows us to define the TTL individually for each document, giving us more flexibility in deciding which documents should expire after a set period of time. To do this, we have a ttl field on the document that is saved to this container that specifies the TTL in seconds.
Now view the Indexing Policy, which is different from the default policy the other containers use. This custom policy is optimized for write-heavy workloads by excluding all paths and only including the paths used when we query the container (vin, state, and partitionKey):

```json
{
    "indexingMode": "consistent",
    "automatic": true,
    "includedPaths": [
        {
            "path": "/vin/?"
        },
        {
            "path": "/state/?"
        },
        {
            "path": "/partitionKey/?"
        }
    ],
    "excludedPaths": [
        {
            "path": "/*"
        },
        {
            "path": "/\"_etag\"/?"
        }
    ]
}
```

Even if the only use case is device-to-cloud data ingestion, we highly recommend using IoT Hub as it provides a service that is designed for IoT device connectivity.
Communicating with IoT Hub and Cosmos DB with the data generator
The data generator project provided in the solution accelerator, FleetDataGenerator, communicates with both the Cosmos DB database and IoT Hub. When you walk through the Quickstart guide, you execute the data generator to seed Cosmos DB with data and simulate vehicles.
There are several tasks that the data generator performs, depending on the state of your environment. The first task is that the generator will create the Cosmos DB database and containers with the optimal configuration for this lab if these elements do not exist in your Cosmos DB account. When you run the generator in a few moments, this step will be skipped because you already created them at the beginning of the lab. The second task the generator performs is to seed your Cosmos DB metadata container with data if no data exists. This includes vehicle, consignment, package, and trip data. Before seeding the container with data, the generator temporarily increases the requested RU/s for the container to 50,000 for optimal data ingestion speed. After the seeding process completes, the RU/s are scaled back down to 15,000.
After the generator ensures the metadata exists, it begins simulating the specified number of vehicles. You are prompted to enter a number between 1 and 5, simulating 1, 10, 50, 100, or the number of vehicles specified in your configuration settings, respectively. For each simulated vehicle, the following tasks take place:
1.	An IoT device is registered for the vehicle, using the IoT Hub connection string and setting the device ID to the vehicle's VIN. This returns a generated device key.
2.	A new simulated vehicle instance (SimulatedVehicle) is added to a collection of simulated vehicles, each acting as an AMQP device and assigned a Trip record to simulate the delivery of packages for a consignment. These vehicles are randomly selected to have their refrigeration units fail and, out of those, some will randomly fail immediately while the others fail gradually.
3.	The simulated vehicle creates its own AMQP device instance, connecting to IoT Hub with its unique device ID (VIN) and generated device key.
4.	The simulated vehicle asynchronously sends vehicle telemetry information through its connection to IoT Hub continuously until it either completes the trip by reaching the distance in miles established by the Trip record or receiving a cancellation token.
How to provision your own devices
Within the data generator, we use the Azure IoT Hub SDK for .NET to directly register and manage devices in Azure IoT Hub. There are also SDKs available for C, Java, Node.js, Python, and iOS.
The DeviceManager.cs file within the data generator project shows how to use the Microsoft.Azure.Devices.RegistryManager to perform create, remove, update, and delete operations on devices. First, we instantiate a new instance of RegistryManager from the IoT Hub connection string:

```c#
// Create an instance of the RegistryManager from the IoT Hub connection string.
registryManager = RegistryManager.CreateFromConnectionString(connectionString);
```

The RegisterDevicesAsync method in the DeviceManager helper class demonstrates how to register a single device with IoT Hub. First, it creates a new device and sets its state to Enabled. Then, it attempts to register the new device. If an exception is returned that a device already exists, we retrieve the registered device, set the status to Enabled, and update the device state in IoT Hub with the status change:

```c#
/// <summary>
/// Register a single device with IoT Hub.
/// </summary>
/// <param name="connectionString"></param>
/// <param name="deviceId"></param>
/// <returns></returns>
public static async Task<string> RegisterDevicesAsync(string connectionString, string deviceId)
{
    //Make sure we're connected
    if (registryManager == null)
        IotHubConnect(connectionString);

    // Create a new device.
    var device = new Device(deviceId) {Status = DeviceStatus.Enabled};

    try
    {
        // Register the new device.
        device = await registryManager.AddDeviceAsync(device);
    }
    catch (Exception ex)
    {
        if (ex is DeviceAlreadyExistsException ||
            ex.Message.Contains("DeviceAlreadyExists"))
        {
            // Device already exists, get the registered device.
            device = await registryManager.GetDeviceAsync(deviceId);

            // Ensure the device is activated.
            device.Status = DeviceStatus.Enabled;

            // Update IoT Hub with the device status change.
            await registryManager.UpdateDeviceAsync(device);
        }
        else
        {
            Program.WriteLineInColor($"An error occurred while registering IoT device '{deviceId}':\r\n{ex.Message}", ConsoleColor.Red);
        }
    }

    // Return the device key.
    return device.Authentication.SymmetricKey.PrimaryKey;
}
```

The RegisterDevicesAsync method returns a symmetric key that the registered device uses to authenticate when connecting to IoT Hub. The deviceId value is the vehicle's VIN in this case. You can use whatever string value you want for your devices, as long as the values are unique. The RegisterDevicesAsync method is called from the SetupVehicleTelemetryRunTasks method in the data generator:

```c#
// Register vehicle IoT device, using its VIN as the device ID, then return the device key.
var deviceKey = await DeviceManager.RegisterDevicesAsync(iotHubConnectionString, trip.vin);
```

Each vehicle IoT device is simulated by the data generator. The simulated device is represented by the SimulatedVehicle class (SimulatedVehicle.cs). When a new simulated device is instantiated, the device ID (the vehicle's VIN) and the device's symmetric key are passed into the constructor and used when creating a new MIcrosoft.Azure.Devices.Client.DeviceClient instance:

```c#
public SimulatedVehicle(Trip trip, bool causeRefrigerationUnitFailure,
    bool immediateRefrigerationUnitFailure, int vehicleNumber,
    string iotHubUri, string deviceId, string deviceKey)
{
    _vehicleNumber = vehicleNumber;
    _trip = trip;
    _tripId = trip.id;
    _distanceRemaining = trip.plannedTripDistance + 3; // Pad a little bit extra distance to ensure all events captured.
    _causeRefrigerationUnitFailure = causeRefrigerationUnitFailure;
    _immediateRefrigerationUnitFailure = immediateRefrigerationUnitFailure;
    _IotHubUri = iotHubUri;
    DeviceId = deviceId;
    DeviceKey = deviceKey;
    _DeviceClient = DeviceClient.Create(_IotHubUri, new DeviceAuthenticationWithRegistrySymmetricKey(DeviceId, DeviceKey));
}
```

You can update this code to simulate your own IoT devices. When you are ready to use physical devices, follow the tutorials found in the IoT Hub documentation.
The best way to provision multiple IoT devices in a secure and scalable manner is to use the Azure IoT Hub Device Provisioning Service (DPS). Use the Microsoft Azure Provisioning SDKs for the best experience with using DPS.
How the data generator configures and uses Cosmos DB
There is a lot of code within the data generator project, so we'll just touch on the highlights. The code we do not cover is commented and should be easy to follow if you so desire.
Within the Main method of Program.cs, the core workflow of the data generator is executed by the following code block:

```c#
// Instantiate Cosmos DB client and start sending messages:
var trips = new List<Trip>();
using (_cosmosDbClient = new CosmosClient(cosmosDbConnectionString.ServiceEndpoint.OriginalString,
    cosmosDbConnectionString.AuthKey, connectionPolicy))
{
    await InitializeCosmosDb();

    // Find and output the container details, including # of RU/s.
    var container = _database.GetContainer(MetadataContainerName);

    var offer = await container.ReadThroughputAsync(cancellationToken);

    if (offer != null)
    {
        var currentCollectionThroughput = offer ?? 0;
        WriteLineInColor(
            $"Found collection `{MetadataContainerName}` with {currentCollectionThroughput} RU/s.",
            ConsoleColor.Green);
    }

    // Ensure the telemetry container throughput is set to 15,000 RU/s.
    var telemetryContainer = await GetContainerIfExists(TelemetryContainerName);
    await ChangeContainerPerformance(telemetryContainer, 15000);

    // Initially seed the Cosmos DB database with metadata if empty or if the user wants to generate new trips.
    await SeedDatabase(cosmosDbConnectionString, generateNewTripsOnly, cancellationToken);
    if (!generateNewTripsOnly)
    {
        trips = await GetTripsFromDatabase(numberSimulatedTrucks, container);
    }
}

if (!generateNewTripsOnly)
{
    try
    {
        // Start sending telemetry from simulated vehicles to Event Hubs:
        _runningVehicleTasks = await SetupVehicleTelemetryRunTasks(numberSimulatedTrucks,
            trips, arguments.IoTHubConnectionString);
        var tasks = _runningVehicleTasks.Select(t => t.Value).ToList();
        while (tasks.Count > 0)
        {
            try
            {
                Task.WhenAll(tasks).Wait(cancellationToken);
            }
            catch (TaskCanceledException)
            {
                //expected
            }

            tasks = _runningVehicleTasks.Where(t => !t.Value.IsCompleted).Select(t => t.Value).ToList();

        }
    }
    catch (OperationCanceledException)
    {
        Console.WriteLine("The vehicle telemetry operation was canceled.");
        // No need to throw, as this was expected.
    }
}
```

The top section of the code instantiates a new CosmosClient, using the connection string defined in either appsettings.json or the environment variables. The first call within the block is to InitializeCosmosDb(). We'll dig into this method in a moment, but it is responsible for creating the Cosmos DB database and containers if they do not exist in the Cosmos DB account. Next, we create a new Container instance, which the v3 version of the .NET Cosmos DB SDK uses for operations against a container, such as CRUD and maintenance information. For example, we call ReadThroughputAsync on the container to retrieve the current throughput (RU/s), and we pass it to GetTripsFromDatabase to read Trip documents from the container, based on the number of vehicles we are simulating. In this method, we also call the SeedDatabase method, which checks whether data currently exists and, if not, calls methods in the DataGenerator class (DataGenerator.cs file) to generate vehicles, consignments, packages, and trips, then writes the data in bulk using the BulkImporter class (BulkImporter.cs file). This SeedDatabase method executes the following on the Container instance to adjust the throughput (RU/s) to 50,000 before the bulk import, and back to 15,000 after the data seeding is complete: await container.ReplaceThroughputAsync(desiredThroughput);. Also notice that we are passing generateNewTripsOnly to the SeedDatabase method. This value is set to true if the user selects option 6 when executing the generator. When generateNewTripsOnly is set to true, any existing trips and consignments that are in pending or active status are canceled and new trips and consignments are created for existing vehicles. If no data currently exists, the SeedDatabase method generates all new data, as usual.
The try/catch block calls SetupVehicleTelemetryRunTasks to register IoT device instances for each simulated vehicle and load up the tasks from each SimulatedVehicle instance it creates. It uses Task.WhenAll to ensure all pending tasks (simulated vehicle trips) are complete, removing completed tasks from the _runningvehicleTasks list as they finish. The cancellation token is used to cancel all running tasks if you issue the cancel command (Ctrl+C or Ctrl+Break) in the console.
Scroll down the Program.cs file until you find the InitializeCosmosDb() method. Here is the code for your reference:

```c#
private static async Task InitializeCosmosDb()
{
    _database = await _cosmosDbClient.CreateDatabaseIfNotExistsAsync(DatabaseName);

    #region Telemetry container
    // Define and create a new container using the builder pattern.
    await _database.DefineContainer(TelemetryContainerName, $"/{PartitionKey}")
        // Tune the indexing policy for write-heavy workloads by only including regularly queried paths.
        // Be careful when using an opt-in policy as we are below. Excluding all and only including certain paths removes
        // Cosmos DB's ability to proactively add new properties to the index.
        .WithIndexingPolicy()
            .WithIndexingMode(IndexingMode.Consistent)
            .WithIncludedPaths()
                .Path("/vin/?")
                .Path("/state/?")
                .Path("/partitionKey/?")
                .Attach()
            .WithExcludedPaths()
                .Path("/*")
                .Attach()
            .Attach()
        .CreateIfNotExistsAsync(15000);
    #endregion

    #region Metadata container
    // Define a new container.
    var metadataContainerDefinition =
        new ContainerProperties(id: MetadataContainerName, partitionKeyPath: $"/{PartitionKey}")
        {
            // Set the indexing policy to consistent and use the default settings because we expect read-heavy workloads in this container (includes all paths (/*) with all range indexes).
            // Indexing all paths when you have write-heavy workloads may impact performance and cost more RU/s than desired.
            IndexingPolicy = { IndexingMode = IndexingMode.Consistent }
        };

    // Set initial performance to 50,000 RU/s for bulk import performance.
    await _database.CreateContainerIfNotExistsAsync(metadataContainerDefinition, throughput: 50000);
    #endregion

    #region Maintenance container
    // Define a new container.
    var maintenanceContainerDefinition =
        new ContainerProperties(id: MaintenanceContainerName, partitionKeyPath: $"/vin")
        {
            IndexingPolicy = { IndexingMode = IndexingMode.Consistent }
        };

    // Set initial performance to 400 RU/s due to light workloads.
    await _database.CreateContainerIfNotExistsAsync(maintenanceContainerDefinition, throughput: 400);
    #endregion

    #region Alerts container
    // Define a new container.
    var alertsContainerDefinition =
        new ContainerProperties(id: AlertsContainerName, partitionKeyPath: $"/id")
        {
            IndexingPolicy = { IndexingMode = IndexingMode.Consistent }
        };

    // Set initial performance to 400 RU/s due to light workloads.
    await _database.CreateContainerIfNotExistsAsync(alertsContainerDefinition, throughput: 400);
    #endregion
}
```

```sql
WITH
VehicleData AS (
    select
        vin,
        AVG(engineTemperature) AS engineTemperature,
        AVG(speed) AS speed,
        AVG(refrigerationUnitKw) AS refrigerationUnitKw,
        AVG(refrigerationUnitTemp) AS refrigerationUnitTemp,
        (case when AVG(engineTemperature) >= 400 OR AVG(engineTemperature) <= 15 then 1 else 0 end) as engineTempAnomaly,
        (case when AVG(engineoil) <= 18 then 1 else 0 end) as oilAnomaly,
        (case when AVG(transmission_gear_position) <= 3.5 AND
            AVG(accelerator_pedal_position) >= 50 AND
            AVG(speed) >= 55 then 1 else 0 end) as aggressiveDriving,
        (case when AVG(refrigerationUnitTemp) >= 30 then 1 else 0 end) as refrigerationTempAnomaly,
        System.TimeStamp() as snapshot
    from events TIMESTAMP BY [timestamp]
    GROUP BY
        vin,
        TumblingWindow(Duration(second, 30))
),
VehicleDataAll AS (
    select
        AVG(engineTemperature) AS engineTemperature,
        AVG(speed) AS speed,
        AVG(refrigerationUnitKw) AS refrigerationUnitKw,
        AVG(refrigerationUnitTemp) AS refrigerationUnitTemp,
        COUNT(*) AS eventCount,
        (case when AVG(engineTemperature) >= 318 OR AVG(engineTemperature) <= 15 then 1 else 0 end) as engineTempAnomaly,
        (case when AVG(engineoil) <= 20 then 1 else 0 end) as oilAnomaly,
        (case when AVG(transmission_gear_position) <= 4 AND
            AVG(accelerator_pedal_position) >= 50 AND
            AVG(speed) >= 55 then 1 else 0 end) as aggressiveDriving,
        (case when AVG(refrigerationUnitTemp) >= 22.5 then 1 else 0 end) as refrigerationTempAnomaly,
        System.TimeStamp() as snapshot
    from events t TIMESTAMP BY [timestamp]
    GROUP BY
        TumblingWindow(Duration(second, 10))
)
-- INSERT INTO POWER BI
SELECT
    *
INTO
    powerbi
FROM
    VehicleDataAll
-- INSERT INTO COSMOS DB
SELECT
    *,
    entityType = 'VehicleAverage',
    partitionKey = vin
INTO
    cosmosdb
FROM
    VehicleData
```

Data management through the web app

1.	Open Startup.cs within the FleetManagementWebApp project. Scroll down to the bottom of the file to find the following code:

```c#
CosmosClientBuilder clientBuilder = new CosmosClientBuilder(cosmosDbConnectionString.ServiceEndpoint.OriginalString, cosmosDbConnectionString.AuthKey);
CosmosClient client = clientBuilder
    .WithConnectionModeDirect()
    .Build();
CosmosDbService cosmosDbService = new CosmosDbService(client, databaseName, containerName);
```

This code uses the .NET SDK for Cosmos DB v3 to initialize the CosmosClient instance that is added to the IServiceCollection as a singleton for dependency injection and object lifetime management.
2.	Open CosmosDBService.cs under the Services folder of the FleetManagementWebApp project to find the following code block:

```c#
var setIterator = query.Where(predicate).Skip(itemIndex).Take(pageSize).ToFeedIterator();
```

Here we are using the newly added Skip and Take methods on the IOrderedQueryable object (query) to retrieve just the records for the requested page. The predicate represents the LINQ expression passed in to the GetItemsWithPagingAsync method to apply filtering.
3.	Scroll down a little further to the following code:

```c#
var count = this._container.GetItemLinqQueryable<T>(allowSynchronousQueryExecution: true, requestOptions: !string.IsNullOrWhiteSpace(partitionKey) ? new QueryRequestOptions { PartitionKey = new PartitionKey(partitionKey) } : null)
    .Where(predicate).Count();
```

In order to know how many pages we need to navigate, we must know the total item count with the current filter applied. To do this, we retrieve a new IOrderedQueryable results from the Container, pass the filter predicate to the Where method, and return the Count to the count variable. For this to work, you must set allowSynchronousQueryExecution to true, which we do with our first parameter to the GetItemLinqQueryable method.

4.	Open VehiclesController.cs under the Controllers folder of the FleetManagementWebApp project to review the following code:

```c#
private readonly ICosmosDbService _cosmosDbService;
private readonly IHttpClientFactory _clientFactory;
private readonly IConfiguration _configuration;
private readonly Random _random = new Random();

public VehiclesController(ICosmosDbService cosmosDbService, IHttpClientFactory clientFactory, IConfiguration configuration)
{
    _cosmosDbService = cosmosDbService;
    _clientFactory = clientFactory;
    _configuration = configuration;
}

public async Task<IActionResult> Index(int page = 1, string search = "")
{
    if (search == null) search = "";
    //var query = new QueryDefinition("SELECT TOP @limit * FROM c WHERE c.entityType = @entityType")
    //    .WithParameter("@limit", 100)
    //    .WithParameter("@entityType", WellKnown.EntityTypes.Vehicle);
    // await _cosmosDbService.GetItemsAsync<Vehicle>(query);

    var vm = new VehicleIndexViewModel
    {
        Search = search,
        Vehicles = await _cosmosDbService.GetItemsWithPagingAsync<Vehicle>(
            x => x.entityType == WellKnown.EntityTypes.Vehicle &&
                  (string.IsNullOrWhiteSpace(search) ||
                  (x.vin.ToLower().Contains(search.ToLower()) || x.stateVehicleRegistered.ToLower() == search.ToLower())), page, 10)
    };

    return View(vm);
}
```

We are using dependency injection with this controller, just as we did with one of our Function Apps earlier. The ICosmosDbService, IHttpClientFactory, and IConfiguration services are injected into the controller through the controller's constructor. The CosmosDbService is the class whose code you reviewed in the previous step. The CosmosClient is injected into it through its constructor.
The Index controller action method uses paging, which it implements by calling the ICosmosDbService.GetItemsWithPagingAsync method you updated in the previous step. Using this service in the controller helps abstract the Cosmos DB query implementation details and business rules from the code in the action methods, keeping the controller lightweight and the code in the service reusable across all the controllers.
Notice that the paging query does not include a partition key, making the Cosmos DB query cross-partition, which is needed to be able to query across all the documents. If this query ends up being used a lot with the passed in search value, causing a higher than desired RU usage on the container per execution, then you might want to consider alternate strategies for the partition key, such as a combination of vin and stateVehicleRegistered. However, since most of our access patterns for vehicles in this container use the VIN, we are using it as the partition key right now. You will see code further down in the method that explicitly passes the partition key value.

5.	Scroll down in the VehiclesController.cs file to find the following code:

```c#
await _cosmosDbService.DeleteItemAsync<Vehicle>(item.id, item.partitionKey);
```

Here we are doing a hard delete by completely removing the item. In a real-world scenario, we would most likely perform a soft delete, which means updating the document with a deleted property and ensuring all filters exclude items with this property. Plus, we'd soft delete related records, such as trips. Soft deletions make it much easier to recover a deleted item if needed in the future. This is an enhancement you should consider adding to your solution.

Open Startup.cs in the Web Application project to see how we register the middleware within the ConfigureServices method:

```c#
// Sign-in users with the Microsoft identity platform
services.AddMicrosoftIdentityPlatformAuthentication(Configuration);
services.AddControllersWithViews(options =>
{
    var policy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
    options.Filters.Add(new AuthorizeFilter(policy));
});
```

Refer to the following GitHub repo (https://github.com/Azure-Samples/active-directory-aspnetcore-webapp-openidconnect-v2) if you are interested in how to build upon the groundwork we’ve provided, such as:
•	Sign-in users in any organization, not just your own.
•	Allow users to sign in with their Microsoft personal account, not just their work or school accounts.
•	Sign-in users through national or sovereign clouds.
•	Sign-in users with their social identities.
•	Update the Web App to call Microsoft Graph.
•	Learn how to call several Microsoft APIs and handle conditional access, incremental consent, and claims challenge.
•	Learn how to update the Web App to call your own Web APIs.
•	Add authorization to your Web App, restricting portions of it to users based on their application roles or to which Azure AD groups they belong.
 
Here is the portion of the Stream Analytics query that outputs to the Power BI sink (powerbi):

```sql
-- INSERT INTO POWER BI
SELECT
    *
INTO
    powerbi
FROM
    VehicleDataAll
```

When you follow the Quickstart guide’s instructions to add the Power BI output in Stream Analytics, you specify the Dataset name and the Table name. Power BI automatically registers the dataset as a custom streaming dataset, which means that the data continuously flows into the associated table and can update a live dashboard.
After an initial lag of a few minutes between adding the Power BI output in Stream Analytics, starting the query, and sending new data (in this case, from the data generator), the dataset appears in the Power BI workspace.
 
Here is an example of the Source dialog for the Trips query:
 
The Trips data source has a SQL statement defined that returns only the fields we need, and applies some aggregates:

```sql
SELECT c.id, c.vin, c.consignmentId, c.plannedTripDistance,
c.location, c.odometerBegin, c.odometerEnd, c.temperatureSetting,
c.tripStarted, c.tripEnded, c.status,
(
    SELECT VALUE Count(1) 
    FROM n IN c.packages
) AS numPackages,
(
    SELECT VALUE MIN(n.storageTemperature) 
    FROM n IN c.packages
) AS packagesStorageTemp,
(
    SELECT VALUE Count(1)
    FROM n IN c.packages
    WHERE n.highValue = true
) AS highValuePackages,
c.consignment.customer,
c.consignment.deliveryDueDate
FROM c where c.entityType = 'Trip'
and c.status in ('Active', 'Delayed', 'Completed')
```

The main report tab (Trip/Consignments) contains a map visualization, line charts displaying the refrigeration unit temperature and engine temperature over time, and slicers (status filter, customer filter, and VIN list) to filter the data for the visualizations.

Deploying to Azure Container Instances
The Model Deployment notebook defines the following Python function to deploy the trained model to Azure Container Instances (ACI):

```python
def deployModelAsWebService(ws, model_name, 
                scoring_script_filename="scoring_service.py", 
                conda_packages=['numpy','pandas','scikit-learn','py-xgboost<=0.80'],
                pip_packages=['azureml-train-automl==1.0.60','inference-schema'],
                conda_file="dependencies.yml", runtime="python",
                cpu_cores=1, memory_gb=1, tags={'name':'scoring'},
                description='Forecast',
                service_name = "scoring"
               ):
    # retrieve a reference to the already registered model
    print("Retrieving model reference...")
    registered_model = Model(workspace=ws, name=model_name)

    # create a Conda dependencies environment file
    print("Creating conda dependencies file locally...")
    from azureml.core.conda_dependencies import CondaDependencies 
    mycondaenv = CondaDependencies.create(conda_packages=conda_packages, pip_packages=pip_packages)
    with open(conda_file,"w") as f:
        f.write(mycondaenv.serialize_to_string())
        
    # create container image configuration
    print("Creating container image configuration...")
    from azureml.core.image import ContainerImage
    image_config = ContainerImage.image_configuration(execution_script = scoring_script_filename,
                                                      runtime = runtime,
                                                      conda_file = conda_file)
    
    # create ACI configuration
    print("Creating ACI configuration...")
    from azureml.core.webservice import AciWebservice, Webservice
    aci_config = AciWebservice.deploy_configuration(
        cpu_cores = cpu_cores, 
        memory_gb = memory_gb, 
        tags = tags, 
        description = description)

    # deploy the webservice to ACI
    print("Deploying webservice to ACI...")
    webservice = Webservice.deploy_from_model(
      workspace=ws, 
      name=service_name, 
      deployment_config=aci_config,
      models = [registered_model], 
      image_config=image_config
    )
    webservice.wait_for_deployment(show_output=True)
    
    return webservice
```

The function performs the following:
•	Retrieves a reference to the trained ML model from the Azure Machine Learning workspace.
•	Creates a Conda dependency file that references the Conda and pip packages and versions defined in the conda_packages and pip_packages parameters, respectively.
•	Creates the Docker container image configuration by defining the scoring script (scoring_service.py), Python runtime, and the Conda file.
•	Creates the Azure Container Instances configuration. ACI is the deployment target for the web service that hosts the model for real-time scoring.
•	Deploys the web service to ACI and returns a reference to the web service after the deployment completes.
The scoring file (scoring_service.py) that gets added to the web service can be used for any of the valid deployment targets:
#save script to  $deployment_folder/scoring_service.py

```python
scoring_service = """
import json
import pickle
import numpy as np
import pandas as pd
import azureml.train.automl
from sklearn.externals import joblib
from azureml.core.model import Model

from inference_schema.schema_decorators import input_schema, output_schema
from inference_schema.parameter_types.numpy_parameter_type import NumpyParameterType
from inference_schema.parameter_types.pandas_parameter_type import PandasParameterType


input_sample = pd.DataFrame(data=[{"Date":"2013-01-01T00:00:00.000Z","Battery_ID":0,"Battery_Age_Days":0,"Daily_Trip_Duration":67.8456075842}])


def init():
    global model
    # This name is model.id of model that we want to deploy deserialize the model file back
    # into a sklearn model
    model_path = Model.get_model_path(model_name = 'batt-cycles-6')
    model = joblib.load(model_path)


@input_schema('data', PandasParameterType(input_sample))
def run(data):
    try:
        #y_query = data.pop('y_query').values
        #result = model.forecast(data, y_query)
        result = model.predict(data)
    except Exception as e:
        result = str(e)
        return json.dumps({"error": result})

    #forecast_as_list = result[0].tolist()
    #index_as_df = result[1].index.to_frame().reset_index(drop=True)
    
    #return json.dumps({"forecast": forecast_as_list,   # return the minimum over the wire: 
    #                   "index": json.loads(index_as_df.to_json(orient='records'))  # no forecast and its featurized values
    #                  })
    return json.dumps({"result": result.tolist()})
"""

with open("scoring_service.py", "w") as file:
  file.write(scoring_service)
```

Modify the deployment target to Azure Kubernetes Service (AKS)
If you prefer to deploy the model to AKS, replace the ACI portions of the deployModelAsWebService method as follows:
ACI portions to replace:

```python
# create ACI configuration
print("Creating ACI configuration...")
from azureml.core.webservice import AciWebservice, Webservice
aci_config = AciWebservice.deploy_configuration(
    cpu_cores = cpu_cores, 
    memory_gb = memory_gb, 
    tags = tags, 
    description = description)

# deploy the webservice to ACI
print("Deploying webservice to ACI...")
webservice = Webservice.deploy_from_model(
  workspace=ws, 
  name=service_name, 
  deployment_config=aci_config,
  models = [registered_model], 
  image_config=image_config
)
webservice.wait_for_deployment(show_output=True)
```

Replace with the following to target AKS as the deployment target:
First, create an AKS cluster with the Azure Machine Learning SDK:
from azureml.core.compute import AksCompute, ComputeTarget

```python
# Use the default configuration (you can also provide parameters to customize this)
prov_config = AksCompute.provisioning_configuration()

aks_name = 'myaks'
# Create the cluster
aks_target = ComputeTarget.create(workspace = ws,
                                    name = aks_name,
                                    provisioning_configuration = prov_config)

# Wait for the create process to complete
aks_target.wait_for_completion(show_output = True)
```

Once you have created the AKS cluster, you can now deploy to it using the SDK:

```python
from azureml.core.webservice import AksWebservice, Webservice
aks_target = AksCompute(ws,"myaks")
deployment_config = AksWebservice.deploy_configuration(cpu_cores = 1, memory_gb = 1)
service = Model.deploy(ws, "aksservice", [model], inference_config, deployment_config, aks_target)
service.wait_for_deployment(show_output = True)
```
Secret storage with Azure Key Vault

Azure Key Vault is used to securely store and tightly control access to tokens, passwords, certificates, API keys, and other secrets. Also, secrets stored in Azure Key Vault are centralized, giving the added benefits of only needing to update secrets in one place, such as an application key value after recycling the key for security purposes.
In this solution accelerator, we store application secrets in Azure Key Vault, then configure the Function Apps, Web App, and Azure Databricks to connect to Key Vault securely. These services connect to Key Vault using managed identities and a Key Vault-backed Databricks secret store, respectively.
When you deployed the solution accelerator using the ARM template, the template created the secrets in Key Vault and added references to them in the application settings within the Function Apps and Web App. Here is one example from the demoDeploy.json ARM template that adds a new secret to Key Vault that stores the Cosmos DB connection string:

```json
{
    "type": "Microsoft.KeyVault/vaults/secrets",
    "name": "[concat(variables('keyVaultName'), '/', 'CosmosDBConnection')]",
    "apiVersion": "2018-02-14",
    "location": "[variables('location')]",
    "dependsOn": [
        "[resourceId('Microsoft.Resources/deployments/', 'labDeployment')]"
    ],
    "properties": {
        "value": "[reference('labDeployment').outputs.CosmosDBConnection.value]"
    }
}
```

The template references this and the other secrets later on to add a special Key Vault path to these secrets to app settings within the Web App and Function Apps. Here is an example:

```json
{
    "name": "CosmosDBConnection",
    "value": "[concat('@Microsoft.KeyVault(SecretUri=', reference(concat(resourceId('Microsoft.KeyVault/vaults', variables('keyVaultName')), '/secrets/', 'CosmosDBConnection')).secretUriWithVersion, ')')]"
}
```

access policies. Here is the portion of the template that creates the three Key Vault access policies:

```json
"accessPolicies": [
    {
        "tenantId": "[reference(concat(resourceId('Microsoft.Web/sites', parameters('iotWebAppName')), '/providers/Microsoft.ManagedIdentity/Identities/default'), '2018-11-30').tenantId]",
        "objectId": "[reference(concat(resourceId('Microsoft.Web/sites', parameters('iotWebAppName')), '/providers/Microsoft.ManagedIdentity/Identities/default'), '2018-11-30').principalId]",
        "permissions": {
            "secrets": [
                "get"
            ]
        }
    },
    {
        "tenantId": "[reference(concat(resourceId('Microsoft.Web/sites', parameters('functionAppStreamProcessingName')), '/providers/Microsoft.ManagedIdentity/Identities/default'), '2018-11-30').tenantId]",
        "objectId": "[reference(concat(resourceId('Microsoft.Web/sites', parameters('functionAppStreamProcessingName')), '/providers/Microsoft.ManagedIdentity/Identities/default'), '2018-11-30').principalId]",
        "permissions": {
            "secrets": [
                "get"
            ]
        }
    },
    {
         "tenantId": "[reference(concat(resourceId('Microsoft.Web/sites', parameters('functionAppCosmosDBProcessingName')), '/providers/Microsoft.ManagedIdentity/Identities/default'), '2018-11-30').tenantId]",
        "objectId": "[reference(concat(resourceId('Microsoft.Web/sites', parameters('functionAppCosmosDBProcessingName')), '/providers/Microsoft.ManagedIdentity/Identities/default'), '2018-11-30').principalId]",
        "permissions": {
            "secrets": [
                "get"
            ]
         }
    }
]
```