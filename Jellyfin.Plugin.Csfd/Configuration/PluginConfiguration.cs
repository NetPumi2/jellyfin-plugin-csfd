using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.Csfd.Configuration;

/// <summary>
/// Plugin configuration.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PluginConfiguration"/> class.
    /// </summary>
    public PluginConfiguration()
    {
        Enabled = true;
        CacheTtlHours = 336; // 14 days, same default as the TreZzoR Watchlist Chrome extension.
        CsfdSessionCookie = string.Empty;
    }

    /// <summary>
    /// Gets or sets a value indicating whether the plugin should look up ČSFD ratings.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets or sets the number of hours a resolved ČSFD rating is cached before being re-fetched.
    /// </summary>
    public int CacheTtlHours { get; set; }

    /// <summary>
    /// Gets or sets the raw <c>Cookie</c> header value captured from a real browser session on
    /// csfd.cz, used to get past the site's Anubis anti-bot proof-of-work challenge. Without it,
    /// requests from this plugin will almost always receive the Anubis challenge page instead of
    /// real content. See the plugin README for how to obtain this value.
    /// </summary>
    public string CsfdSessionCookie { get; set; }
}
