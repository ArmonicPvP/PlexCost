# PlexCost

PlexCost is a .NET console application that automates the retrieval of Plex watch history from a Tautulli server, fetches pricing information from Plex’s Discover API, and computes potential savings by comparing purchase/rental costs against subscription fees. It outputs raw data in `data.json` and per-user monthly savings (including totals) in `savings.json`.

## Features

* **Scheduled execution** with configurable interval (default: every 6 hours)
* **Tautulli history retrieval** (filters watch events ≥80% completion)
* **Pricing summary** with retry/backoff for HTTP 429 responses
* **Deduplication** of processed history records
* **Greedy set-cover algorithm** to minimize subscription cost per month
* **JSON output**:

  * `data.json`: raw per-user history with pricing details
  * `savings.json`: per-user monthly savings and grand totals
* **Structured JSON logging** via Serilog (console + rolling files)

## Prerequisites

* [.NET 6.0 SDK](https://dotnet.microsoft.com/download) or later
* Running Tautulli instance with history API enabled
* Plex Discover API access (valid Plex token)

## Configuration

Configure the application via environment variables. Defaults are applied when variables are omitted (except required ones).

| Variable                  | Description                                   | Default        | Required |
| ------------------------- | --------------------------------------------- | -------------- | -------- |
| `HOURS_BETWEEN_RUNS`      | Interval between runs (hours)                 | `6`            | No       |
| `BASE_SUBSCRIPTION_PRICE` | Price per subscription (for cost computation) | `13.99`        | No       |
| `DATA_JSON_PATH`          | Path to raw data JSON input/output            | `data.json`    | No       |
| `SAVINGS_JSON_PATH`       | Path to computed savings JSON output          | `savings.json` | No       |
| `IP_ADDRESS`              | Tautulli server IP address                    | `127.0.0.1`    | No       |
| `PORT`                    | Tautulli server port                          | `80`           | No       |
| `API_KEY`                 | Tautulli API key                              | *none*         | Yes      |
| `PLEX_TOKEN`              | Plex Discover API token                       | *none*         | Yes      |

## Architecture Overview

* **PlexCost.Configuration.PlexCostConfig**: Reads and validates environment variables.
* **PlexCost.Services.LoggerService**: Sets up Serilog for JSON-formatted logging.
* **GetHistory**: Fetches Tautulli history, filters by watch completion, maps to lightweight model.
* **GetPricing**: Calls Plex Discover endpoint to obtain max/avg prices and subscription platforms, with retry/backoff.
* **RecordService**: Tracks processed history in `data.json`, appends new records, avoids duplicates.
* **SaveService**: Aggregates records per user/month, runs a greedy set-cover to minimize subscription count, computes savings and totals, writes `savings.json`.
* **Program**: Entry point—loads config, enters main loop: fetch history → append new records → compute savings → wait.

## Build & Run

### Build

```bash
dotnet build
```

### Run (development)

```bash
dotnet run --project PlexCost
```

### Publish (self-contained example)

```bash
dotnet publish -c Release -r win-x64 --self-contained true
./bin/Release/net6.0/win-x64/PlexCost.exe
```

## Output Files

* **data.json**: Dictionary of `{ userId: UserDataJson }` containing raw history records with pricing details.
* **savings.json**: Dictionary of `{ userId: UserSavingsJson }` containing per-month savings (`Year`, `Month`, `MaximumSavings`, `AverageSavings`, `SubscriptionCosts`, `Subscriptions`) and totals (`TotalMaximumSavings`, `TotalAverageSavings`, `TotalSubscriptionCosts`).

## Logging

* **Format**: JSON (renderMessage: true)
* **Targets**:

  * **Console** (stdout)
  * **Files**: `logs/plexcost-<date>.json` (daily rolling, retain 7 days)

Log levels: Debug, Information, Warning, Error, Critical.

## Contributing

Contributions, issues, and feature requests are welcome. Please open an issue or submit a pull request.
