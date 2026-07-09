namespace Jellyfin.Plugin.Csfd.Csfd;

/// <summary>
/// Which ČSFD search result section to look at ("Filmy" vs "Seriály").
/// </summary>
public enum CsfdItemKind
{
    /// <summary>
    /// A movie - looks under the "Filmy" search result section.
    /// </summary>
    Movie,

    /// <summary>
    /// A series - looks under the "Seriály" search result section.
    /// </summary>
    Series
}
