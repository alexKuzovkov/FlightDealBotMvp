using FlightDealBotMvp.Models;
namespace FlightDealBotMvp.Services;
public interface IFlightProvider
{
    string Name { get; }
    Task<IReadOnlyList<FlightOffer>> SearchAsync(FlightSearchRequest request, CancellationToken cancellationToken);
    Task<BookingResolution> ResolveBookingAsync(FlightOffer offer, CancellationToken cancellationToken);
}
