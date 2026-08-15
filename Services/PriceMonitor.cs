using System.Net;
using FlightDealBotMvp.Infrastructure;
using FlightDealBotMvp.Models;

namespace FlightDealBotMvp.Services;

public sealed class PriceMonitor
{
    private readonly JsonFileAlertStore _store;
    private readonly AmadeusClient _amadeus;
    private readonly TelegramApiClient _telegram;
    private readonly AppOptions _options;

    public PriceMonitor(JsonFileAlertStore store, AmadeusClient amadeus, TelegramApiClient telegram, AppOptions options)
    {
        _store = store;
        _amadeus = amadeus;
        _telegram = telegram;
        _options = options;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        Console.WriteLine($"Price monitor started. Interval: {_options.PriceCheckIntervalMinutes} min.");

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await RunCycleAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Monitor cycle error: {ex}");
            }

            await Task.Delay(TimeSpan.FromMinutes(_options.PriceCheckIntervalMinutes), cancellationToken);
        }
    }

    private async Task RunCycleAsync(CancellationToken cancellationToken)
    {
        var alerts = await _store.GetActiveAsync(cancellationToken);
        if (alerts.Count == 0)
            return;

        var groups = alerts.GroupBy(x => x.SearchKey, StringComparer.Ordinal);

        foreach (var group in groups)
        {
            var sample = group.First();
            FlightDeal? deal = null;

            try
            {
                deal = await _amadeus.SearchLowestOfferAsync(
                    sample.Origin,
                    sample.Destination,
                    sample.DepartureDate,
                    sample.ReturnDate,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Amadeus search failed for {sample.SearchKey}: {ex.Message}");
            }

            foreach (var alert in group)
            {
                var shouldNotify = deal is not null && ShouldNotify(alert, deal.TotalPriceUsd);

                if (shouldNotify && deal is not null)
                {
                    try
                    {
                        await _telegram.SendMessageAsync(alert.ChatId,
                            $"🔥 <b>DEAL ALERT</b>\n\n" +
                            $"✈️ {alert.Origin} → {alert.Destination}\n" +
                            $"📅 {alert.DepartureDate:yyyy-MM-dd} → {alert.ReturnDate:yyyy-MM-dd}\n" +
                            $"💰 <b>${deal.TotalPriceUsd:0.##}</b>\n" +
                            $"🎯 Твой порог: ${alert.MaxPriceUsd:0.##}\n\n" +
                            $"Проверь цену перед покупкой: предложения могут меняться быстро.", cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"Telegram notification failed for alert #{alert.Id}: {WebUtility.HtmlEncode(ex.Message)}");
                        shouldNotify = false;
                    }
                }

                await _store.UpdateCheckResultAsync(alert.Id, deal?.TotalPriceUsd, shouldNotify, cancellationToken);
            }
        }
    }

    private bool ShouldNotify(AlertSubscription alert, decimal price)
    {
        if (price > alert.MaxPriceUsd)
            return false;

        if (!alert.LastNotifiedAtUtc.HasValue || !alert.LastNotifiedPriceUsd.HasValue)
            return true;

        var droppedEnough = alert.LastNotifiedPriceUsd.Value - price >= _options.MinimumPriceDropForRepeatAlert;
        var cooldownExpired = DateTimeOffset.UtcNow - alert.LastNotifiedAtUtc.Value >=
                              TimeSpan.FromMinutes(_options.AlertNotificationCooldownMinutes);

        return droppedEnough || cooldownExpired;
    }
}
