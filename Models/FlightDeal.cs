namespace FlightDealBotMvp.Models;

public sealed record FlightDeal(
    string Origin,
    string Destination,
    DateOnly DepartureDate,
    DateOnly ReturnDate,
    decimal TotalPriceUsd,
    string? OfferId);
