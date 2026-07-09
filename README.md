# ČSFD Rating (jellyfin-plugin-csfd)

Jellyfin plugin, který při refreshi metadat dohledá film/seriál na [ČSFD](https://www.csfd.cz)
podle názvu a roku a jeho procentuální hodnocení uloží jako `CriticRating` - v Jellyfin UI se pak
zobrazí vedle názvu položky stejně jako Rotten Tomatoes rajčátko (`🍅 63`), jen s hodnotou z ČSFD.

## Důležité: ČSFD je chráněné Anubis anti-bot ochranou

csfd.cz je za [Anubis](https://github.com/TecharoHQ/anubis) proof-of-work anti-bot ochranou
("Making sure you're not a bot!"). Obyčejný HTTP request ze serveru (bez JS enginu) tuhle výzvu
neprojde a dostane jen HTML challenge stránky místo skutečného obsahu.

Tenhle plugin **automatickou výzvu neřeší** (to by byl přesně ten druh detection-evasion bota,
proti kterému Anubis existuje). Místo toho posílá s každým requestem `Cookie` hlavičku, kterou mu
nastavíš v konfiguraci pluginu - hodnotu získáš tak, že jednou projdeš Anubis výzvu ve svém
běžném prohlížeči a zkopíruješ jeho cookies.

### Jak cookie získat a nastavit

1. Otevři `https://www.csfd.cz` v běžném prohlížeči a počkej, až tě Anubis pustí na normální stránku.
2. Otevři DevTools (F12) → záložka **Network** → klikni na libovolný request na `csfd.cz` → v
   Request Headers najdi hlavičku `Cookie` a zkopíruj celou její hodnotu (je to jeden dlouhý
   řetězec typu `jmeno1=hodnota1; jmeno2=hodnota2; ...`).
   - Alternativně: DevTools → **Application** (Chrome) / **Storage** (Firefox) → **Cookies** →
     `https://www.csfd.cz`, a hodnoty poskládej ručně do stejného formátu.
   - Nejdůležitější je cookie, kterou nastavuje samotný Anubis po úspěšném projití výzvy - v době
     psaní tohohle pluginu se jmenuje `techaro.lol-anubis-auth` (Anubis je open-source projekt
     Techaro a tohle je jeho výchozí název cookie, ČSFD ho zjevně nepřejmenovala) - ale protože se
     to může kdykoliv změnit, je bezpečnější zkopírovat rovnou všechny cookies pro doménu.
3. V Jellyfinu: **Dashboard → Plugins → ČSFD Rating → Settings**, vlož celý řetězec do pole
   "ČSFD Cookie" a ulož.

Tahle relace časem vyprší - podle toho, jak Anubis relace obvykle fungují, půjde řádově o **dny**
(je to jen orientační odhad, ne garantovaná hodnota, ČSFD si dobu platnosti může nastavit jinak).
Až hodnocení přestanou přibývat, zkontroluj Jellyfin log - plugin do něj při vypršelé/neplatné
cookie napíše jasnou hlášku "ČSFD cookie vypršela nebo je neplatná - obnov ji v nastavení pluginu
ČSFD Rating", a stačí zopakovat kroky výše.

## Jak plugin funguje

1. Pro položku (`Movie`/`Series`) vezme `item.Name` a `item.ProductionYear` a sestaví vyhledávací
   URL `https://www.csfd.cz/hledat/?q={název}+{rok}`.
2. Stáhne HTML (s cookie z konfigurace) a v sekci "Filmy" (nebo "Seriály" pro seriály) vezme první
   výsledek.
3. Stáhne stránku toho filmu/seriálu a z `<div class="film-rating-average">63%</div>` vytáhne
   procento. Pokud ČSFD ukazuje `?` (nedost hodnocení), rating se nenastaví.
4. Nastaví `item.CriticRating` na tuhle hodnotu (0-100 škála, 1:1 s ČSFD procenty) a ČSFD URL uloží
   jako provider id (`Csfd`) na položce.
5. Výsledek (i "nenalezeno") se cachuje podle názvu+roku na dobu nastavenou v konfiguraci (výchozí
   14 dní / 336 hodin), takže se ČSFD nescrapuje znovu při každém refreshi.

Detekce chyb: chybějící/neplatná cookie, timeout, nenalezený výsledek nebo změna struktury ČSFD
stránky se jen zaloguje (`ILogger`) - položka zůstane bez `CriticRating` a refresh knihovny
pokračuje dál, plugin nikdy nespadne kvůli jedné položce.

**Známé limity:** bere se první výsledek vyhledávání - u nejednoznačných názvů (více filmů se
stejným jménem a rokem, remaky apod.) se může přiřadit špatná položka. Struktura ČSFD stránek se
může časem změnit a rozbít parsing - v takovém případě uprav selektory v
`Jellyfin.Plugin.Csfd/Csfd/CsfdHtmlParser.cs`.

## Build

Vyžaduje .NET SDK 9.0 (stejná verze, se kterou je postavený balíček `Jellyfin.Controller`
10.11.11 - odpovídá aktuální stabilní řadě Jellyfin serveru 10.11.x).

```bash
dotnet build Jellyfin.Plugin.Csfd.sln
```

Testy:

```bash
dotnet test tests/Jellyfin.Plugin.Csfd.Tests/Jellyfin.Plugin.Csfd.Tests.csproj
```

Publish (vytvoří `.dll` soubory pro instalaci):

```bash
dotnet publish Jellyfin.Plugin.Csfd/Jellyfin.Plugin.Csfd.csproj -c Release -o publish
```

V `publish/` budou mj. tyhle dva soubory, které plugin potřebuje (ostatní - `.pdb`, `.xml`,
`.deps.json` - jsou volitelné, Jellyfin je nepotřebuje):

- `Jellyfin.Plugin.Csfd.dll`
- `HtmlAgilityPack.dll`

## Instalace do Jellyfinu (Docker na pinas)

Jellyfin načítá pluginy z podadresářů `plugins/<Název>_<verze>/` uvnitř svého config adresáře
(typicky namountovaného jako volume, např. `/config` uvnitř kontejneru). Postup:

1. **Najdi cestu k plugins adresáři.** Na `pinas` (přes SSH jako `salek`) zjisti, kam je
   namountovaný Jellyfin config volume:

   ```bash
   docker inspect <jméno_kontejneru_jellyfin> --format '{{ range .Mounts }}{{ .Source }} -> {{ .Destination }}{{ "\n" }}{{ end }}'
   ```

   Hledej řádek, kde `Destination` je `/config` (nebo podobně) - `Source` je cesta na hostu
   (pinas), kam se dá zapisovat přímo, aniž bys musel kopírovat dovnitř kontejneru. Uvnitř
   config adresáře pak plugins bývají v `plugins/` (tzn. `<Source>/plugins/`).

2. **Vytvoř podadresář pluginu** (verze musí sedět s tou v `meta.json`/`build.yaml`):

   ```bash
   mkdir -p "<Source>/plugins/ČSFD Rating_1.0.0.0"
   ```

3. **Zkopíruj DLL soubory a meta.json** z `publish/` do tohohle adresáře:

   ```bash
   cp publish/Jellyfin.Plugin.Csfd.dll publish/HtmlAgilityPack.dll "<Source>/plugins/ČSFD Rating_1.0.0.0/"
   ```

   `meta.json` (uprav `version`/`timestamp` při dalších verzích) - obsah:

   ```json
   {
     "category": "Metadata",
     "changelog": "1.0.0.0 - Initial release: ČSFD rating lookup for movies and series via CriticRating.",
     "description": "Při refreshi metadat vyhledá film/seriál na ČSFD podle názvu a roku a jeho procentuální hodnocení uloží jako CriticRating.",
     "guid": "200ed2e9-c3b4-4c8a-a8ae-b90fc6b635b8",
     "name": "ČSFD Rating",
     "overview": "Zobrazí ČSFD hodnocení (v %) u filmů a seriálů.",
     "owner": "NetPumi2",
     "targetAbi": "10.11.0.0",
     "version": "1.0.0.0",
     "status": 0,
     "autoUpdate": false
   }
   ```

   (Pole `assemblies` se dá vynechat - když chybí nebo je prázdné, Jellyfin automaticky nahraje
   všechny `.dll` soubory v adresáři pluginu, takže se použije jak `Jellyfin.Plugin.Csfd.dll`, tak
   `HtmlAgilityPack.dll`.)

4. **Restartuj kontejner Jellyfin**, ať se plugin načte:

   ```bash
   docker restart <jméno_kontejneru_jellyfin>
   ```

5. Jdi do **Dashboard → Plugins**, ověř že se "ČSFD Rating" objevil a je aktivní, otevři jeho
   **Settings** a nastav cookie podle návodu výše.

## Ruční ověření po nasazení

1. V nastavení pluginu zkontroluj, že je zaškrtnuté "Povolit vyhledávání ČSFD hodnocení" a že je
   vyplněná cookie.
2. V Jellyfinu jdi do knihovny s pár filmy/seriály (ideálně takovými, co mají výrazně odlišné ČSFD
   hodnocení od IMDb, ať je změna dobře vidět) → **⋮ → Skenovat knihovnu médií** (nebo přes
   **Dashboard → Knihovny → (tvoje knihovna) → Skenovat** s zapnutým "Vynutit znovu stažení
   metadat obrázků" pokud chceš čerstvý refresh i pro už naskenované položky).
3. Po dokončení skenu otevři detail filmu/seriálu a zkontroluj, že se u něj objevila
   rajčátková ikona (🍅) s procentem odpovídajícím ČSFD hodnocení dané položky (zkontroluj ručně
   na csfd.cz, že sedí).
4. Pokud se rating neobjevil, zkontroluj Jellyfin log (**Dashboard → Logs** nebo přímo soubor v
   config adresáři) a hledej řádky od `Jellyfin.Plugin.Csfd` - nejčastější příčiny:
   - cookie chybí/vypršela (hláška o Anubis ochraně / obnovení cookie),
   - položka nemá na ČSFD dost hodnocení (`?`),
   - vyhledávání nenašlo žádný výsledek (nejednoznačný/neobvyklý název).

## Struktura projektu

```
Jellyfin.Plugin.Csfd/
  Plugin.cs                        - vstupní bod pluginu (BasePlugin<PluginConfiguration>)
  Configuration/
    PluginConfiguration.cs         - Enabled, CacheTtlHours, CsfdSessionCookie
    configPage.html                - konfigurační stránka v Dashboardu
  Csfd/
    CsfdClient.cs                  - HTTP volání na csfd.cz (přes IHttpClientFactory)
    CsfdHtmlParser.cs              - čistá parsing logika (HtmlAgilityPack), bez I/O - testovatelná
    CsfdCache.cs / CsfdCacheEntry.cs - JSON file cache s TTL
    CsfdServices.cs                - sdílená instance klienta/cache pro oba providery
    CsfdItemKind.cs / CsfdLookupResult.cs
  Providers/
    CsfdMovieProvider.cs           - ICustomMetadataProvider<Movie>
    CsfdSeriesProvider.cs          - ICustomMetadataProvider<Series>
    CsfdMetadataUpdater.cs         - sdílená logika cache→lookup→apply pro oba providery
tests/Jellyfin.Plugin.Csfd.Tests/
  CsfdHtmlParserTests.cs           - xUnit testy nad reálnými HTML fixture soubory
  Fixtures/                        - uložené ukázky ČSFD HTML (výsledky hledání, detail filmu,
                                     Anubis challenge stránka) pro testování bez síťového přístupu
```
