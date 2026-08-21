using System.Net.Http.Json;
using System.Text.Json;
using FlightDealBotMvp.Models;

namespace FlightDealBotMvp.Services;

public sealed class TelegramApiClient
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public TelegramApiClient(HttpClient httpClient, string botToken)
    {
        _httpClient = httpClient;
        _baseUrl = $"https://api.telegram.org/bot{botToken}/";
    }

    public async Task DeleteWebhookAsync(CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync(_baseUrl + "deleteWebhook", new { drop_pending_updates = false }, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<TelegramUpdate>> GetUpdatesAsync(long offset, CancellationToken cancellationToken)
    {
        var url = $"{_baseUrl}getUpdates?offset={offset}&timeout=30&allowed_updates=%5B%22message%22%5D";
        using var response = await _httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        var envelope = await response.Content.ReadFromJsonAsync<TelegramResponse<List<TelegramUpdate>>>(_jsonOptions, cancellationToken);
        if (envelope is null || !envelope.Ok) throw new InvalidOperationException($"Telegram getUpdates failed: {envelope?.Description ?? "empty response"}");
        return envelope.Result ?? [];
    }

    public Task SendMessageAsync(long chatId, string text, CancellationToken cancellationToken) => SendMessageAsync(chatId, text, null, cancellationToken);

    public async Task SendMessageAsync(long chatId, string text, IReadOnlyList<TelegramInlineButton>? buttons, CancellationToken cancellationToken)
    {
        object? replyMarkup = buttons is { Count: > 0 }
            ? new { inline_keyboard = buttons.Select(x => new[] { new { text = x.Text, url = x.Url } }).ToArray() }
            : null;
        var payload = new
        {
            chat_id = chatId,
            text,
            parse_mode = "HTML",
            disable_web_page_preview = true,
            reply_markup = replyMarkup
        };
        using var response = await _httpClient.PostAsJsonAsync(_baseUrl + "sendMessage", payload, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode) throw new HttpRequestException($"Telegram sendMessage failed ({(int)response.StatusCode}): {body}");
    }
}
