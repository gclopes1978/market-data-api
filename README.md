# Market Data API

A .NET 10 minimal Web API that exposes market data (FX rates, Euribor, government bond yields, stocks, ETFs, commodities and crypto) through provider abstractions. It is a read-only facade over public data sources, with in-memory caching for selected endpoints and resilience controls around upstream HTTP calls.

This is an R&D proof of concept with a Docker-first workflow and an Azure Container Apps deployment path. It is not intended for production or heavy traffic.

## Capabilities and status

| Area | Implementation |
|---|---|
| FX rates | Frankfurter (ECB reference rates); one-hour in-memory cache |
| Euribor | ECB monthly average rates for 1M, 3M, 6M and 12M |
| Government bonds | U.S. Treasury par yield curve and ECB euro-area AAA fitted yield curve |
| Stocks and ETFs | Yahoo Finance unofficial endpoints |
| Commodities | Yahoo Finance futures quotes |
| Crypto catalog and quotes | CoinPaprika; catalog cached for one hour and quotes for 30 seconds |
| Validation and errors | Input validation and RFC 7807 ProblemDetails |
| Resilience | Per-upstream circuit breakers, HTTP timeouts and client-IP rate limiting |
| Automated checks | .NET build/tests, line-coverage threshold and Docker health smoke test |
| Deployment | Docker image to GHCR; optional Azure Container Apps deployment via OIDC |

## API endpoints

| Method | Path | Description |
|---|---|---|
| GET | `/api/fx/{base}/{quote}` | Latest rate for a three-letter currency pair |
| GET | `/api/euribor/{tenor}` | ECB monthly-average Euribor (`1M`, `3M`, `6M`, `12M`) |
| GET | `/api/bonds/{market}/{identifier}` | Government yield: `US` accepts `US1M`…`US30Y`; `EUR` accepts `EUR3M`…`EUR30Y`; `GLOBAL` currently has no provider and returns `503` |
| GET | `/api/stocks/search?q={query}` | Search stocks by company name or ticker |
| GET | `/api/stocks/{ticker}` | Stock quote (Yahoo Finance symbols such as `AAPL`) |
| GET | `/api/etfs/{ticker}` | ETF quote (for example `SPY`, `VWCE.DE`) |
| GET | `/api/commodities/{symbol}` | Commodity futures quote |
| GET | `/api/crypto?q={query}&limit={limit}` | Active CoinPaprika assets, optionally filtered by name/symbol; limit is 1–1000 |
| GET | `/api/crypto/{id}?currency={USD|EUR}` | Crypto quote in USD (default) or EUR; use the provider ID returned by the catalog |
| GET | `/health` | Liveness/health endpoint |

Most market-data endpoints return an envelope with `data` and `metadata` (source and observation date). FX returns a flat rate object.

Errors use RFC 7807 ProblemDetails: `400` invalid input, `404` missing data, `502` upstream transport/payload failure, `503` open circuit or unsupported market, and `504` upstream timeout. Rate-limit responses use `429` and include `Retry-After`.

## Run locally

Requires the .NET 10 SDK.

```bash
dotnet restore
dotnet run --project src/MarketData.Api
```

The API listens on the ASP.NET Core development URL printed by `dotnet run`. For the common local frontend setup, configure the API to listen on port `7013`.

## Run with Docker

```bash
docker build -t market-data-api .
docker run --rm --name market-data-api -p 8080:8080 market-data-api
```

Then check:

```bash
curl http://localhost:8080/health
curl http://localhost:8080/api/fx/EUR/USD
```

## Test

```bash
dotnet test
```

Tests are designed not to depend on live data providers: endpoint tests use fake provider implementations and provider tests use stubbed HTTP handlers.

## Configuration

