namespace FlightDealBotMvp.Models;
public sealed record BookingOption(IReadOnlyList<string> Legs, IReadOnlyList<BookingLink> Links);
