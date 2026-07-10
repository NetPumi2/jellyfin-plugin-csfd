# ČSFD Rating (jellyfin-plugin-csfd)

![vibe coded](https://img.shields.io/badge/vibe%20coded-100%25-ff69b4)

A Jellyfin plugin that, during a metadata refresh, looks up a movie/series on
[ČSFD](https://www.csfd.cz) (the Czech-Slovak Film Database) by name and year and adds its
percentage rating as a `ČSFD: NN%` tag, visible as plain text on the item's detail page in the
Jellyfin UI.

(Earlier versions stored the rating as `CriticRating`, the same field Jellyfin uses for the
Rotten Tomatoes tomato icon - but that field is shared with whatever other metadata provider also
sets it, e.g. an OMDb/Rotten Tomatoes provider, and whichever one runs last silently overwrites
the other. Tags don't have that problem.)

## Important: ČSFD is protected by the Anubis anti-bot challenge

csfd.cz sits behind [Anubis](https://github.com/TecharoHQ/anubis), a proof-of-work anti-bot
challenge ("Making sure you're not a bot!"). A plain server-side HTTP request (with no JS engine)
cannot pass this challenge and gets back the challenge page's HTML instead of real content.

This plugin **does not automate solving the challenge** (that would be exactly the kind of
bot-detection evasion Anubis exists to stop). Instead, it sends a `Cookie` header on every
request, using a value you configure in the plugin's settings - you get that value by passing
the Anubis challenge once in your own regular browser and copying its cookies.

### How to get and set the cookie

1. Open `https://www.csfd.cz` in a regular browser and wait for Anubis to let you through to the
   real page.
2. Open DevTools (F12) → **Network** tab → click any request to `csfd.cz` → find the `Cookie`
   header under Request Headers and copy its whole value (one long string like
   `name1=value1; name2=value2; ...`).
   - Alternatively: DevTools → **Application** (Chrome) / **Storage** (Firefox) → **Cookies** →
     `https://www.csfd.cz`, and assemble the values into the same format by hand.
   - The most important one is the cookie Anubis itself sets after a successful challenge - at
     the time this plugin was written it's called `techaro.lol-anubis-auth` (Anubis is an
     open-source project by Techaro and this is its default cookie name, which ČSFD apparently
     hasn't renamed) - but since that could change at any time, it's safer to just copy every
     cookie for the domain.
3. In Jellyfin: **Dashboard → Plugins → ČSFD Rating → Settings**, paste the whole string into the
   "ČSFD Cookie" field and save.

This session will eventually expire - based on how Anubis sessions typically work, that's likely
on the order of **days** (this is only a rough estimate, not a guaranteed value; ČSFD may
configure its own expiry differently). Once ratings stop being picked up, check the Jellyfin log -
when the cookie is expired/invalid, the plugin logs a clear message ("ČSFD cookie has expired or
is invalid - refresh it in the ČSFD Rating plugin settings"), and you just repeat the steps above.

## How the plugin works

1. For an item (`Movie`/`Series`), it takes `item.Name` and `item.ProductionYear` and builds the
   search URL `https://www.csfd.cz/hledat/?q={name}+{year}`.
2. It downloads the HTML (with the cookie from configuration) and, within the "Filmy" (movies) or
   "Seriály" (series) section matching the item's type, picks the first result whose listed year
   matches `item.ProductionYear` (ČSFD often lists several unrelated titles that share a name -
   remakes, unrelated foreign films, etc.). If no result's year matches (or the year isn't known),
   it falls back to the plain first result in the section.
3. It downloads that movie's/series's page and extracts the percentage from
   `<div class="film-rating-average">63%</div>`. If ČSFD shows `?` (not enough ratings yet), no
   rating is set.
4. It adds a `ČSFD: NN%` tag to `item.Tags` (replacing any `ČSFD: ...` tag left over from an
   earlier refresh) and stores the ČSFD URL as a provider id (`Csfd`) on the item.
5. The result (including "not found") is cached by name+year for the duration configured in
   settings (default 14 days / 336 hours), so ČSFD isn't re-scraped on every single refresh.

Error handling: a missing/invalid cookie, a timeout, no matching search result, or a change in
ČSFD's page structure are all just logged (`ILogger`) - the item is left without a ČSFD tag and
the library refresh keeps going; the plugin never crashes over a single item. All of the steps
above log an Info-level line (search URL, resolved ČSFD link, found rating or the reason it
wasn't set, whether the tag was actually added) - see **Troubleshooting** below for how to read
them.

**Known limitations:** if ČSFD lists more than one same-titled, same-year result (rare, but
possible), the first one in that year is used as-is. ČSFD's page structure may change over time
and break parsing - if that happens, update the selectors in
`Jellyfin.Plugin.Csfd/Csfd/CsfdHtmlParser.cs`.

## Build

Requires .NET SDK 9.0. The `Jellyfin.Controller`/`Jellyfin.Model` NuGet package version in the
`.csproj` is pinned to match the exact Jellyfin server version this build targets (currently
`10.11.6`) - see the note on ABI/patch-version compatibility below before bumping it.

```bash
dotnet build Jellyfin.Plugin.Csfd.sln
```

Tests:

```bash
dotnet test tests/Jellyfin.Plugin.Csfd.Tests/Jellyfin.Plugin.Csfd.Tests.csproj
```

Publish (produces the `.dll` files needed for installation):

```bash
dotnet publish Jellyfin.Plugin.Csfd/Jellyfin.Plugin.Csfd.csproj -c Release -o publish
```

Among the output in `publish/`, the plugin needs these two files (the rest - `.pdb`, `.xml`,
`.deps.json` - are optional, Jellyfin doesn't need them):

- `Jellyfin.Plugin.Csfd.dll`
- `HtmlAgilityPack.dll`

## Releasing a new version (packaging + manifest.json)

This repo is public and is meant to be installed as a custom Jellyfin plugin repository, backed
by `manifest.json` at the repo root (served via raw.githubusercontent.com) and a `.zip` per
version attached to a GitHub Release.

1. Bump the version in `Directory.Build.props` (and `build.yaml`) if this isn't `1.0.0.4`.
2. Build and package the release zip:

   ```bash
   ./scripts/package-release.sh
   # or explicitly: ./scripts/package-release.sh 1.2.0.0
   ```

   This runs `dotnet publish -c Release`, zips `Jellyfin.Plugin.Csfd.dll` and
   `HtmlAgilityPack.dll` (the artifacts listed in `build.yaml`) into
   `dist/csfd-rating-{VERSION}.zip`, and prints its MD5 checksum.
3. Add a new entry to the `versions` array in `manifest.json` (newest first) with that `version`,
   `checksum`, a `sourceUrl` following the pattern
   `https://github.com/NetPumi2/jellyfin-plugin-csfd/releases/download/v{VERSION}/csfd-rating-{VERSION}.zip`,
   and an updated `changelog`/`timestamp`. `targetAbi` should match the exact `Jellyfin.Controller`
   NuGet version used in the `.csproj` (currently `10.11.6.0`) - see the note below.
4. Commit and push `manifest.json` - see **Manual steps** below for creating the matching GitHub
   Release and uploading the zip (that part needs your GitHub login and can't be automated here).

**Important - `targetAbi` is not just a "minimum version" in practice.** Jellyfin's plugin loader
calls `assembly.GetTypes()` on every plugin DLL at startup; if the plugin was compiled against a
newer/older `Jellyfin.Controller`/`Jellyfin.Model` than what the running server actually ships,
even a small API difference between patch releases (e.g. `10.11.6` vs `10.11.11`, same minor
line) can throw a `TypeLoadException`/`ReflectionTypeLoadException` there, which Jellyfin reports
as plugin status **NotSupported** - not a targetAbi version-number check failing, an actual binary
incompatibility. The safe rule: **pin the `Jellyfin.Controller`/`Jellyfin.Model` package versions
in the `.csproj` to the exact version of the Jellyfin server(s) you're targeting**, and set
`targetAbi` to that same exact version (`X.Y.Z.0`) rather than a lower "minimum" like `X.Y.0.0`.
If you need to support multiple server versions, you'd need a separate build (and `manifest.json`
version entry) per target.

`dist/` and `publish/` are gitignored - only `manifest.json` itself is committed; the zip lives
solely as a GitHub Release asset, never in git history.

## Installing on Jellyfin (Docker on pinas)

Two ways to install this plugin: as a **custom plugin repository** (recommended - lets Jellyfin
notify you about future updates), or by **manually copying files** into the plugins folder.

### Option A: custom plugin repository (recommended)

Once a GitHub Release with the packaged zip exists (see **Manual steps** at the end of this
README), add the repository once in Jellyfin:

1. **Dashboard → Plugins → Repositories → (+) New Repository**
2. Repository Name: `ČSFD Rating` (or anything you like)
3. Repository URL: `https://raw.githubusercontent.com/NetPumi2/jellyfin-plugin-csfd/main/manifest.json`
4. Save, then go to **Dashboard → Plugins → Catalog**, find "ČSFD Rating" under the "Metadata"
   category, and install it.
5. Check **Dashboard → Plugins → My Plugins** to confirm it installed and shows as **Active**.
6. Open **ČSFD Rating → Settings** and paste in your csfd.cz cookie (see
   [How to get and set the cookie](#how-to-get-and-set-the-cookie) above) - without it the rating
   lookup will never work.

Future releases just need a new GitHub Release + an updated `manifest.json` entry; Jellyfin will
offer the update from the Catalog like any other plugin.

### Option B: manual copy (no public repo/release needed)

Jellyfin loads plugins from subdirectories `plugins/<Name>_<version>/` inside its config
directory (typically mounted as a volume, e.g. `/config` inside the container). Steps:

1. **Find the plugins directory path.** On `pinas` (over SSH as `salek`), find where the Jellyfin
   config volume is mounted:

   ```bash
   docker inspect <jellyfin_container_name> --format '{{ range .Mounts }}{{ .Source }} -> {{ .Destination }}{{ "\n" }}{{ end }}'
   ```

   Look for the line where `Destination` is `/config` (or similar) - `Source` is the path on the
   host (pinas) you can write to directly, without needing to copy files into the container.
   Inside the config directory, plugins usually live under `plugins/` (i.e.
   `<Source>/plugins/`).

2. **Create the plugin's subdirectory** (the version must match the one in `meta.json`/`build.yaml`):

   ```bash
   mkdir -p "<Source>/plugins/ČSFD Rating_1.0.0.4"
   ```

3. **Copy the DLL files and meta.json** from `publish/` into that directory:

   ```bash
   cp publish/Jellyfin.Plugin.Csfd.dll publish/HtmlAgilityPack.dll "<Source>/plugins/ČSFD Rating_1.0.0.4/"
   ```

   `meta.json` (bump `version`/`timestamp` for later releases) - contents:

   ```json
   {
     "category": "Metadata",
     "changelog": "1.0.0.4 - Fix: prefer the search result matching the item's year; add detailed Info-level logging.",
     "description": "Looks up a movie/series on ČSFD by name and year during a metadata refresh and adds its percentage rating as a \"ČSFD: NN%\" tag.",
     "guid": "200ed2e9-c3b4-4c8a-a8ae-b90fc6b635b8",
     "name": "ČSFD Rating",
     "overview": "Shows the ČSFD rating (in %) for movies and series.",
     "owner": "NetPumi2",
     "targetAbi": "10.11.6.0",
     "version": "1.0.0.4",
     "status": 0,
     "autoUpdate": false
   }
   ```

   (The `assemblies` field can be omitted - when it's missing or empty, Jellyfin automatically
   loads every `.dll` file in the plugin's directory, so both `Jellyfin.Plugin.Csfd.dll` and
   `HtmlAgilityPack.dll` get picked up.)

4. **Restart the Jellyfin container** so the plugin gets loaded:

   ```bash
   docker restart <jellyfin_container_name>
   ```

5. Go to **Dashboard → Plugins**, confirm "ČSFD Rating" shows up and is active, open its
   **Settings**, and set the cookie as described above.

## Manual verification after deployment

1. In the plugin's settings, confirm "Enable ČSFD rating lookup" is checked and the cookie field
   is filled in.
2. In Jellyfin, go to a library with a few movies/series (ideally ones whose ČSFD rating differs
   noticeably from their IMDb rating, so the change is easy to spot) → **⋮ → Scan Library** (a
   plain scan is enough as of 1.0.0.3 - see note below - but **Dashboard → Libraries → (your
   library) → Scan** with "Replace all metadata" enabled works too and is a good way to force an
   immediate re-check of everything, e.g. right after first installing the plugin).
3. Once the scan finishes, open a movie's/series's detail page and check that a `ČSFD: NN%` tag
   with a percentage matching that item's ČSFD rating now shows up among its tags (double-check
   manually on csfd.cz that it matches).
4. If the rating didn't show up, check the Jellyfin log (**Dashboard → Logs**, or the log file
   directly in the config directory) for lines from `Jellyfin.Plugin.Csfd` - the most common
   causes are:
   - the cookie is missing/expired (a message about the Anubis challenge / refreshing the cookie),
   - the item doesn't have enough ratings on ČSFD yet (`?`),
   - the search didn't find any result (an ambiguous or unusual title).

**Why a plain "Scan Library" didn't work on versions before 1.0.0.3:** Jellyfin only re-runs a
custom metadata provider for an item that's already been scanned before if that provider reports
"this item changed" via `IHasItemChangeMonitor` - otherwise it's skipped entirely (to avoid
needlessly re-running every provider on every scan). Versions before 1.0.0.3 didn't implement
that interface, so the ČSFD provider only ever ran for brand-new items or during a forced
"Replace all metadata" refresh. 1.0.0.3 fixes this: it reports a change whenever there's no
still-valid cached ČSFD result for the item yet (never looked up, or the cache TTL expired), so a
routine "Scan Library" (or the periodic scheduled library scan) now picks those items up too.

## Troubleshooting

If a tag still doesn't show up after a "Replace all metadata" refresh, check these in order (all
via SSH into the machine running the Jellyfin container/config - substitute `<CONFIG>` with the
host path you found under **Installing on Jellyfin** above, e.g. via `docker inspect`):

1. **Is the cookie actually saved?** The plugin's config is an XML file named after the plugin
   assembly, at `<CONFIG>/plugins/configurations/Jellyfin.Plugin.Csfd.xml`. Check it directly
   instead of trusting the settings page:

   ```bash
   grep -A1 CsfdSessionCookie "<CONFIG>/plugins/configurations/Jellyfin.Plugin.Csfd.xml"
   ```

   If `<CsfdSessionCookie>` is empty (`<CsfdSessionCookie />` or `<CsfdSessionCookie></CsfdSessionCookie>`),
   the value in the Settings page was never actually saved (e.g. "Save" wasn't clicked, or the
   page reloaded before the request finished) - go back to Settings, paste the cookie again, and
   confirm you see the "Settings saved" confirmation toast.

2. **What does the log say happened for that specific item?** As of 1.0.0.4 every step logs at
   **Info** level (not just Debug/Warning), so it's visible without turning on verbose logging.
   If Jellyfin runs in Docker, tail the container's own logs and filter for the plugin:

   ```bash
   docker logs -f --since 10m <jellyfin_container_name> 2>&1 | grep -i "csfd"
   ```

   (or, without `-f`, grep the on-disk log file instead: `grep -i csfd "<CONFIG>/log/log_*.log"`,
   picking the most recent one). Then trigger the refresh again and watch for, in order:
   - `ČSFD: zpracovávám 'Roofman' (2025)` - confirms the provider actually ran for this item at all.
     If this line never appears, the provider was skipped entirely (wrong item type, plugin
     disabled, or - for a non-"Replace all metadata" scan - `IHasItemChangeMonitor` decided
     nothing needed refetching; see the cache check below).
   - `ČSFD: search URL pro 'Roofman' (2025): https://www.csfd.cz/hledat/?q=Roofman+2025` - the
     exact URL requested.
   - Either `ČSFD: nalezený odkaz pro 'Roofman' (2025): https://www.csfd.cz/film/...` or
     `ČSFD: odkaz nenalezen pro 'Roofman' (2025) - žádný výsledek v sekci vyhledávání.`
   - A warning containing `Anubis ochrana zablokovala request` means the cookie is missing/expired
     - go back to step 1.
   - Either `ČSFD: nalezené hodnocení pro 'Roofman' (2025): NN%` or a line explaining why not
     (rating element missing / shows "?").
   - Finally `ČSFD: Tag přidán pro 'Roofman': "ČSFD: NN%"`, or `ČSFD: Tag NEpřidán pro '...', důvod: ...`
     spelling out exactly why nothing was written to the item.

3. **Is there a stale cache entry blocking a retry?** Results (including "not found"/unrated) are
   cached in a JSON file per name+year, so a bad result from an earlier attempt (e.g. before the
   cookie was set) sticks around until the configured TTL expires. Find and inspect it:

   ```bash
   find "<CONFIG>" -iname "csfd-cache.json"
   cat "<the path that found>" | python3 -m json.tool | grep -B1 -A3 -i roofman
   ```

   The key format is `"<lowercased name>|<year>"`, e.g. `"roofman|2025"`. To force an immediate
   retry for just that title, remove its key from the JSON file (or delete the whole file to
   clear everything) while Jellyfin is stopped, then start it again and re-run the refresh. You
   can also just lower `CacheTtlHours` in the plugin settings temporarily and wait it out instead
   of editing the file directly.

4. **Does the ČSFD search actually return the right film for this title/year?** Open
   `https://www.csfd.cz/hledat/?q=Roofman+2025` yourself in a browser (same query the plugin
   builds) and confirm the first result in the "Filmy" section for that year is the film you
   expect - ČSFD sometimes lists several unrelated titles sharing a name. As of 1.0.0.4 the plugin
   matches on year (see **How the plugin works** above), but if ČSFD's HTML structure changes
   this could still silently break; the log lines from step 2 tell you exactly which URL it
   actually picked so you can compare.

## Project layout

```
Jellyfin.Plugin.Csfd/
  Plugin.cs                        - plugin entry point (BasePlugin<PluginConfiguration>)
  Configuration/
    PluginConfiguration.cs         - Enabled, CacheTtlHours, CsfdSessionCookie
    configPage.html                - the Dashboard settings page
  Csfd/
    CsfdClient.cs                  - HTTP calls to csfd.cz (via IHttpClientFactory)
    CsfdHtmlParser.cs              - pure parsing logic (HtmlAgilityPack), no I/O - unit-testable
    CsfdCache.cs / CsfdCacheEntry.cs - JSON file cache with TTL
    CsfdServices.cs                - shared client/cache instance for both providers
    CsfdItemKind.cs / CsfdLookupResult.cs
  Providers/
    CsfdMovieProvider.cs           - ICustomMetadataProvider<Movie>
    CsfdSeriesProvider.cs          - ICustomMetadataProvider<Series>
    CsfdMetadataUpdater.cs         - shared cache→lookup→apply logic for both providers
tests/Jellyfin.Plugin.Csfd.Tests/
  CsfdHtmlParserTests.cs           - xUnit tests against real HTML fixture files
  Fixtures/                        - saved ČSFD HTML samples (search results, film detail,
                                     Anubis challenge page) for testing without network access
```

## Manual steps (need your GitHub/Jellyfin login - can't be automated)

To finish publishing v1.0.0.4 and make the plugin catalog installable:

1. **Create the GitHub Release.** On GitHub, go to the repo → **Releases → Draft a new release**.
   - Tag: `v1.0.0.4` (create it on publish, targeting `main`)
   - Title: e.g. `v1.0.0.4`
   - Attach `dist/csfd-rating-1.0.0.4.zip` (built by `./scripts/package-release.sh`) under
     **Attach binaries by dropping them here**.
   - Publish the release. This must produce the download URL already referenced in
     `manifest.json`:
     `https://github.com/NetPumi2/jellyfin-plugin-csfd/releases/download/v1.0.0.4/csfd-rating-1.0.0.4.zip`
   - The earlier `v1.0.0.0`/`v1.0.0.1`/`v1.0.0.2`/`v1.0.0.3` releases/tags (if you already created
     them) can be left as-is for history, or deleted - your choice. `manifest.json` now points
     people at `1.0.0.4` first either way.
2. **Add the repository in Jellyfin.** Dashboard → Plugins → Repositories → New Repository:
   - Repository Name: `ČSFD Rating` (or anything)
   - Repository URL: `https://raw.githubusercontent.com/NetPumi2/jellyfin-plugin-csfd/main/manifest.json`
3. **Install the plugin.** Dashboard → Plugins → Catalog → find "ČSFD Rating" (category
   "Metadata") → Install. Then check Dashboard → Plugins → My Plugins to confirm it's listed and
   **Active**.
4. **Set the ČSFD cookie.** Dashboard → Plugins → ČSFD Rating → Settings → paste your csfd.cz
   cookie into the "ČSFD Cookie" field and save - see
   [How to get and set the cookie](#how-to-get-and-set-the-cookie) above. Without this, ratings
   will never be looked up (every request gets blocked by the Anubis challenge).
