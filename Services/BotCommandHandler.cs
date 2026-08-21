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
    private readonly IFlightProvider _provider;

    public BotCommandHandler(TelegramApiClient telegram, JsonFileAlertStore store, IFlightProvider provider)
    {
        _telegram = telegram;
        _store = store;
        _provider = provider;
    }

    public async Task HandleAsync(TelegramMessage message, CancellationToken ct)
    {
        var text = message.Text?.Trim();
        if (string.IsNullOrWhiteSpace(text)) return;
        var userId = message.From?.Id ?? message.Chat.Id;
        var command = text.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries)[0].Split('@')[0].ToLowerInvariant();
        switch (command)
        {
            case "/start": case "/help": await SendHelpAsync(message.Chat.Id, ct); break;
            case "/alert": await AddAlertAsync(message.Chat.Id, userId, text, ct); break;
            case "/list": await ListAlertsAsync(message.Chat.Id, userId, ct); break;
            case "/delete": await DeleteAlertAsync(message.Chat.Id, userId, text, ct); break;
            case "/check": await CheckNowAsync(message.Chat.Id, userId, text, ct); break;
            default: await _telegram.SendMessageAsync(message.Chat.Id, "Не понял команду. Используй /help.", ct); break;
        }
    }

    private Task SendHelpAsync(long chatId, CancellationToken ct) => _telegram.SendMessageAsync(chatId,
        "<b>✈️ Flight Deal Bot</b>\n\nСоздать: <code>/alert WAW BCN 2026-09-20 2026-09-27 250</code>\n" +
        "/list — алерты\n/check ID — проверить сейчас\n/delete ID — удалить", ct);

    private async Task AddAlertAsync(long chatId, long userId, string text, CancellationToken ct)
    {
        var p = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (p.Length != 6) { await _telegram.SendMessageAsync(chatId, "Формат: <code>/alert WAW BCN 2026-09-20 2026-09-27 250</code>", ct); return; }
        var origin = NormalizeIata(p[1]); var destination = NormalizeIata(p[2]);
        if (origin is null || destination is null || origin == destination) { await _telegram.SendMessageAsync(chatId, "Нужны разные IATA-коды из 3 латинских букв.", ct); return; }
        if (!DateOnly.TryParseExact(p[3], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var departure) || !DateOnly.TryParseExact(p[4], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var returnDate)) { await _telegram.SendMessageAsync(chatId, "Дата: YYYY-MM-DD.", ct); return; }
        if (departure < DateOnly.FromDateTime(DateTime.UtcNow) || returnDate <= departure) { await _telegram.SendMessageAsync(chatId, "Проверь даты поездки.", ct); return; }
        if (!decimal.TryParse(p[5], NumberStyles.Number, CultureInfo.InvariantCulture, out var max) || max <= 0) { await _telegram.SendMessageAsync(chatId, "MAX_USD должен быть положительным.", ct); return; }
        var alert = await _store.AddAsync(new AlertSubscription { ChatId = chatId, TelegramUserId = userId, Origin = origin, Destination = destination, DepartureDate = departure, ReturnDate = returnDate, MaxPriceUsd = max }, ct);
        await _telegram.SendMessageAsync(chatId, $"✅ Алерт <b>#{alert.Id}</b>: {origin} → {destination}, {departure:yyyy-MM-dd} → {returnDate:yyyy-MM-dd}, ≤ ${max:0.##}", ct);
    }
    private async Task ListAlertsAsync(long chatId, long userId, CancellationToken ct)
    {
        var alerts = await _store.GetForUserAsync(userId, ct);
        if (alerts.Count == 0) { await _telegram.SendMessageAsync(chatId, "Активных алертов нет.", ct); return; }
        var sb = new StringBuilder("<b>Твои алерты</b>\n\n");
        foreach (var a in alerts) sb.AppendLine($"<b>#{a.Id}</b> {a.Origin} → {a.Destination}\n{a.DepartureDate:yyyy-MM-dd} → {a.ReturnDate:yyyy-MM-dd} | ≤ ${a.MaxPriceUsd:0.##}\n");
        await _telegram.SendMessageAsync(chatId, sb.ToString(), ct);
    }

    private async Task DeleteAlertAsync(long chatId, long userId, string text, CancellationToken ct)
    {
        var p = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (p.Length != 2 || !int.TryParse(p[1], out var id)) { await _telegram.SendMessageAsync(chatId, "Формат: <code>/delete ID</code>", ct); return; }
        var deleted = await _store.DeleteAsync(id, userId, ct);
        await _telegram.SendMessageAsync(chatId, deleted ? $"🗑 Алерт #{id} удалён." : $"Алерт #{id} не найден.", ct);
    }

    private async Task CheckNowAsync(long chatId, long userId, string text, CancellationToken ct)
    {
        var p = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (p.Length != 2 || !int.TryParse(p[1], out var id)) { await _telegram.SendMessageAsync(chatId, "Формат: <code>/check ID</code>", ct); return; }
        var alert = (await _store.GetForUserAsync(userId, ct)).FirstOrDefault(x => x.Id == id);
        if (alert is null) { await _telegram.SendMessageAsync(chatId, $"Алерт #{id} не найден.", ct); return; }
        try
        {
            var offer = await FindCheapestAsync(alert, ct);
            if (offer is null) { await _store.UpdateCheckResultAsync(id, null, false, ct); await _telegram.SendMessageAsync(chatId, $"{_provider.Name} не вернул предложений в USD.", ct); return; }
            var booking = await _provider.ResolveBookingAsync(offer, ct); offer = booking.RefreshedOffer ?? offer;
            await _store.UpdateCheckResultAsync(id, offer.Price.Amount, false, ct);
            await _telegram.SendMessageAsync(chatId, FormatOffer(offer.Price.Amount <= alert.MaxPriceUsd ? "🔥 DEAL" : "ℹ️ Пока выше порога", alert, offer), BuildButtons(booking), ct);
        }
        catch (Exception ex) { await _telegram.SendMessageAsync(chatId, $"Ошибка {_provider.Name}: <code>{WebUtility.HtmlEncode(Shorten(ex.Message, 600))}</code>", ct); }
    }

    private async Task<FlightOffer?> FindCheapestAsync(AlertSubscription a, CancellationToken ct)
    {
        var request = new FlightSearchRequest([new(a.Origin, a.Destination, a.DepartureDate), new(a.Destination, a.Origin, a.ReturnDate)]);
        return (await _provider.SearchAsync(request, ct)).Where(x => x.Price.Currency.Equals("USD", StringComparison.OrdinalIgnoreCase)).OrderBy(x => x.Price.Amount).FirstOrDefault();
    }

    internal static IReadOnlyList<TelegramInlineButton> BuildButtons(BookingResolution booking)
    {
        var links = booking.Options.SelectMany(x => x.Links).Where(x => Uri.TryCreate(x.Url, UriKind.Absolute, out _)).ToList();
        var airline = links.Where(x => x.ProviderType.Equals("airline", StringComparison.OrdinalIgnoreCase)).OrderBy(x => x.Price.Amount).FirstOrDefault();
        var ota = links.Where(x => !x.ProviderType.Equals("airline", StringComparison.OrdinalIgnoreCase)).OrderBy(x => x.Price.Amount).FirstOrDefault();
        return new[] { airline, ota }.Where(x => x is not null).Select(x => new TelegramInlineButton($"Купить: {x!.ProviderName} — {x.Price.Amount:0.##} {x.Price.Currency}", x.Url)).ToList();
    }

    internal static string FormatOffer(string marker, AlertSubscription alert, FlightOffer offer)
    {
        var flights = string.Join(", ", offer.Legs.SelectMany(x => x.Segments).Select(x => $"{x.MarketingCarrierCode}{x.FlightNumber}"));
        var carrier = string.Join(" / ", offer.Legs.Select(x => x.Carrier).Distinct());
        var transfer = offer.RequiresSelfTransfer ? "\n⚠️ Требуется самостоятельная пересадка" : string.Empty;
        return $"{marker}\n\n✈️ {alert.Origin} → {alert.Destination}\n📅 {alert.DepartureDate:yyyy-MM-dd} → {alert.ReturnDate:yyyy-MM-dd}\n💰 <b>{offer.Price.Amount:0.##} {offer.Price.Currency}</b>\n🛫 {carrier} {flights}\n🎯 Твой порог: ${alert.MaxPriceUsd:0.##}{transfer}";
    }

    private static string? NormalizeIata(string value) { var v = value.Trim().ToUpperInvariant(); return v.Length == 3 && v.All(c => c is >= 'A' and <= 'Z') ? v : null; }
    private static string Shorten(string value, int max) => value.Length <= max ? value : value[..max] + "…";
}
