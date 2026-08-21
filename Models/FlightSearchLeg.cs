namespace FlightDealBotMvp.Models;
public sealed record FlightSearchLeg(string Origin, string Destination, DateOnly DepartureDate, int? MaxStops = null);
