# market-data-api

A small .NET 9 Web API that exposes market data (currently FX rates) through a cached facade over free providers.

This is an R&D proof of concept, built to explore a Docker-first workflow that is ready to move to Azure while keeping the running cost at zero. It is not intended for production or heavy traffic.

## Status

| Area | State |
|---|---|
| FX rates (Frankfurter, ECB reference rates) | Done |
| Unit and endpoint tests | Done (11 tests) |
| CI (build and test on every PR) | Done |
| Euribor, EUR short-term rate, Fed, stocks, ETFs, commodities | Planned |
| Deployment to Azure with Bicep | Planned |

## Endpoints

| Method | Path | Description |
|---|---|---|
| GET | `/api/fx/{base}/{quote}` | Latest rate for a currency pair |
| GET | `/health` | Liveness check |

Status codes: `200` rate found, `400` currency codes are not 3 letters, `404` unknown currency.

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
| `Providers:Frankfurter:BaseUrl` | `https://api.frankfurter.dev/v1/` |

As an environment variable, use the double-underscore form: `Providers__Frankfurter__BaseUrl`.

## Structure


## Design decisions

- **Minimal API with light layering.** The system is a read facade with no business rules yet, so a full Clean Architecture or domain model would be ceremony. If rules appear (alerts, portfolios, regulated rounding), an application layer and a domain model are introduced then.
- **Provider abstraction plus a caching decorator.** Free tiers have small quotas, so requests are served from an in-memory cache (1 hour TTL) and the upstream is only called on a miss. Swapping a provider only touches the infrastructure project.
- **Cost first.** Free providers, free CI, and a free-tier Azure target. Spending decisions are documented before being made.
- **Protected `main`.** Changes go through pull requests, and the CI check must pass before merging.

## Data sources

Rates come from free public providers and may be delayed or change their terms. Do not use this data for trading decisions. Check each provider's terms before exposing the data publicly.