#!/usr/bin/env bash
# Downloads McNeel's standalone yak.exe and installs tools/yak (macOS/Linux).
# On Mac with Rhino 8, you can use Rhino's script instead:
#   /Applications/Rhino 8.app/Contents/Resources/bin/yak
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
CLI_DIR="$ROOT/tools/yak-cli"
YAK_EXE="$CLI_DIR/yak.exe"
YAK_URL="https://files.mcneel.com/yak/tools/latest/yak.exe"
WRAPPER="$ROOT/tools/yak"

mkdir -p "$CLI_DIR"

if [[ ! -f "$YAK_EXE" ]]; then
  echo "Downloading yak.exe from McNeel..."
  curl -fsSL "$YAK_URL" -o "$YAK_EXE"
fi

cat > "$WRAPPER" << 'EOF'
#!/usr/bin/env bash
set -euo pipefail
DIR="$(cd "$(dirname "$0")" && pwd)"
YAK_EXE="$DIR/yak-cli/yak.exe"

if [[ ! -f "$YAK_EXE" ]]; then
  echo "yak.exe missing. Run: tools/install-yak.sh" >&2
  exit 1
fi

# Prefer Rhino's yak when installed — see discourse.mcneel.com/t/yak-on-mac/88589
RHINO_APP="${RHINO_MAC_APP:-}"
if [[ -z "$RHINO_APP" ]]; then
  for CANDIDATE in \
    "/Volumes/Storage/00_Applications/Rhino 8.app" \
    "/Applications/Rhino 8.app" \
    "/Applications/Rhinoceros.app"
  do
    [[ -d "$CANDIDATE" ]] && RHINO_APP="$CANDIDATE" && break
  done
fi
if [[ -n "$RHINO_APP" && -x "$RHINO_APP/Contents/Resources/bin/yak" ]]; then
  exec "$RHINO_APP/Contents/Resources/bin/yak" "$@"
fi

MONO="$(command -v mono || true)"
if [[ -z "$MONO" ]]; then
  echo "mono not found. Install Mono (brew install mono) or Rhino 8." >&2
  exit 1
fi

exec "$MONO" "$YAK_EXE" "$@"
EOF

chmod +x "$WRAPPER"
"$WRAPPER" --version
echo "Installed: $WRAPPER"
