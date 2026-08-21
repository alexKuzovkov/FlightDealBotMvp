using System.Collections.Concurrent;
using System.Net;
using System.Text;

namespace FlightDealBotMvp.Tests;

internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly ConcurrentQueue<Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>> _responses = new();

    public int RequestCount { get; private set; }
    public Uri? LastRequestUri { get; private set; }
    public string? LastRequestBody { get; private set; }
    public string? LastApiKey { get; private set; }

    public void Enqueue(HttpStatusCode statusCode, string json)
    {
        _responses.Enqueue((_, _) => Task.FromResult(new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        }));
    }
    public void Enqueue(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> response) => _responses.Enqueue(response);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        RequestCount++;
        LastRequestUri = request.RequestUri;
        LastApiKey = request.Headers.TryGetValues("X-Api-Key", out var values) ? values.SingleOrDefault() : null;
        LastRequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);

        if (!_responses.TryDequeue(out var response))
            throw new InvalidOperationException("No stub response configured.");

        return await response(request, cancellationToken);
    }
}