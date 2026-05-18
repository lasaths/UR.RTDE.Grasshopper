#!/usr/bin/env bash
# Build UR-RTDE-Grasshopper yak packages (rh8 + rh7) from Release/net48 output.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
VERSION="${1:-}"
OUT_DIR="$ROOT/bin/Release/net48"
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

if [[ ! -f "$OUT_DIR/UR.RTDE.Grasshopper.gha" ]]; then
  echo "Missing $OUT_DIR/UR.RTDE.Grasshopper.gha — run: dotnet build -c Release" >&2
  exit 1
fi

rm -rf "$STAGE"
mkdir -p "$STAGE"
cp "$OUT_DIR/UR.RTDE.Grasshopper.gha" "$STAGE/"
cp "$OUT_DIR/UR.RTDE.dll" "$STAGE/"
# Flat natives beside the GHA (Rhino P/Invoke probes the plugin folder, not runtimes/ only).
for pattern in ur_rtde_c_api.dll rtde.dll boost_thread-*.dll libur_rtde_c_api.dylib; do
  for f in "$OUT_DIR"/$pattern; do
    [[ -f "$f" ]] && cp "$f" "$STAGE/"
  done
done
[[ -d "$OUT_DIR/runtimes" ]] && cp -R "$OUT_DIR/runtimes" "$STAGE/"
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
rm -f "$OUT_DIR"/*.yak
"$YAK" build
PKG="$(ls -1 ur-rtde-grasshopper-"${VERSION}"-rh8_0-any.yak)"

verify_yak() {
  local yak_file="$1"
  local missing=0
  local listing
  listing="$(unzip -Z1 "$yak_file")"
  for required in \
    UR.RTDE.Grasshopper.gha \
    UR.RTDE.dll \
    ur_rtde_c_api.dll \
    rtde.dll \
    libur_rtde_c_api.dylib \
    runtimes/win-x64/native/ur_rtde_c_api.dll \
    runtimes/osx-arm64/native/libur_rtde_c_api.dylib; do
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
