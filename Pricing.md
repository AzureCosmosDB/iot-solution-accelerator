# Microsoft Azure Cosmos DB IoT solution accelerator - pricing information for the deployed solution

When you follow the [Quickstart](Quickstart.md) guide to deploy the solution accelerator, you deploy Azure resources that incur a monthly cost. This page breaks down the deployed resources and the pricing based on our recommended configuration of each. The pricing listed here may differ from the pricing you see after you deploy, based on several factors. The primary factors include the Azure region into which you deploy, pricing at the time of your deployment, and your usage of these services.

> All pricing posted on this page represent the price of these services at the time of publication, and for deployments to the `West US` Azure region.

## Required resources

Most of the solution accelerator's components are required for the core end-to-end features to work. This section focuses on these required resources. The section that follows covers optional components.

| Resource | Size | Monthly Cost | Description |
| --- | --- | --- | --- |
| API Connection | N/A | $ | Office 365 connection for the Logic App |
| Function App | Consumption-based | $ | Function App that contains functions triggered by the Cosmos DB change feed |
| Function App | Consumption-based | $ | Function App that contains a function triggered by IoT Hub and outputs to the Cosmos DB telemetry container |
| Web App | ? | $ | The example management web app that performs CRUD operations against Cosmos DB |
| App Service Plan | ? | $ | The consumption-based App Service plan for both Function Apps |
| App Service Plan | ? | $ | The App Service plan for the management web app |
| Application Insights | N/A | $ | Application Insights instance |
| Azure Cosmos DB account | Various containers (see below) | $ | The Azure Cosmos DB account configured with the SQL API |
| -- Container 1 | 15,000 RUs | $ | Container does X |
| Event Hubs Namespace | ? | $ | The Event Hubs namespace that contains the “reporting” event hub. A Cosmos DB change feed-triggered function writes to this event hub, which in turn triggers the Stream Analytics job |
| IoT Hub | ? | $ | The IoT Hub instance for managing devices and ingesting telemetry|
| Key vault | N/A | $ | Azure Key Vault contains secrets used by the Function Apps, web app, and Databricks |
| Logic App | Consumption-based| $ | The Logic App that sends notification emails. A Cosmos DB change feed-triggered function triggers the logic app via its HTTP trigger when it needs to send a notification email |
| Storage account | N/A | $ | Azure Storage account for the Cosmos DB Function App |
| Storage account | N/A | $ | Azure Storage account for the stream processing (IoT Hub) Function App |
| Storage account | N/A | $ | Azure Storage account used for cold storage of all telemetry. A Cosmos DB change feed-triggered function outputs all telemetry to a container (“telemetry”) in time-sliced path format: `/yyyy/MM/dd/HH/mm/ss-fffffff.json`. The Storage Queues service is also used on this account to provide a queue for temporarily storing alert messages when they need to be summarized on a regular schedule. The name of the queue is `alertqueue`. |
| Storage account | N/A | $ | Azure Storage account used by the Azure Machine Learning workspace |
| Azure Stream Analytics job | N/A | $ | Azure Stream Analytics job used for stream processing of events sent to Event Hubs from a Cosmos DB change feed-triggered function. The query uses window functions to create aggregates over time windows of varying length, outputting the results to Cosmos DB and Power BI |

## Optional resources

| Resource | Size | Monthly Cost | Description |
| --- | --- | --- | --- |
| Azure Databricks Service | Cluster size: | $ | Azure Databricks workspace used for advanced analytics and Event Hubs Namespace |
| Azure Machine Learning | ? | $ | The Azure Machine Learning service that manages the custom ML model training, storage, and deployment |

## Deployment and setup time

Most of the deployment is automated, but there is some manual configuration you must complete.

* Automated deployment from the ARM template: **< 15 minutes**.
* Manual configuration of core components: **1 hour**.
* (Optional) Power BI dashboard creation: **15 minutes**.
* (Optional) Power BI desktop report configuration: **5-10 minutes**.
* (Optional) Azure Databricks configuration, batch processing, and model deployment: **~30 minutes**.
