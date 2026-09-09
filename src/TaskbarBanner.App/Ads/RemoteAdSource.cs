using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TaskbarBanner.App.Ads;

public sealed class RemoteAdSource : IAdSource, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly IAdsApiClient _client;
    private readonly string _cachePath;
    private readonly AdContent _fallback;
    private readonly object _gate = new();
    private readonly CancellationTokenSource _cts = new();
    private AdContent _current;
    private TimeSpan _rotation = TimeSpan.FromSeconds(30);
    private Task? _pump;

    public RemoteAdSource(IAdsApiClient client, string cachePath, AdContent fallback)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _cachePath = cachePath;
        _fallback = fallback;
        _current = TryLoadCache() ?? fallback;
    }

    public AdContent GetCurrent()
    {
        lock (_gate)
        {
            return _current;
        }
    }

    public void Start()
    {
        if (_pump is not null)
        {
            return;
        }

        _pump = Task.Run(() => PumpAsync(_cts.Token));
    }

    public Task RefreshAsync(CancellationToken cancellationToken)
        => RefreshCoreAsync(cancellationToken);

    public void Dispose()
    {
        _cts.Cancel();
        try
        {
            _pump?.Wait(TimeSpan.FromSeconds(3));
        }
        catch
        {
            // best effort
        }

        _cts.Dispose();
    }

    private async Task PumpAsync(CancellationToken cancellationToken)
    {
        await RefreshCoreAsync(cancellationToken);
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(RotationOrDefault(), cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            await RefreshCoreAsync(cancellationToken);
        }
    }

    private async Task RefreshCoreAsync(CancellationToken cancellationToken)
    {
        AdFetchResult? result;
        try
        {
            result = await _client.FetchCurrentAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch
        {
            return;
        }

        if (result is null)
        {
            return;
        }

        lock (_gate)
        {
            _current = result.Ad;
            _rotation = result.Rotation;
        }

        TrySaveCache(result.Ad);
    }

    private TimeSpan RotationOrDefault()
    {
        lock (_gate)
        {
            return _rotation;
        }
    }

    private AdContent? TryLoadCache()
    {
        try
        {
            if (!File.Exists(_cachePath))
            {
                return null;
            }

            string json = File.ReadAllText(_cachePath);
            return JsonSerializer.Deserialize<AdContent>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private void TrySaveCache(AdContent ad)
    {
        try
        {
            string? directory = Path.GetDirectoryName(_cachePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(_cachePath, JsonSerializer.Serialize(ad, JsonOptions));
        }
        catch
        {
            // cache is best-effort
        }
    }
}
