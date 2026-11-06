# Azure Container Apps — custo controlado a zero no escalão gratuito

A infraestrutura segue a estimativa Azure fornecida: **duas Container Apps no plano Consumption**, região **Spain Central**, uma para a API e outra para o frontend.

## O que é criado

- Um Container Apps Environment no plano Consumption.
- `ca-market-data-api-dev`: API .NET 10, ingress interno, porta 8080.
- `ca-market-data-web-dev`: React/Vite servido por Nginx, ingress público, porta 80.
- Uma User Assigned Managed Identity com federação OIDC para os dois repositórios GitHub.
- Role `Container Apps Contributor` limitada ao Resource Group.
- Sem App Service Plan, Azure Container Registry, Log Analytics Workspace, Application Insights, NAT Gateway ou perfil Dedicated.

As duas aplicações usam **0 réplicas mínimas** e no máximo uma réplica cada. O Nginx encaminha `/api` e `/health` para o endereço interno da API, pelo que o browser só comunica com o frontend e não é necessário CORS.

## Atenção ao significado de “0 €”

O ficheiro de estimativa indica duas linhas de Azure Container Apps, ambas em Spain Central, cada uma com 1 milhão de pedidos/mês, 20 pedidos simultâneos, 100 ms por pedido e 1 GiB de memória. O total apresentado é 0 €.

A concessão gratuita do Consumption é **partilhada por toda a subscrição e por mês**: 180.000 vCPU-segundos, 360.000 GiB-segundos e 2 milhões de pedidos HTTP. Duas aplicações com 1 milhão de pedidos cada usam todo o plafond gratuito de pedidos, sem margem para outras Container Apps na mesma subscrição. A estimativa não é uma garantia de faturação: consumo acima dos plafonds, tráfego de saída ou outros serviços Azure podem gerar custos.

Com 0,5 vCPU, 1 GiB e 100 ms por pedido, a aproximação para 2 milhões de pedidos é 100.000 vCPU-segundos e 200.000 GiB-segundos, abaixo dos plafonds de compute. O tempo real de execução, arranques, tráfego e consumo de outros recursos da subscrição podem alterar o resultado.

**Não configurar Dedicated workload profiles, réplicas mínimas acima de zero, VNet própria, Private Endpoint, Log Analytics ou outros serviços pagos.** A configuração de logs é `none` para evitar uma workspace faturável.

## Pré-requisitos e primeira publicação

1. Ter uma subscrição Azure ativa e permissões para criar recursos e role assignments no Resource Group.
2. Fazer merge deste branch para `main`. O workflow da API publica a imagem GHCR; o workflow do frontend já publica a sua imagem GHCR.
3. Em cada repositório, abrir o package GHCR criado e mudar a visibilidade para **Public**. Isto permite que o Container Apps Consumption descarregue as imagens sem ACR ou credenciais pagas.
4. Criar o Resource Group e aplicar o Bicep:

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

Guardar os outputs:

```bash
az deployment group show \
  --resource-group rg-market-data-dev \
  --name market-data-dev \
  --query properties.outputs \
  --output json
```

## Configuração GitHub Actions

Em **ambos** os repositórios, criar estes Actions secrets com os outputs/valores da subscrição:

- `AZURE_CLIENT_ID`: output `githubActionsClientId` do Bicep.
- `AZURE_TENANT_ID`: output `tenantId`.
- `AZURE_SUBSCRIPTION_ID`: output `subscriptionId`.

Em **ambos** os repositórios, criar estas Actions variables:

- `AZURE_RESOURCE_GROUP` = `rg-market-data-dev`
- `AZURE_DEPLOY_ENABLED` = `true` (só depois de a infraestrutura existir e os packages GHCR serem públicos).

Variável adicional por repositório:

- API: `AZURE_CONTAINER_APP_NAME` = output `apiContainerAppName`.
- Web: `AZURE_CONTAINER_APP_NAME` = output `webContainerAppName`.

A federação OIDC confia apenas nos branches `main` dos dois repositórios. Não é necessário PAT GitHub nem segredo de publicação Azure estático. Os workflows usam o `GITHUB_TOKEN` temporário para publicar no GHCR e OIDC para autenticar na Azure.

## Atualizações

Depois da configuração inicial, cada push para `main` executa testes, constrói a imagem, publica no GHCR e atualiza a respetiva Container App. O deployment fica desativado enquanto `AZURE_DEPLOY_ENABLED` não for `true`.

Para verificar réplicas:

```bash
az containerapp revision list -n ca-market-data-api-dev -g rg-market-data-dev -o table
az containerapp revision list -n ca-market-data-web-dev -g rg-market-data-dev -o table
```

Para eliminar os recursos e evitar consumo futuro:

```bash
az group delete --name rg-market-data-dev --yes --no-wait
```
