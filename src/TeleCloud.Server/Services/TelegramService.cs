using Microsoft.Extensions.Options;
using TL;

namespace TeleCloud.Server.Services;

public sealed class TelegramService
{
    private StorageChannel? _channel;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly Client _client;
    private readonly TeleCloudOptions _options;
    private readonly ILogger _logger;

    public Client Client => _client;

    public TelegramService(Client client, IOptions<TeleCloudOptions> options, ILogger<TelegramService> logger)
    {
        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    public async ValueTask<StorageChannel> GetChannelAsync(CancellationToken ct = default)
    {
        if (_channel is not null)
            return _channel;

        await _lock.WaitAsync(ct);
        try
        {
            if (_channel is not null)
                return _channel;

            Helpers.Log = (lvl, str) => _logger.Log((LogLevel)lvl, str);

            var myself = await _client.LoginUserIfNeeded();
            _logger.LogInformation("Logged in as {User} (id {Id})", myself, myself.id);

            _channel = await TelegramInitializer.ResolveChannelAsync(_client, _options, _logger, ct);
        }
        finally
        {
            _lock.Release();
        }

        return _channel;
    }
}
