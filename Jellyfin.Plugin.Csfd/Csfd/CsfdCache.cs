using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Csfd.Csfd;

/// <summary>
/// A simple JSON-file-backed cache mapping "name+year" to a previously resolved ČSFD lookup
/// result, so that a library refresh does not re-scrape ČSFD for every item every time.
/// </summary>
public class CsfdCache
{
    private readonly string _filePath;
    private readonly ILogger<CsfdCache> _logger;
    private readonly object _lock = new();
    private readonly Dictionary<string, CsfdCacheEntry> _entries;

    /// <summary>
    /// Initializes a new instance of the <see cref="CsfdCache"/> class.
    /// </summary>
    /// <param name="dataFolderPath">The plugin's data folder, where the cache file is stored.</param>
    /// <param name="logger">Instance of the <see cref="ILogger{CsfdCache}"/> interface.</param>
    public CsfdCache(string dataFolderPath, ILogger<CsfdCache> logger)
    {
        _logger = logger;
        Directory.CreateDirectory(dataFolderPath);
        _filePath = Path.Combine(dataFolderPath, "csfd-cache.json");
        _entries = Load(_filePath, _logger);
    }

    /// <summary>
    /// Attempts to get a still-valid cached entry for the given title/year.
    /// </summary>
    /// <param name="name">The title.</param>
    /// <param name="year">The production year, if known.</param>
    /// <param name="ttl">How long a cached entry stays valid.</param>
    /// <returns>The cached entry, or <see langword="null"/> if missing or expired.</returns>
    public CsfdCacheEntry? TryGet(string name, int? year, TimeSpan ttl)
    {
        lock (_lock)
        {
            if (_entries.TryGetValue(BuildKey(name, year), out var entry)
                && DateTime.UtcNow - entry.FetchedAtUtc < ttl)
            {
                return entry;
            }

            return null;
        }
    }

    /// <summary>
    /// Stores/overwrites a cache entry for the given title/year and persists it to disk.
    /// </summary>
    /// <param name="name">The title.</param>
    /// <param name="year">The production year, if known.</param>
    /// <param name="entry">The entry to store.</param>
    public void Set(string name, int? year, CsfdCacheEntry entry)
    {
        lock (_lock)
        {
            _entries[BuildKey(name, year)] = entry;
            Save();
        }
    }

    /// <summary>
    /// Builds the cache key for a title/year pair.
    /// </summary>
    /// <param name="name">The title.</param>
    /// <param name="year">The production year, if known.</param>
    /// <returns>A normalized cache key.</returns>
    public static string BuildKey(string name, int? year)
    {
        ArgumentNullException.ThrowIfNull(name);
        return $"{name.Trim().ToLowerInvariant()}|{year?.ToString(CultureInfo.InvariantCulture) ?? string.Empty}";
    }

    private static Dictionary<string, CsfdCacheEntry> Load(string filePath, ILogger logger)
    {
        try
        {
            if (File.Exists(filePath))
            {
                var json = File.ReadAllText(filePath);
                var loaded = JsonSerializer.Deserialize<Dictionary<string, CsfdCacheEntry>>(json);
                if (loaded is not null)
                {
                    return loaded;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            logger.LogWarning(ex, "Nepodařilo se načíst ČSFD cache soubor {Path}, začínám s prázdnou cache.", filePath);
        }

        return new Dictionary<string, CsfdCacheEntry>();
    }

    private void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(_entries);
            File.WriteAllText(_filePath, json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Nepodařilo se uložit ČSFD cache soubor {Path}.", _filePath);
        }
    }
}
