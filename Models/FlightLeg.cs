namespace FlightDealBotMvp.Models;
public sealed record FlightLeg(string Carrier, int DurationMinutes, IReadOnlyList<FlightSegment> Segments);
