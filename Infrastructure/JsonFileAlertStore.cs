using System.Text.Json;
using System.Text.Json.Serialization;
using FlightDealBotMvp.Models;

namespace FlightDealBotMvp.Infrastructure;

public sealed class JsonFileAlertStore
{
    private readonly string _filePath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };
    private List<AlertSubscription> _alerts = [];

    public JsonFileAlertStore(string filePath)
    {
        _filePath = filePath;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            if (!File.Exists(_filePath))
            {
                _alerts = [];
                await SaveUnsafeAsync(cancellationToken);
                return;
            }

            await using var stream = File.OpenRead(_filePath);
            _alerts = await JsonSerializer.DeserializeAsync<List<AlertSubscription>>(stream, _jsonOptions, cancellationToken)
                      ?? [];
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<AlertSubscription> AddAsync(AlertSubscription alert, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            alert.Id = _alerts.Count == 0 ? 1 : _alerts.Max(x => x.Id) + 1;
            _alerts.Add(alert);
            await SaveUnsafeAsync(cancellationToken);
            return Clone(alert);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<AlertSubscription>> GetForUserAsync(long telegramUserId, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return _alerts
                .Where(x => x.TelegramUserId == telegramUserId && x.IsActive)
                .OrderBy(x => x.Id)
                .Select(Clone)
                .ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<AlertSubscription>> GetActiveAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return _alerts.Where(x => x.IsActive).Select(Clone).ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> DeleteAsync(int id, long telegramUserId, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var alert = _alerts.FirstOrDefault(x => x.Id == id && x.TelegramUserId == telegramUserId && x.IsActive);
            if (alert is null)
                return false;

            alert.IsActive = false;
            await SaveUnsafeAsync(cancellationToken);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task UpdateCheckResultAsync(
        int id,
        decimal? latestPrice,
        bool notified,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var alert = _alerts.FirstOrDefault(x => x.Id == id);
            if (alert is null)
                return;

            alert.LastCheckedAtUtc = DateTimeOffset.UtcNow;
            alert.LastPriceUsd = latestPrice;

            if (notified && latestPrice.HasValue)
            {
                alert.LastNotifiedAtUtc = DateTimeOffset.UtcNow;
                alert.LastNotifiedPriceUsd = latestPrice.Value;
            }

            await SaveUnsafeAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task SaveUnsafeAsync(CancellationToken cancellationToken)
    {
        var tempPath = _filePath + ".tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, _alerts, _jsonOptions, cancellationToken);
        }

        File.Move(tempPath, _filePath, overwrite: true);
    }

    private static AlertSubscription Clone(AlertSubscription x) => new()
    {
        Id = x.Id,
        ChatId = x.ChatId,
        TelegramUserId = x.TelegramUserId,
        Origin = x.Origin,
        Destination = x.Destination,
        DepartureDate = x.DepartureDate,
        ReturnDate = x.ReturnDate,
        MaxPriceUsd = x.MaxPriceUsd,
        IsActive = x.IsActive,
        CreatedAtUtc = x.CreatedAtUtc,
        LastCheckedAtUtc = x.LastCheckedAtUtc,
        LastPriceUsd = x.LastPriceUsd,
        LastNotifiedAtUtc = x.LastNotifiedAtUtc,
        LastNotifiedPriceUsd = x.LastNotifiedPriceUsd
    };
}
