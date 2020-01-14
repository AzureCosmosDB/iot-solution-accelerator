# Microsoft Azure Cosmos DB IoT solution accelerator - pricing information for the deployed solution

When you follow the [Quickstart](Quickstart.md) guide to deploy the solution accelerator, you deploy Azure resources that incur a monthly cost. This page breaks down the deployed resources and the pricing based on our recommended configuration of each. The pricing listed here may differ from the pricing you see after you deploy, based on several factors. The primary factors include the Azure region into which you deploy, pricing at the time of your deployment, and your usage of these services.

> All pricing posted on this page represent the price ($USD) of these services at the time of publication, and for deployments to the `West US` Azure region within a [Pay-As-You-Go](https://azure.microsoft.com/offers/ms-azr-0003p/) subscription. For the most up-to-date pricing, use the [pricing calculator](https://azure.microsoft.com/pricing/calculator/) to estimate the cost of the services listed below, based on your subscription type, Azure region, and the specified size configuration. You can often save money by selecting a 1 or 3-year reserved capacity for the services.

## Required resources

Most of the solution accelerator's components are required for the core end-to-end features to work. This section focuses on these required resources. The section that follows covers optional components.

| Resource | Size | Monthly Cost | Description |
| --- | --- | --- | --- |
| API Connection | N/A | $0 | Office 365 connection for the Logic App |
| Azure Functions | Consumption-based | < $5 (Depends on usage. The first 400,000 GB/s of execution and 1,000,000 executions are free.) | Function App that contains functions triggered by the Cosmos DB change feed |
| Azure Functions | Consumption-based | < $5 | Function App that contains a function triggered by IoT Hub and outputs to the Cosmos DB telemetry container |
| App Service | Standard: S1 | $73 | The example management web app that performs CRUD operations against Cosmos DB |
| Application Insights | N/A | N/A (not enough volume to incur a cost) | Application Insights instance |
| Azure Cosmos DB account | Various containers (see below) | - | The Azure Cosmos DB account configured with the SQL API |
| - Container: **telemetry** | 15,000 RUs | $876 | Used for ingesting hot vehicle telemetry data with a 90-day lifespan (TTL). |
| - Container: **metadata** | 15,000 RUs | $876 | Stores vehicle, consignment, package, trip, and aggregate event data. |
| - Container: **maintenance** | 400 RUs | $23.36 | The batch battery failure predictions are stored here for reporting purposes. |
| - Container: **alerts** | 400 RUs | $23.36 | Stores alert settings that control the types of alerts and frequency in which they are sent. It also stores a history document that keeps track of when summary alerts are sent. |
| - Container: **leases** | 400 RUs | $23.36 | Stores lease information for the Azure Functions that consume the change feed. |
| Event Hubs | Standard tier, 10 million events @ 1 throughput unit | $22.18 | The Event Hubs namespace that contains the "reporting" event hub. A Cosmos DB change feed-triggered function writes to this event hub, which in turn triggers the Stream Analytics job |
| IoT Hub | B2 - Basic, 4 IoT Hub units | $200 | The IoT Hub instance for managing devices and ingesting telemetry|
| Key vault | Consumption-based | $3 @ 1,000,000 operations | Azure Key Vault contains secrets used by the Function Apps, web app, and Databricks |
| Logic App | Consumption-based| $4.50 @ 1,000 executions/day | The Logic App that sends notification emails. A Cosmos DB change feed-triggered function triggers the logic app via its HTTP trigger when it needs to send a notification email |
| Storage account | Performance tier: Standard, General Purpose (V1), 100 GB capacity | $2.44 | Azure Storage account for the Cosmos DB Function App |
| Storage account | Performance tier: Standard, General Purpose (V1), 100 GB capacity | $2.44 | Azure Storage account for the stream processing (IoT Hub) Function App |
| Storage account | Performance tier: Standard, General Purpose (V1), 100 GB capacity, 10,000 Queue Class 1 & 10,000 Queue Class 2 operations | $11.70 | Azure Storage account used for cold storage of all telemetry. A Cosmos DB change feed-triggered function outputs all telemetry to a container ("telemetry") in time-sliced path format: `/yyyy/MM/dd/HH/mm/ss-fffffff.json`. The Storage Queues service is also used on this account to provide a queue for temporarily storing alert messages when they need to be summarized on a regular schedule. The name of the queue is `alertqueue`. |
| Azure Stream Analytics job | 3 streaming units | $240.90 | Azure Stream Analytics job used for stream processing of events sent to Event Hubs from a Cosmos DB change feed-triggered function. The query uses window functions to create aggregates over time windows of varying length, outputting the results to Cosmos DB and Power BI |

**Total monthly cost as configured**: $2,379.80

## Optional resources

The optional resources are not required to support the core features of the solution accelerator, but we recommend them for the best experience.

| Resource | Size | Monthly Cost | Description |
| --- | --- | --- | --- |
| Azure Databricks Service | Pricing tier: Premium, Cluster size: DS3 v2 | $41.49 (if running the cluster 2 hours/day for batch processing) | Azure Databricks workspace used for advanced analytics and Event Hubs Namespace |
| Azure Machine Learning | Basic workspace edition, no VMs used, reuses existing Azure Key Vault and Application Insights | $0 | The Azure Machine Learning service that manages the custom ML model training, storage, and deployment |
| Azure Container Registry | Basic, 10 GB bandwidth | $5.43 | Container Registry is deployed with the Azure Machine Learning service and is used for storing the Docker image used to deploy the machine learning model to ACI |
| Azure Container Instances (ACI) | Linux, 2 container groups @ 2,592,000‬ seconds duration/month | $64.73 | Hosts the Docker-based scoring web service for the deployed machine learning model |
| Storage account | Performance tier: Standard, General Purpose (V1), 100 GB capacity | $2.44 | Azure Storage account used by the Azure Machine Learning workspace |
| Power BI online | Pro | $9.99/user ([see pricing](https://powerbi.microsoft.com/pricing/)) | The Power BI online service is used for the real-time dashboard. However, the Power BI Desktop report does not require a Power BI Pro subscription, and is free of charge |

**Total monthly cost as configured**: $123.09

## Cost optimizations

Depending on your throughput and processing requirements, you can reduce the size of some of the core services to reduce your overall monthly cost. For example, if you have fewer than 50 users for the management web app, you can scale down the App Service to the Shared tier with the D1 instance for $9.49/month, saving around $64/month.

If your IoT device throughput requirements are relatively low, you can save money by reducing the RUs for the `telemetry` and `maintenance` containers. Use the handy Azure Cosmos DB [capacity planner](https://cosmos.azure.com/capacitycalculator/) to help estimate your required throughput, as [outlined in the Azure Cosmos DB documentation](https://docs.microsoft.com/azure/cosmos-db/estimate-ru-with-capacity-planner). You can also reduce the number of IoT Hub units assigned to the IoT Hub account, based on your IoT device throughput requirements. If you make reductions to the Cosmos DB container throughput and the number of IoT Hub units, then you can also consider reducing the number of streaming units assigned to the Azure Stream Analytics job. The combination of these optimizations can save you a significant amount of money each month.

If your Cosmos DB throughput requirements fluctuate considerably during different times of the day, consider recreating the containers as configured and enabling [autopilot mode](https://docs.microsoft.com/azure/cosmos-db/provision-throughput-autopilot) to adjust the throughput based on actual usage automatically. This way, you only pay for the resources that your workloads need on a per-hour basis.
