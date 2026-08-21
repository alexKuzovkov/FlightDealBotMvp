using System.Net;
using FlightDealBotMvp.Infrastructure;
using FlightDealBotMvp.Models;

namespace FlightDealBotMvp.Services;

public sealed class PriceMonitor
{
    private readonly JsonFileAlertStore _store;
    private readonly IFlightProvider _provider;
    private readonly TelegramApiClient _telegram;
    private readonly AppOptions _options;

    public PriceMonitor(JsonFileAlertStore store, IFlightProvider provider, TelegramApiClient telegram, AppOptions options)
    {
        _store = store; _provider = provider; _telegram = telegram; _options = options;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        Console.WriteLine($"Price monitor started. Provider: {_provider.Name}. Interval: {_options.PriceCheckIntervalMinutes} min.");
        while (!ct.IsCancellationRequested)
        {
            try { await RunCycleAsync(ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex) { Console.Error.WriteLine($"Monitor cycle error: {ex}"); }
            await Task.Delay(TimeSpan.FromMinutes(_options.PriceCheckIntervalMinutes), ct);
        }
    }
    private async Task RunCycleAsync(CancellationToken ct)
    {
        var alerts = await _store.GetActiveAsync(ct);
        foreach (var group in alerts.GroupBy(x => x.SearchKey, StringComparer.Ordinal))
        {
            var sample = group.First(); FlightOffer? offer = null;
            try
            {
                var request = new FlightSearchRequest([new(sample.Origin, sample.Destination, sample.DepartureDate), new(sample.Destination, sample.Origin, sample.ReturnDate)]);
                offer = (await _provider.SearchAsync(request, ct)).Where(x => x.Price.Currency.Equals("USD", StringComparison.OrdinalIgnoreCase)).OrderBy(x => x.Price.Amount).FirstOrDefault();
            }
            catch (Exception ex) { Console.Error.WriteLine($"{_provider.Name} search failed for {sample.SearchKey}: {ex.Message}"); }

            var candidates = offer is null ? [] : group.Where(x => ShouldNotify(x, offer.Price.Amount)).ToList();
            BookingResolution? booking = null;
            if (offer is not null && candidates.Count > 0)
            {
                try { booking = await _provider.ResolveBookingAsync(offer, ct); offer = booking.RefreshedOffer ?? offer; }
                catch (Exception ex) { Console.Error.WriteLine($"{_provider.Name} booking resolution failed: {ex.Message}"); }
            }

            foreach (var alert in group)
            {
                var notify = offer is not null && ShouldNotify(alert, offer.Price.Amount);
                if (notify && offer is not null)
                {
                    try { await _telegram.SendMessageAsync(alert.ChatId, BotCommandHandler.FormatOffer("🔥 <b>DEAL ALERT</b>", alert, offer), booking is null ? null : BotCommandHandler.BuildButtons(booking), ct); }
                    catch (Exception ex) { Console.Error.WriteLine($"Telegram notification failed for alert #{alert.Id}: {WebUtility.HtmlEncode(ex.Message)}"); notify = false; }
                }
                await _store.UpdateCheckResultAsync(alert.Id, offer?.Price.Amount, notify, ct);
            }
        }
    }
    private bool ShouldNotify(AlertSubscription alert, decimal price)
    {
        if (price > alert.MaxPriceUsd) return false;
        if (!alert.LastNotifiedAtUtc.HasValue || !alert.LastNotifiedPriceUsd.HasValue) return true;
        var droppedEnough = alert.LastNotifiedPriceUsd.Value - price >= _options.MinimumPriceDropForRepeatAlert;
        var cooldownExpired = DateTimeOffset.UtcNow - alert.LastNotifiedAtUtc.Value >= TimeSpan.FromMinutes(_options.AlertNotificationCooldownMinutes);
        return droppedEnough || cooldownExpired;
    }
}
