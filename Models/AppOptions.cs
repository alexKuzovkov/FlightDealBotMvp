namespace FlightDealBotMvp.Models;

public sealed class AppOptions
{
    public string TelegramBotToken { get; init; } = string.Empty;
    public string AmadeusClientId { get; init; } = string.Empty;
    public string AmadeusClientSecret { get; init; } = string.Empty;
    public string AmadeusBaseUrl { get; init; } = "https://test.api.amadeus.com";
    public int PriceCheckIntervalMinutes { get; init; } = 15;
    public int AlertNotificationCooldownMinutes { get; init; } = 720;
    public decimal MinimumPriceDropForRepeatAlert { get; init; } = 20m;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(TelegramBotToken))
            throw new InvalidOperationException("TelegramBotToken is empty.");
        if (string.IsNullOrWhiteSpace(AmadeusClientId))
            throw new InvalidOperationException("AmadeusClientId is empty.");
        if (string.IsNullOrWhiteSpace(AmadeusClientSecret))
            throw new InvalidOperationException("AmadeusClientSecret is empty.");
        if (!Uri.TryCreate(AmadeusBaseUrl, UriKind.Absolute, out _))
            throw new InvalidOperationException("AmadeusBaseUrl must be an absolute URL.");
        if (PriceCheckIntervalMinutes < 1)
            throw new InvalidOperationException("PriceCheckIntervalMinutes must be >= 1.");
    }
}
