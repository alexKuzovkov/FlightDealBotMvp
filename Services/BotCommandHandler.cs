using System.Globalization;
using System.Net;
using System.Text;
using FlightDealBotMvp.Infrastructure;
using FlightDealBotMvp.Models;

namespace FlightDealBotMvp.Services;

public sealed class BotCommandHandler
{
    private readonly TelegramApiClient _telegram;
    private readonly JsonFileAlertStore _store;
    private readonly AmadeusClient _amadeus;

    public BotCommandHandler(TelegramApiClient telegram, JsonFileAlertStore store, AmadeusClient amadeus)
    {
        _telegram = telegram;
        _store = store;
        _amadeus = amadeus;
    }

    public async Task HandleAsync(TelegramMessage message, CancellationToken cancellationToken)
    {
        var text = message.Text?.Trim();
        if (string.IsNullOrWhiteSpace(text))
            return;

        var userId = message.From?.Id ?? message.Chat.Id;
        var chatId = message.Chat.Id;
        var command = text.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries)[0].Split('@')[0].ToLowerInvariant();

        switch (command)
        {
            case "/start":
            case "/help":
                await SendHelpAsync(chatId, cancellationToken);
                break;
            case "/alert":
                await AddAlertAsync(chatId, userId, text, cancellationToken);
                break;
            case "/list":
                await ListAlertsAsync(chatId, userId, cancellationToken);
                break;
            case "/delete":
                await DeleteAlertAsync(chatId, userId, text, cancellationToken);
                break;
            case "/check":
                await CheckNowAsync(chatId, userId, text, cancellationToken);
                break;
            default:
                await _telegram.SendMessageAsync(chatId, "Не понял команду. Используй /help.", cancellationToken);
                break;
        }
    }

    private Task SendHelpAsync(long chatId, CancellationToken cancellationToken)
    {
        const string help = """
<b>✈️ Flight Deal Bot — MVP</b>

Создать алерт:
<code>/alert JFK CDG 2026-10-17 2026-10-24 500</code>

Формат:
<code>/alert FROM TO DEPART RETURN MAX_USD</code>

Команды:
/list — мои алерты
/check ID — проверить алерт сейчас
/delete ID — удалить алерт
/help — помощь

Пример:
<code>/alert JFK LIS 2026-10-17 2026-10-24 450</code>
""";
        return _telegram.SendMessageAsync(chatId, help, cancellationToken);
    }

    private async Task AddAlertAsync(long chatId, long userId, string text, CancellationToken cancellationToken)
    {
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 6)
        {
            await _telegram.SendMessageAsync(chatId,
                "Неверный формат.\n<code>/alert JFK CDG 2026-10-17 2026-10-24 500</code>", cancellationToken);
            return;
        }

        var origin = NormalizeIata(parts[1]);
        var destination = NormalizeIata(parts[2]);
        if (origin is null || destination is null)
        {
            await _telegram.SendMessageAsync(chatId, "Коды аэропортов должны состоять из 3 латинских букв, например JFK и CDG.", cancellationToken);
            return;
        }

        if (!DateOnly.TryParseExact(parts[3], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var departure) ||
            !DateOnly.TryParseExact(parts[4], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var returnDate))
        {
            await _telegram.SendMessageAsync(chatId, "Дата должна быть в формате YYYY-MM-DD.", cancellationToken);
            return;
        }

        if (departure < DateOnly.FromDateTime(DateTime.UtcNow) || returnDate <= departure)
        {
            await _telegram.SendMessageAsync(chatId, "Дата вылета должна быть не в прошлом, а дата возврата — позже даты вылета.", cancellationToken);
            return;
        }

        if (!decimal.TryParse(parts[5], NumberStyles.Number, CultureInfo.InvariantCulture, out var maxPrice) || maxPrice <= 0)
        {
            await _telegram.SendMessageAsync(chatId, "MAX_USD должен быть положительным числом, например 500.", cancellationToken);
            return;
        }

        var alert = await _store.AddAsync(new AlertSubscription
        {
            ChatId = chatId,
            TelegramUserId = userId,
            Origin = origin,
            Destination = destination,
            DepartureDate = departure,
            ReturnDate = returnDate,
            MaxPriceUsd = maxPrice
        }, cancellationToken);

        await _telegram.SendMessageAsync(chatId,
            $"✅ Алерт <b>#{alert.Id}</b> создан.\n" +
            $"✈️ {origin} → {destination}\n" +
            $"📅 {departure:yyyy-MM-dd} → {returnDate:yyyy-MM-dd}\n" +
            $"💰 Порог: <b>${maxPrice:0.##}</b>\n\n" +
            $"Проверить сейчас: <code>/check {alert.Id}</code>", cancellationToken);
    }

    private async Task ListAlertsAsync(long chatId, long userId, CancellationToken cancellationToken)
    {
        var alerts = await _store.GetForUserAsync(userId, cancellationToken);
        if (alerts.Count == 0)
        {
            await _telegram.SendMessageAsync(chatId, "Активных алертов пока нет. Создай через /alert.", cancellationToken);
            return;
        }

        var sb = new StringBuilder("<b>Твои алерты</b>\n\n");
        foreach (var a in alerts)
        {
            var lastPrice = a.LastPriceUsd.HasValue ? $"${a.LastPriceUsd:0.##}" : "—";
            sb.AppendLine($"<b>#{a.Id}</b> {a.Origin} → {a.Destination}");
            sb.AppendLine($"{a.DepartureDate:yyyy-MM-dd} → {a.ReturnDate:yyyy-MM-dd} | ≤ ${a.MaxPriceUsd:0.##}");
            sb.AppendLine($"Последняя цена: {lastPrice}");
            sb.AppendLine();
        }

        await _telegram.SendMessageAsync(chatId, sb.ToString(), cancellationToken);
    }

    private async Task DeleteAlertAsync(long chatId, long userId, string text, CancellationToken cancellationToken)
    {
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 || !int.TryParse(parts[1], out var id))
        {
            await _telegram.SendMessageAsync(chatId, "Формат: <code>/delete ID</code>", cancellationToken);
            return;
        }

        var deleted = await _store.DeleteAsync(id, userId, cancellationToken);
        await _telegram.SendMessageAsync(chatId,
            deleted ? $"🗑 Алерт #{id} удалён." : $"Алерт #{id} не найден.", cancellationToken);
    }

    private async Task CheckNowAsync(long chatId, long userId, string text, CancellationToken cancellationToken)
    {
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 || !int.TryParse(parts[1], out var id))
        {
            await _telegram.SendMessageAsync(chatId, "Формат: <code>/check ID</code>", cancellationToken);
            return;
        }

        var alert = (await _store.GetForUserAsync(userId, cancellationToken)).FirstOrDefault(x => x.Id == id);
        if (alert is null)
        {
            await _telegram.SendMessageAsync(chatId, $"Алерт #{id} не найден.", cancellationToken);
            return;
        }

        await _telegram.SendMessageAsync(chatId, $"🔎 Проверяю {alert.Origin} → {alert.Destination}...", cancellationToken);

        try
        {
            var deal = await _amadeus.SearchLowestOfferAsync(
                alert.Origin, alert.Destination, alert.DepartureDate, alert.ReturnDate, cancellationToken);

            await _store.UpdateCheckResultAsync(alert.Id, deal?.TotalPriceUsd, notified: false, cancellationToken);

            if (deal is null)
            {
                await _telegram.SendMessageAsync(chatId, "По этому запросу Amadeus не вернул предложений.", cancellationToken);
                return;
            }

            var marker = deal.TotalPriceUsd <= alert.MaxPriceUsd ? "🔥 DEAL" : "ℹ️ Пока выше порога";
            await _telegram.SendMessageAsync(chatId,
                $"{marker}\n\n✈️ {alert.Origin} → {alert.Destination}\n" +
                $"📅 {alert.DepartureDate:yyyy-MM-dd} → {alert.ReturnDate:yyyy-MM-dd}\n" +
                $"💰 Найдено: <b>${deal.TotalPriceUsd:0.##}</b>\n" +
                $"🎯 Твой порог: ${alert.MaxPriceUsd:0.##}", cancellationToken);
        }
        catch (Exception ex)
        {
            await _telegram.SendMessageAsync(chatId,
                $"Ошибка проверки: <code>{WebUtility.HtmlEncode(Shorten(ex.Message, 600))}</code>", cancellationToken);
        }
    }

    private static string? NormalizeIata(string value)
    {
        var normalized = value.Trim().ToUpperInvariant();
        return normalized.Length == 3 && normalized.All(c => c is >= 'A' and <= 'Z') ? normalized : null;
    }

    private static string Shorten(string value, int max) => value.Length <= max ? value : value[..max] + "…";
}
