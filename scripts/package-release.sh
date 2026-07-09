#!/usr/bin/env bash
# Builds the plugin in Release mode and packages the artifacts declared in build.yaml into a
# release zip (dist/csfd-rating-{VERSION}.zip), then prints its MD5 checksum for manifest.json.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
CSPROJ="$REPO_ROOT/Jellyfin.Plugin.Csfd/Jellyfin.Plugin.Csfd.csproj"

VERSION="${1:-}"
if [[ -z "$VERSION" ]]; then
    VERSION="$(grep -m1 -oE '<Version>[^<]+</Version>' "$REPO_ROOT/Directory.Build.props" | sed -E 's/<\/?Version>//g')"
fi
if [[ -z "$VERSION" ]]; then
    echo "Could not determine version. Pass it explicitly: $0 <version>" >&2
    exit 1
fi

PUBLISH_DIR="$REPO_ROOT/publish"
DIST_DIR="$REPO_ROOT/dist"
ZIP_NAME="csfd-rating-$VERSION.zip"
ZIP_PATH="$DIST_DIR/$ZIP_NAME"

# Artifacts to ship, matching the "artifacts" list in build.yaml.
ARTIFACTS=(
    "Jellyfin.Plugin.Csfd.dll"
    "HtmlAgilityPack.dll"
)

echo "==> Publishing $CSPROJ (Release, version $VERSION)"
rm -rf "$PUBLISH_DIR"
dotnet publish "$CSPROJ" -c Release -o "$PUBLISH_DIR"

echo "==> Packaging $ZIP_PATH"
mkdir -p "$DIST_DIR"
rm -f "$ZIP_PATH"

STAGING_DIR="$(mktemp -d)"
trap 'rm -rf "$STAGING_DIR"' EXIT

for artifact in "${ARTIFACTS[@]}"; do
    src="$PUBLISH_DIR/$artifact"
    if [[ ! -f "$src" ]]; then
        echo "Expected artifact not found after publish: $src" >&2
        exit 1
    fi
    cp "$src" "$STAGING_DIR/"
done

(cd "$STAGING_DIR" && zip -q -X "$ZIP_PATH" "${ARTIFACTS[@]}")

echo "==> Computing MD5 checksum"
if command -v md5sum >/dev/null 2>&1; then
    CHECKSUM="$(md5sum "$ZIP_PATH" | awk '{print $1}')"
elif command -v md5 >/dev/null 2>&1; then
    CHECKSUM="$(md5 -q "$ZIP_PATH")"
else
    echo "Neither md5sum nor md5 is available on this system." >&2
    exit 1
fi

echo
echo "Built: $ZIP_PATH"
echo "Version: $VERSION"
echo "MD5 checksum: $CHECKSUM"
echo
echo "Put this value into manifest.json -> versions[] -> checksum for version $VERSION."
