namespace FlightDealBotMvp.Models;

public sealed class AlertSubscription
{
    public int Id { get; set; }
    public long ChatId { get; set; }
    public long TelegramUserId { get; set; }
    public string Origin { get; set; } = string.Empty;
    public string Destination { get; set; } = string.Empty;
    public DateOnly DepartureDate { get; set; }
    public DateOnly ReturnDate { get; set; }
    public decimal MaxPriceUsd { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastCheckedAtUtc { get; set; }
    public decimal? LastPriceUsd { get; set; }
    public DateTimeOffset? LastNotifiedAtUtc { get; set; }
    public decimal? LastNotifiedPriceUsd { get; set; }

    public string SearchKey => $"{Origin}|{Destination}|{DepartureDate:yyyy-MM-dd}|{ReturnDate:yyyy-MM-dd}";
}
