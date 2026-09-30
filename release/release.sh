#!/bin/bash
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
here="$root/release"
dist="$root/dist"

bepinex_version="5.4.2350"
bepinex_url="https://thunderstore.io/package/download/denikson/BepInExPack_Valheim/$bepinex_version/"
bepinex_sha256="37a91c000b4e88f2ed7a4bd7d812239852d2e36cbf0ff0a9f5faacfba46b105f"

mods=(
  "valheim-server-side-mod/src/ServerAuthority/ServerAuthority.csproj:ServerAuthority.dll"
  "valheim-rebalanced/src/ValheimRebalanced/ValheimRebalanced.csproj:ValheimRebalanced.dll"
  "valheim-creatures/src/ValheimCreatures/ValheimCreatures.csproj:ValheimCreatures.dll"
)
debug_only_types=(TestCommands SpawnRequests ShipDiagnosticPatches NearestWater)

if [ $# -ge 1 ]; then
  printf '%s\n' "$1" > "$root/VERSION"
fi
version="$(tr -d '[:space:]' < "$root/VERSION")"
if ! [[ "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
  echo "VERSION must be MAJOR.MINOR.PATCH, got '$version'" >&2
  exit 1
fi
echo "Releasing GoatTech Valheim mods $version"

cache="$here/cache"
pack_zip="$cache/BepInExPack_Valheim-$bepinex_version.zip"
mkdir -p "$cache"
if [ ! -f "$pack_zip" ]; then
  echo "Downloading BepInExPack_Valheim $bepinex_version"
  curl -fsSL -o "$pack_zip.part" "$bepinex_url"
  mv "$pack_zip.part" "$pack_zip"
fi
echo "$bepinex_sha256  $pack_zip" | sha256sum -c --quiet

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

unzip -q "$pack_zip" -d "$work/pack"
pack="$work/pack/BepInExPack_Valheim"

mkdir -p "$work/plugins"
for entry in "${mods[@]}"; do
  proj="$root/${entry%%:*}"
  dll="${entry##*:}"
  out="$work/build/${dll%.dll}"
  echo "Building $dll"
  env -u VALHEIM_PLUGINS dotnet build "$proj" -c Release -p:DebugTools=false --no-incremental \
    -nologo -v quiet -o "$out" > "$work/build-${dll%.dll}.log" 2>&1 || {
    cat "$work/build-${dll%.dll}.log" >&2
    exit 1
  }
  python3 - "$out/$dll" "$version" "${debug_only_types[@]}" <<'PY'
import sys
path, version, debug_types = sys.argv[1], sys.argv[2], sys.argv[3:]
data = open(path, "rb").read()
if version.encode("utf-16-le") not in data:
    sys.exit(f"{path} does not carry version {version}")
found = [t for t in debug_types if t.encode() in data]
if found:
    sys.exit(f"{path} contains debug tools: {', '.join(found)}")
PY
  cp "$out/$dll" "$work/plugins/"
done

tool_proj="$root/valheim-server-tool/src/ServerTool/ServerTool.csproj"
for rid in win-x64 linux-x64; do
  echo "Publishing ValheimServerTool for $rid"
  dotnet publish "$tool_proj" -c Release -r "$rid" -p:SelfContained=false -p:PublishSingleFile=true \
    -p:DebugType=none -nologo -v quiet -o "$work/servertool-$rid" > "$work/build-servertool-$rid.log" 2>&1 || {
    cat "$work/build-servertool-$rid.log" >&2
    exit 1
  }
done
if ! grep -q "$version" "$work/servertool-linux-x64/ValheimServerTool"; then
  echo "ValheimServerTool does not carry version $version" >&2
  exit 1
fi

stage_base() {
  local dest="$1"
  mkdir -p "$dest/BepInEx/plugins" "$dest/BepInEx/config"
  cp -r "$pack/BepInEx/core" "$dest/BepInEx/"
  cp "$pack/BepInEx/config/BepInEx.cfg" "$dest/BepInEx/config/"
  cp -r "$pack/doorstop_libs" "$dest/"
  cp "$pack/winhttp.dll" "$pack/doorstop_config.ini" "$pack/.doorstop_version" "$dest/"
  cp "$work/plugins/"*.dll "$dest/BepInEx/plugins/"
  printf 'GoatTech Valheim mods %s\nBepInExPack_Valheim %s\n' "$version" "$bepinex_version" > "$dest/GoatTech-VERSION.txt"
}

fill_readme() {
  sed "s/{{VERSION}}/$version/g" "$1" > "$2"
}

client_name="GoatTech-Valheim-client-$version"
client="$work/$client_name"
stage_base "$client/client-files"
cp "$pack/start_game_bepinex.sh" "$client/client-files/"
chmod +x "$client/client-files/start_game_bepinex.sh"
fill_readme "$here/client/README.txt" "$client/README.txt"

server_name="GoatTech-Valheim-server-$version"
server="$work/$server_name"
package="$root/valheim-server-side-mod/tools/package"
stage_base "$server/server-files"
cp "$pack/start_server_bepinex.sh" "$here/server/ServerAuthority-watchdog.sh" "$server/server-files/"
chmod +x "$server/server-files/start_server_bepinex.sh" "$server/server-files/ServerAuthority-watchdog.sh"
cp "$package"/ServerAuthority-*.bat "$package"/ServerAuthority-*.ps1 "$server/server-files/"
cp "$here/server/valheim.server_authority.cfg" "$server/server-files/BepInEx/config/"
mkdir -p "$server/server-files/servertool"
cp "$work/servertool-win-x64/ValheimServerTool.exe" "$work/servertool-linux-x64/ValheimServerTool" "$server/server-files/servertool/"
chmod +x "$server/server-files/servertool/ValheimServerTool"
fill_readme "$here/server/README.txt" "$server/README.txt"

mkdir -p "$dist"
for name in "$client_name" "$server_name"; do
  rm -f "$dist/$name.zip"
  (cd "$work" && zip -qrX "$dist/$name.zip" "$name")
done
(cd "$dist" && sha256sum "$client_name.zip" "$server_name.zip" > "GoatTech-Valheim-$version.sha256")

echo
echo "Done:"
ls -l "$dist/$client_name.zip" "$dist/$server_name.zip" "$dist/GoatTech-Valheim-$version.sha256"
