# FlightDealBotMvp

Первый рабочий vertical slice Telegram-бота для мониторинга цен на авиабилеты.

## Что уже умеет

- запускаться локально на Windows/Linux;
- получать Telegram-сообщения через long polling, без домена и HTTPS;
- создавать алерт на конкретный маршрут и даты;
- хранить алерты в `data/alerts.json`;
- получать Flight Offers из Amadeus Self-Service API;
- группировать одинаковые поиски, чтобы не дергать Amadeus по одному разу на каждого пользователя;
- присылать Telegram-уведомление при цене ниже заданного порога;
- защищаться от спама повторными одинаковыми алертами.

## Требования

- .NET 8 SDK
- Telegram Bot Token от @BotFather
- Amadeus API Key + Secret

## Настройка

1. Скопируй конфиг:

```powershell
Copy-Item appsettings.example.json appsettings.json
```

2. Заполни `appsettings.json`:

```json
{
  "TelegramBotToken": "...",
  "AmadeusClientId": "...",
  "AmadeusClientSecret": "...",
  "AmadeusBaseUrl": "https://test.api.amadeus.com",
  "PriceCheckIntervalMinutes": 15,
  "AlertNotificationCooldownMinutes": 720,
  "MinimumPriceDropForRepeatAlert": 20
}
```

3. Запусти:

```powershell
dotnet restore
dotnet run
```

4. Открой своего бота и напиши:

```text
/start
```

5. Создай тестовый алерт:

```text
/alert JFK CDG 2026-10-17 2026-10-24 500
```

6. Проверь сразу:

```text
/check 1
```

## Команды

```text
/start
/help
/alert FROM TO DEPART RETURN MAX_USD
/list
/check ID
/delete ID
```

## Почему пока JSON, а не PostgreSQL

На первом этапе нам нужно проверить самую рискованную часть продукта: реально ли стабильно получать полезные цены и нравятся ли пользователям алерты. После этого JSON заменяется репозиторием PostgreSQL без изменения Telegram/Amadeus слоя.

## Следующий этап

1. Города вместо IATA: `New York` => JFK/EWR/LGA.
2. `Anywhere in Europe` с набором европейских аэропортов.
3. Flexible dates + trip duration.
4. История цен и DealScore.
5. PostgreSQL + Redis.
6. Telegram Stars после проверки продуктовой гипотезы.
