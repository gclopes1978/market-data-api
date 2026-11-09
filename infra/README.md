# Azure Container Apps — Cost-Controlled Deployment Within the Free Tier

The infrastructure follows the supplied Azure estimate: **two Container Apps on the Consumption plan**, in the **Spain Central** region—one for the API and one for the frontend.

## What gets created

- One Container Apps Environment on the Consumption plan.
- `ca-market-data-api-dev`: .NET 10 API, internal ingress, port 8080.
- `ca-market-data-web-dev`: React/Vite served by Nginx, public ingress, port 80.
- One User Assigned Managed Identity with OIDC federation for both GitHub repositories.
- The `Container Apps Contributor` role, scoped to the Resource Group.
- No App Service Plan, Azure Container Registry, Log Analytics Workspace, Application Insights, NAT Gateway, or Dedicated workload profile.

Both applications use **0 minimum replicas** and a maximum of one replica each. Nginx routes `/api` and `/health` to the API's internal address, so the browser communicates only with the frontend and CORS is not required.

## What “€0” actually means

The estimate file lists two Azure Container Apps entries, both in Spain Central, each with 1 million requests per month, 20 concurrent requests, 100 ms per request, and 1 GiB of memory. The displayed total is €0.

The Consumption free grant is **shared across the entire subscription per month**: 180,000 vCPU-seconds, 360,000 GiB-seconds, and 2 million HTTP requests. Two applications with 1 million requests each use the entire free request allowance, leaving no headroom for other Container Apps in the same subscription. The estimate is not a billing guarantee: usage above the free grants, outbound traffic, or other Azure services may incur charges.

At 0.5 vCPU, 1 GiB, and 100 ms per request, the estimate for 2 million requests is approximately 100,000 vCPU-seconds and 200,000 GiB-seconds, which is below the compute allowances. Actual execution time, cold starts, traffic, and usage by other resources in the subscription may change the result.

**Do not configure Dedicated workload profiles, minimum replicas above zero, a custom VNet, Private Endpoints, Log Analytics, or other paid services.** The logging configuration is set to `none` to avoid a billable workspace.

## Prerequisites and first deployment

1. Have an active Azure subscription and permissions to create resources and role assignments in the Resource Group.
2. Merge this branch into `main`. The API workflow publishes the GHCR image; the frontend workflow already publishes its GHCR image.
3. In each repository, open the created GHCR package and change its visibility to **Public**. This allows Container Apps Consumption to pull the images without ACR or paid credentials.
4. Create the Resource Group and deploy the Bicep template:

```bash
az login
az account set --subscription "<SUBSCRIPTION_ID>"

az group create \
  --name rg-market-data-dev \
  --location spaincentral

az deployment group validate \
  --resource-group rg-market-data-dev \
  --template-file infra/main.bicep \
  --parameters infra/dev.bicepparam

az deployment group what-if \
  --resource-group rg-market-data-dev \
  --template-file infra/main.bicep \
  --parameters infra/dev.bicepparam

az deployment group create \
  --name market-data-dev \
  --resource-group rg-market-data-dev \
  --template-file infra/main.bicep \
  --parameters infra/dev.bicepparam
```

Save the deployment outputs:

```bash
az deployment group show \
  --resource-group rg-market-data-dev \
  --name market-data-dev \
  --query properties.outputs \
  --output json
```

## GitHub Actions configuration

In **both** repositories, create the following Actions secrets using the subscription outputs/values:

- `AZURE_CLIENT_ID`: the Bicep `githubActionsClientId` output.
- `AZURE_TENANT_ID`: the `tenantId` output.
- `AZURE_SUBSCRIPTION_ID`: the `subscriptionId` output.

In **both** repositories, create the following Actions variables:

- `AZURE_RESOURCE_GROUP` = `rg-market-data-dev`
- `AZURE_DEPLOY_ENABLED` = `true` (only after the infrastructure exists and the GHCR packages are public).

Additional variable per repository:

- API: `AZURE_CONTAINER_APP_NAME` = the `apiContainerAppName` output.
- Web: `AZURE_CONTAINER_APP_NAME` = the `webContainerAppName` output.

OIDC federation trusts only the `main` branches of the two repositories. No GitHub PAT or static Azure publishing secret is required. The workflows use the temporary `GITHUB_TOKEN` to publish to GHCR and OIDC to authenticate with Azure.

## Updates

After the initial setup, each push to `main` runs tests, builds the image, publishes it to GHCR, and updates the corresponding Container App. Deployment remains disabled until `AZURE_DEPLOY_ENABLED` is set to `true`.

To check replicas:

```bash
az containerapp revision list -n ca-market-data-api-dev -g rg-market-data-dev -o table
az containerapp revision list -n ca-market-data-web-dev -g rg-market-data-dev -o table
```

To delete the resources and prevent future consumption:

```bash
az group delete --name rg-market-data-dev --yes --no-wait
```
