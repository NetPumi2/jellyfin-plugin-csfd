using System.Net.Http;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Csfd.Csfd;

/// <summary>
/// Lazily creates a single shared <see cref="CsfdClient"/> and <see cref="CsfdCache"/> for the
/// whole plugin, so the movie and series providers read/write the same in-memory cache state
/// instead of each keeping (and overwriting) their own copy of the cache file.
/// </summary>
internal static class CsfdServices
{
    private static readonly object InitLock = new();
    private static CsfdCache? _cache;
    private static CsfdClient? _client;

    public static CsfdCache GetCache(ILoggerFactory loggerFactory)
    {
        if (_cache is null)
        {
            lock (InitLock)
            {
                _cache ??= new CsfdCache(Plugin.Instance!.DataFolderPath, loggerFactory.CreateLogger<CsfdCache>());
            }
        }

        return _cache;
    }

    public static CsfdClient GetClient(IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory)
    {
        if (_client is null)
        {
            lock (InitLock)
            {
                _client ??= new CsfdClient(httpClientFactory, loggerFactory.CreateLogger<CsfdClient>());
            }
        }

        return _client;
    }
}
