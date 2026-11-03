# market-data-api

A small .NET 9 Web API that exposes market data (FX rates, Euribor, government bond yields, stocks, ETFs, commodities and crypto) through a facade over free providers, with in-memory caching on part of the endpoints (see Known limitations).

This is an R&D proof of concept, built to explore a Docker-first workflow that is ready to move to Azure while keeping the running cost at zero. It is not intended for production or heavy traffic.

## Status

| Area | State |
|---|---|
| FX rates (Frankfurter, ECB reference rates) | Done |
| Euribor (ECB), US Treasury and euro area bond yields | Done |
| Stocks, ETFs, commodities (Yahoo Finance, unofficial API) | Done |
| Crypto catalog and quotes (CoinPaprika) | Done |
| Unit and endpoint tests (no network) | Done |
| CI (build and test on every PR, Docker smoke test) | Done |
| Caching for Euribor, bonds, stocks, ETFs and commodities | Planned |
| Retries and circuit breaker for providers | Planned |
| Fed rates, EUR short-term rate, global bond market | Planned |
| Deployment to Azure with Bicep | Planned |

## Endpoints

| Method | Path | Description |
|---|---|---|
| GET | `/api/fx/{base}/{quote}` | Latest rate for a currency pair |
| GET | `/api/euribor/{tenor}` | Euribor monthly average (`1M`, `3M`, `6M`, `12M`) |
| GET | `/api/bonds/{market}/{identifier}` | Government bond yield. `US`: `US1M`...`US30Y` (U.S. Treasury). `EUR`: `EUR3M`...`EUR30Y` (ECB euro area AAA yield curve, a fitted curve rather than a single bond). `GLOBAL` has no provider yet (503) |
| GET | `/api/stocks/{ticker}`, `/api/etfs/{ticker}` | Quote for a stock or ETF (Yahoo Finance symbols, e.g. `AAPL`, `SPY`, `VWCE.DE`) |
| GET | `/api/commodities/{symbol}` | Commodity futures quote |
| GET | `/api/crypto` | All active cryptocurrencies ordered by market-cap rank (CoinPaprika, no API key). Optional `q` (name or symbol) and `limit` (1-1000). Each item has the `id` used by the quote endpoint |
| GET | `/api/crypto/{id}` | Latest quote of one cryptocurrency (`id` as listed above, e.g. `btc-bitcoin`). Optional `currency`: `USD` (default) or `EUR`. Returns price, 24h change, 24h volume and market cap |
| GET | `/health` | Liveness check |

Errors use RFC 7807 ProblemDetails: `400` invalid input, `404` not found, `502` provider failure or invalid provider response, `503` no provider for the requested market (e.g. `GLOBAL` bonds), `504` provider timeout.

## Run with Docker

```powershell
docker build -t marketdata-api .
docker run --rm --name marketdata-api -p 8080:8080 marketdata-api
```

```powershell
curl http://localhost:8080/health
curl http://localhost:8080/api/fx/EUR/USD
```

## Run locally

```powershell
dotnet run --project src/MarketData.Api
```

## Test

```powershell
dotnet test
```

The tests do not use the network: the provider interface is replaced by a fake in the endpoint tests, and the HTTP handler is stubbed in the provider tests.

## Configuration

| Key | Default |
|---|---|
| `Providers:Frankfurter:BaseUrl` | `https://api.frankfurter.dev/v2/` |

As an environment variable, use the double-underscore form: `Providers__Frankfurter__BaseUrl`.

## Structure

- `src/MarketData.Api`: minimal API endpoints and ProblemDetails mapping.
- `src/MarketData.Core`: provider interfaces and response contracts.
- `src/MarketData.Infrastructure`: provider implementations (Frankfurter, ECB, U.S. Treasury, Yahoo Finance, CoinPaprika), the FX caching decorator and DI wiring.
- `tests/MarketData.Tests`: endpoint tests with fake providers and provider tests with stubbed HTTP handlers.

## Design decisions

- **Minimal API with light layering.** The system is a read facade with no business rules yet, so a full Clean Architecture or domain model would be ceremony. If rules appear (alerts, portfolios, regulated rounding), an application layer and a domain model are introduced then.
- **Provider abstraction with caching where it exists today.** Free tiers have small quotas, so some endpoints are served from an in-memory cache and the upstream is only called on a miss: FX rates (1 hour), the crypto catalog (1 hour) and crypto quotes (30 seconds). Swapping a provider only touches the infrastructure project.
- **Cost first.** Free providers, free CI, and a free-tier Azure target. Spending decisions are documented before being made.
- **Protected `main`.** Changes go through pull requests, and the CI check must pass before merging.

## Known limitations

- Euribor, bond, stock, ETF, commodity and stock search requests are not cached yet: each request calls the upstream provider.
- Providers only have timeouts. There are no retries, backoff or circuit breaker.
- The cache is in memory: it is lost on restart and not shared between instances.
- Stocks, ETFs and commodities use an unofficial Yahoo Finance endpoint that can change or throttle without notice.
- Only the Frankfurter base URL is configurable; the other provider URLs are fixed in code.

## Data sources

Rates come from free public providers and may be delayed or change their terms. Do not use this data for trading decisions. Check each provider's terms before exposing the data publicly.
