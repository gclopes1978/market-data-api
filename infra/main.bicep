// Deploys the shared Azure infrastructure for both Market Data applications.
// The template is scoped to an existing resource group; create the resource group before deployment.
targetScope = 'resourceGroup'

// Azure region for the Container Apps environment and managed identity.
@description('Azure region selected in the cost estimate.')
param location string = 'spaincentral'

// The current template intentionally supports the development environment only.
@description('Environment suffix.')
@allowed([
  'dev'
])
param environmentName string = 'dev'

// GitHub repository coordinates used to restrict federated authentication to the two main branches.
@description('GitHub owner and repository names used by OIDC.')
param githubOwner string = 'gclopes1978'
param apiRepository string = 'market-data-api'
param webRepository string = 'market-data-web'

// Keep Azure resource names consistent and environment-specific.
var environmentResourceName = 'cae-market-data-${environmentName}'
var apiAppName = 'ca-market-data-api-${environmentName}'
var webAppName = 'ca-market-data-web-${environmentName}'
var identityName = 'id-market-data-github-actions-${environmentName}'

// Built-in Azure role that allows GitHub Actions to deploy/update Container Apps.
var containerAppsContributorRoleId = '358470bc-b998-42bd-ab17-a7e34c199c0f'

// One user-assigned identity is shared by the API and Web deployment workflows.
// GitHub Actions obtains short-lived Azure tokens through OIDC; no client secret is stored in GitHub.
resource githubActionsIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: identityName
  location: location
}

// Trust only the API repository's main branch when exchanging a GitHub OIDC token for an Azure token.
resource apiFederatedCredential 'Microsoft.ManagedIdentity/userAssignedIdentities/federatedIdentityCredentials@2023-01-31' = {
  parent: githubActionsIdentity
  name: 'github-api-main'
  properties: {
    issuer: 'https://token.actions.githubusercontent.com'
    subject: 'repo:${githubOwner}/${apiRepository}:ref:refs/heads/main'
    audiences: [
      'api://AzureADTokenExchange'
    ]
  }
}

// Trust only the Web repository's main branch; pull-request branches are not granted deployment access.
resource webFederatedCredential 'Microsoft.ManagedIdentity/userAssignedIdentities/federatedIdentityCredentials@2023-01-31' = {
  parent: githubActionsIdentity
  name: 'github-web-main'
  properties: {
    issuer: 'https://token.actions.githubusercontent.com'
    subject: 'repo:${githubOwner}/${webRepository}:ref:refs/heads/main'
    audiences: [
      'api://AzureADTokenExchange'
    ]
  }
}

// Grant the shared identity the permissions required by the deployment workflows.
// The assignment is scoped to this resource group rather than the whole subscription.
resource githubActionsRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(resourceGroup().id, githubActionsIdentity.properties.principalId, containerAppsContributorRoleId)
  properties: {
    principalId: githubActionsIdentity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', containerAppsContributorRoleId)
  }
}

// Shared hosting environment for the API and Web Container Apps.
// Zone redundancy is disabled to keep the development footprint/cost lower.
resource containerAppsEnvironment 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: environmentResourceName
  location: location
  properties: {
    zoneRedundant: false
  }
}

// Internal API service: it is not exposed directly to the public internet.
// The Web app reaches it over the Container Apps environment using its internal FQDN.
resource apiApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: apiAppName
  location: location
  properties: {
    managedEnvironmentId: containerAppsEnvironment.id
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: false
        targetPort: 8080
        transport: 'auto'
        allowInsecure: true
      }
    }
    template: {
      containers: [
        {
          name: 'market-data-api'
          image: 'ghcr.io/${toLower(githubOwner)}/${apiRepository}:latest'
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          // Readiness keeps traffic away from the API until its health endpoint responds.
          probes: [
            {
              type: 'Readiness'
              httpGet: {
                path: '/health'
                port: 8080
                scheme: 'HTTP'
              }
              initialDelaySeconds: 5
              periodSeconds: 10
            }
          ]
        }
      ]
      // Scale to zero when idle and cap at one replica for the low-cost development setup.
      scale: {
        minReplicas: 0
        maxReplicas: 1
        rules: [
          {
            name: 'http-concurrency'
            http: {
              metadata: {
                concurrentRequests: '20'
              }
            }
          }
        ]
      }
    }
  }
}

// Public-facing Web application. Nginx serves the frontend and proxies API requests internally.
resource webApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: webAppName
  location: location
  properties: {
    managedEnvironmentId: containerAppsEnvironment.id
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: true
        targetPort: 80
        transport: 'auto'
        allowInsecure: false
      }
    }
    template: {
      containers: [
        {
          name: 'market-data-web'
          image: 'ghcr.io/${toLower(githubOwner)}/${webRepository}:latest'
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          // Pass the API's internal address to the Web container; it is not a public API URL.
          env: [
            {
              name: 'API_UPSTREAM'
              value: 'http://${apiApp.properties.configuration.ingress.fqdn}'
            }
          ]
          // The root path is used as the Web container readiness check.
          probes: [
            {
              type: 'Readiness'
              httpGet: {
                path: '/'
                port: 80
                scheme: 'HTTP'
              }
              initialDelaySeconds: 5
              periodSeconds: 10
            }
          ]
        }
      ]
      // Match the API's low-cost scale-to-zero development configuration.
      scale: {
        minReplicas: 0
        maxReplicas: 1
        rules: [
          {
            name: 'http-concurrency'
            http: {
              metadata: {
                concurrentRequests: '20'
              }
            }
          }
        ]
      }
    }
  }
}

// Deployment outputs consumed by the setup guide and GitHub Actions configuration.
output apiContainerAppName string = apiApp.name
output webContainerAppName string = webApp.name
output webUrl string = 'https://${webApp.properties.configuration.ingress.fqdn}'
output apiInternalFqdn string = apiApp.properties.configuration.ingress.fqdn
output githubActionsClientId string = githubActionsIdentity.properties.clientId
output tenantId string = tenant().tenantId
output subscriptionId string = subscription().subscriptionId
