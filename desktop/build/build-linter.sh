#!/usr/bin/env bash
# Freezes the FastAPI linter into a single-file binary with PyInstaller and
# installs it as a Tauri sidecar. Uses a stable Python (the machine default may
# be a pre-release that PyInstaller doesn't support).
set -euo pipefail

REPO="$(cd "$(dirname "$0")/../.." && pwd)"
PYTHON="${PYTHON:-/opt/homebrew/bin/python3.12}"
TRIPLE="$(rustc -Vv | sed -n 's/host: //p')"
VENV="$REPO/desktop/build/linter-venv"
OUT="$REPO/desktop/src-tauri/binaries/lava-linter-$TRIPLE"

echo "Python:   $PYTHON ($($PYTHON --version))"
echo "Triple:   $TRIPLE"

# Fresh venv with the linter's runtime deps + PyInstaller.
rm -rf "$VENV"
"$PYTHON" -m venv "$VENV"
"$VENV/bin/pip" install --quiet --upgrade pip
"$VENV/bin/pip" install --quiet -r "$REPO/linter/requirements.txt" pyinstaller

# Freeze. Run from linter/ so the `app` package is importable.
cd "$REPO/linter"
rm -rf build dist lava-linter.spec
"$VENV/bin/pyinstaller" --onefile --name lava-linter --clean --noconfirm \
  --paths . \
  --collect-all uvicorn \
  --collect-all fastapi \
  --collect-all starlette \
  --collect-all pydantic \
  --collect-all pydantic_core \
  --collect-all anyio \
  desktop_launcher.py

mkdir -p "$(dirname "$OUT")"
cp "dist/lava-linter" "$OUT"
chmod +x "$OUT"
echo "Wrote sidecar: $OUT"
