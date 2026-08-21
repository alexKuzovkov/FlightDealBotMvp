# FlightDealBotMvp

Telegram MVP for monitoring airfare and sending actionable deal alerts.

## Current vertical slice

- Telegram `/alert`, `/list`, `/check`, `/delete` commands.
- Ignav is isolated behind `IFlightProvider`.
- One-way, round-trip and two-leg/open-jaw provider requests.
- Search results are normalized into provider-independent domain models.
- Booking links are resolved only when needed.
- Telegram can show direct airline and OTA purchase buttons.
- JSON alert persistence remains the MVP storage.
- Retry/backoff handles HTTP 408, 429, 5xx and transient transport failures.

## Configuration

```powershell
Copy-Item appsettings.example.json appsettings.json
```

Fill `TelegramBotToken` and `IgnavApiKey`. The Ignav key is server-side only and is sent in the `X-Api-Key` header. `appsettings.json` is ignored by Git.

## Run

```powershell
dotnet restore
dotnet build FlightDealBotMvp.sln -c Release
dotnet test FlightDealBotMvp.sln -c Release --no-build
dotnet run
```

## Telegram example

```text
/alert WAW BCN 2026-09-20 2026-09-27 250
/check 1
```

The `/check` flow is: alert -> Ignav search -> cheapest USD offer -> booking refresh -> Telegram result -> direct airline/OTA buttons when available. Booking prices can change; the seller page remains the final price source before purchase.

## Architecture

`BotCommandHandler` and `PriceMonitor` depend on `IFlightProvider`, not on Ignav DTOs. This keeps a second or third provider possible without coupling Telegram/business logic to Ignav's response format.

The monitor keeps grouping identical searches, so one provider search serves all matching alerts. Booking resolution happens only when an offer can trigger a notification.

## Roadmap

- richer airport/city discovery;
- flexible-date deal discovery;
- PostgreSQL persistence;
- Redis caching/rate limiting;
- price history and DealScore;
- additional flight providers and fallback routing.
