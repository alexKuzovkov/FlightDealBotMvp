namespace FlightDealBotMvp.Models;

public sealed class AppOptions
{
    public string TelegramBotToken { get; init; } = string.Empty;
    public string IgnavApiKey { get; init; } = string.Empty;
    public string IgnavBaseUrl { get; init; } = "https://ignav.com/api";
    public string IgnavMarket { get; init; } = "US";
    public int IgnavRequestTimeoutSeconds { get; init; } = 30;
    public int IgnavMaxRetries { get; init; } = 3;
    public int PriceCheckIntervalMinutes { get; init; } = 15;
    public int AlertNotificationCooldownMinutes { get; init; } = 720;
    public decimal MinimumPriceDropForRepeatAlert { get; init; } = 20m;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(TelegramBotToken)) throw new InvalidOperationException("TelegramBotToken is empty.");
        if (string.IsNullOrWhiteSpace(IgnavApiKey)) throw new InvalidOperationException("IgnavApiKey is empty.");
        if (!Uri.TryCreate(IgnavBaseUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) throw new InvalidOperationException("IgnavBaseUrl must be an absolute HTTPS URL.");
        if (IgnavMarket.Length != 2 || IgnavMarket.Any(c => c is < 'A' or > 'Z')) throw new InvalidOperationException("IgnavMarket must contain exactly 2 uppercase letters.");
        if (IgnavRequestTimeoutSeconds < 1) throw new InvalidOperationException("IgnavRequestTimeoutSeconds must be > 0.");
        if (IgnavMaxRetries is < 0 or > 5) throw new InvalidOperationException("IgnavMaxRetries must be between 0 and 5.");
        if (PriceCheckIntervalMinutes < 1) throw new InvalidOperationException("PriceCheckIntervalMinutes must be at least 1.");
    }
}