| Setting | Default / purpose |
|---|---|
| `Providers:Frankfurter:BaseUrl` | `https://api.frankfurter.dev/v2/` |
| `RateLimiting:Enabled` | `true` |
| `RateLimiting:PermitLimit` | `60` requests per client IP per window (`1000` in Development) |
| `RateLimiting:WindowSeconds` | `60` |
| `Resilience:CircuitBreaker:Enabled` | `true` |
| `Resilience:CircuitBreaker:FailureRatio` | `0.5` |
| `Resilience:CircuitBreaker:MinimumThroughput` | `5` calls before evaluating the ratio |
| `Resilience:CircuitBreaker:SamplingDurationSeconds` | `30` |
| `Resilience:CircuitBreaker:BreakDurationSeconds` | `30` |

Use double underscores for environment variables, for example `Providers__Frankfurter__BaseUrl`.

## Architecture

- `src/MarketData.Api`: minimal API route mapping, input validation, rate limiting and ProblemDetails middleware.
- `src/MarketData.Core`: provider interfaces and API response contracts.
- `src/MarketData.Infrastructure`: typed HTTP clients, provider implementations, dependency-injection registration, caching decorators and circuit-breaker configuration.
- `tests/MarketData.Tests`: endpoint, provider and registration tests using fakes/stubs.

The API is intentionally a read facade with light layering. Provider interfaces isolate upstream services so implementations can be replaced without changing route handlers. FX uses a one-hour memory cache; CoinPaprika's catalog uses one hour and individual quotes use 30 seconds. Other market endpoints currently call their upstream on each request.

Each upstream has an independent circuit breaker. Transport errors, timeouts, 5xx, 408 and 429 responses count as failures. The exception middleware maps upstream failures centrally; route mappings declare their upstream with `.WithUpstream(...)`. The fixed-window rate limiter and cache are in-memory and therefore per process/instance.

## CI/CD

Workflows live in `.github/workflows/`:

### Pull-request and main-branch validation — `ci.yml`

Runs for pull requests targeting `main` and pushes to `main`.

1. **Build and test**: restore, Release build, tests with Cobertura coverage, and a Markdown coverage summary in the GitHub Actions run summary.
2. **Coverage gate**: fails the job when line coverage is below 80%.
3. **PR feedback**: on pull requests, attempts to publish the coverage summary and filtered build/test failures as a PR comment. Commenting is best-effort so fork PRs with read-only tokens can still complete validation.
4. **Docker smoke test**: builds the production image, starts it, polls `/health`, prints logs on failure and always removes the container.

The workflow requests read-only repository contents by default; the build/test job grants pull-request write permission only for its optional PR comment.

### Image publishing and Azure deployment — `deploy-container-apps.yml`

Runs on pushes to `main` and `feature/azure-container-apps-zero-cost`, or manually through `workflow_dispatch`.

1. Restores and tests the .NET solution.
2. Builds and publishes the API image to GHCR, tagged with the commit SHA and, on the default branch, `latest`.
3. Deploys only when the ref is `main` and `AZURE_DEPLOY_ENABLED=true`. Azure authentication uses GitHub OIDC; the deployment job needs the configured Azure client, tenant and subscription secrets plus resource-group and Container App variables.

The deployment workflow can publish an image from the feature branch, but that branch does not satisfy the deploy job's `main` condition. A successful CI run does not itself mean an Azure deployment occurred.

## Azure deployment

Infrastructure-as-code and setup guidance are in [infra/README.md](infra/README.md). The Bicep setup targets Azure Container Apps Consumption in Spain Central, with internal API ingress and a public frontend reverse proxy. Deployment remains disabled until resources, GHCR package visibility and GitHub Actions configuration are ready. The zero-cost objective depends on remaining within the Azure free-grant limits.

## Known limitations

- Only FX, the crypto catalog and crypto quotes are cached; the cache is process-local and is lost on restart.
- Rate limiting is process-local, so multiple replicas enforce separate budgets.
- There are timeouts and circuit breakers, but no retry/backoff policy.
- Stocks, ETFs and commodities rely on unofficial Yahoo Finance endpoints that may change or throttle.
- Frankfurter is the only provider base URL currently configurable.
- Public market data may be delayed and provider terms can change. Do not use this data as the sole basis for trading decisions.
