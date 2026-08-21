namespace FlightDealBotMvp.Models;

public sealed record FlightSearchRequest(
    IReadOnlyList<FlightSearchLeg> Legs,
    int Adults = 1,
    string CabinClass = "economy",
    string Market = "US");
