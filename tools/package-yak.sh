#!/usr/bin/env bash
# Build UR-RTDE-Grasshopper yak packages (rh8 + rh7) with multi-target layout for Rhino 8.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
VERSION="${1:-}"
STAGE="$ROOT/bin/Release/yak-staging"
if [[ -x "/Volumes/Storage/00_Applications/Rhino 8.app/Contents/Resources/bin/yak" ]]; then
  YAK="/Volumes/Storage/00_Applications/Rhino 8.app/Contents/Resources/bin/yak"
elif [[ -n "${RHINO_MAC_APP:-}" && -x "${RHINO_MAC_APP}/Contents/Resources/bin/yak" ]]; then
  YAK="${RHINO_MAC_APP}/Contents/Resources/bin/yak"
else
  YAK="$ROOT/tools/yak"
fi

if [[ -z "$VERSION" ]]; then
  VERSION="$(grep -m1 '<Version>' "$ROOT/UR.RTDE.Grasshopper.csproj" | sed 's/.*<Version>\(.*\)<\/Version>.*/\1/')"
fi

if [[ ! -x "$YAK" ]]; then
  bash "$ROOT/tools/install-yak.sh"
fi

stage_framework() {
  local framework="$1"
  local src="$ROOT/bin/Release/$framework"
  local dest="$STAGE/$framework"

  if [[ ! -f "$src/UR.RTDE.Grasshopper.gha" ]]; then
    echo "Missing $src/UR.RTDE.Grasshopper.gha — run: dotnet build -c Release" >&2
    exit 1
  fi

  mkdir -p "$dest"
  cp "$src/UR.RTDE.Grasshopper.gha" "$dest/"
  cp "$src/UR.RTDE.dll" "$dest/"
  for pattern in ur_rtde_c_api.dll rtde.dll boost_thread-*.dll libur_rtde_c_api.dylib; do
    for f in "$src"/$pattern; do
      [[ -f "$f" ]] && cp "$f" "$dest/"
    done
  done
  if [[ -d "$src/runtimes" ]]; then
    cp -R "$src/runtimes" "$dest/"
  fi
}

rm -rf "$STAGE"
mkdir -p "$STAGE"
stage_framework net48
stage_framework net8.0-windows
stage_framework net8.0

# Mac/Linux Release builds may not copy Windows DLLs into net48; mirror from net8.0-windows for Rhino 7.
NET48_STAGE="$STAGE/net48"
WIN_STAGE="$STAGE/net8.0-windows"
if [[ ! -f "$NET48_STAGE/ur_rtde_c_api.dll" && -f "$WIN_STAGE/ur_rtde_c_api.dll" ]]; then
  for pattern in ur_rtde_c_api.dll rtde.dll boost_thread-*.dll; do
    for f in "$WIN_STAGE"/$pattern; do
      [[ -f "$f" ]] && cp "$f" "$NET48_STAGE/"
    done
  done
  if [[ -d "$WIN_STAGE/runtimes" && ! -d "$NET48_STAGE/runtimes" ]]; then
    cp -R "$WIN_STAGE/runtimes" "$NET48_STAGE/"
  fi
fi

cp "$ROOT/Resources/Icons/robot-duotone.png" "$STAGE/icon.png"

GUID="6d2ecd23-5f02-4314-9c8a-e5a5dc7a1c53"
cat > "$STAGE/manifest.yml" << EOF
name: UR-RTDE-Grasshopper
version: $VERSION
authors:
  - lasaths
description: >
  Grasshopper components to control Universal Robots via UR.RTDE (C# wrapper).
  Supports session management, reads (joints/pose/IO/modes), commands, and Robotiq grippers.
url: https://github.com/lasaths/UR.RTDE.Grasshopper
icon: icon.png
keywords:
  - universal-robots
  - rtde
  - robotics
  - grasshopper
  - guid:$GUID
EOF

cd "$STAGE"
OUT_DIR="$ROOT/bin/Release/net48"
rm -f "$OUT_DIR"/*.yak
"$YAK" build
PKG="$(ls -1 ur-rtde-grasshopper-"${VERSION}"-rh8_0-any.yak)"

verify_yak() {
  local yak_file="$1"
  local missing=0
  local listing
  listing="$(unzip -Z1 "$yak_file")"
  for required in \
    net48/UR.RTDE.Grasshopper.gha \
    net48/ur_rtde_c_api.dll \
    net8.0-windows/UR.RTDE.Grasshopper.gha \
    net8.0-windows/ur_rtde_c_api.dll \
    net8.0/UR.RTDE.Grasshopper.gha \
    net8.0-windows/runtimes/win-x64/native/ur_rtde_c_api.dll \
    net8.0/runtimes/osx-arm64/native/libur_rtde_c_api.dylib \
    net8.0/runtimes/osx-x64/native/libur_rtde_c_api.dylib; do
    if ! printf '%s\n' "$listing" | grep -qxF "$required"; then
      echo "ERROR: $yak_file missing: $required" >&2
      missing=1
    fi
  done
  return "$missing"
}

if ! verify_yak "$PKG"; then
  exit 1
fi
echo "Verified $PKG"
cp "$PKG" "${PKG/rh8_0/rh7_0}"
cp "$PKG" "$OUT_DIR/"
cp "${PKG/rh8_0/rh7_0}" "$OUT_DIR/"
echo ""
echo "Built:"
ls -1 "$OUT_DIR"/*.yak
echo ""
echo "Push (requires YAK_TOKEN or yak login):"
echo "  $YAK push $OUT_DIR/$PKG"
echo "  $YAK push $OUT_DIR/${PKG/rh8_0/rh7_0}"
