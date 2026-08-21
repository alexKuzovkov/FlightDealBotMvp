namespace FlightDealBotMvp.Models;
public sealed record BookingResolution(FlightOffer? RefreshedOffer, IReadOnlyList<BookingOption> Options);
