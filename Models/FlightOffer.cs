namespace FlightDealBotMvp.Models;
public sealed record FlightOffer(string Provider, string ProviderOfferId, FlightPrice Price, string CabinClass, bool RequiresSelfTransfer, IReadOnlyList<FlightLeg> Legs);
