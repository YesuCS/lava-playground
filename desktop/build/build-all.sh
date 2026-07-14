#!/usr/bin/env bash
# Build the whole Lava Playground desktop app from scratch:
#   1. C# render API   -> self-contained single-file sidecar
#   2. Python linter   -> PyInstaller single-file sidecar
#   3. Vue frontend    -> static bundle pointed at the sidecar ports
#   4. Tauri           -> .app + .dmg
#
# Requirements: .NET SDK, a stable Python (see PYTHON below), Node, and the
# Rust toolchain (https://rustup.rs). Run from anywhere:  bash build-all.sh
set -euo pipefail

REPO="$(cd "$(dirname "$0")/../.." && pwd)"
export PATH="$HOME/.cargo/bin:$PATH"
PYTHON="${PYTHON:-/opt/homebrew/bin/python3.12}"
TRIPLE="$(rustc -Vv | sed -n 's/host: //p')"
BIN_DIR="$REPO/desktop/src-tauri/binaries"
mkdir -p "$BIN_DIR"

echo "==> Target triple: $TRIPLE"

echo "==> [1/4] Publishing C# render API (self-contained single file)"
RID="$(echo "$TRIPLE" | sed 's/aarch64-apple-darwin/osx-arm64/;s/x86_64-apple-darwin/osx-x64/')"
rm -rf "$REPO/desktop/build/api-publish"
dotnet publish "$REPO/api/LavaPlayground.Api/LavaPlayground.Api.csproj" \
  -c Release -r "$RID" \
  --self-contained true -p:PublishSingleFile=true \
  -p:RestoreConfigFile="$REPO/desktop/build/nuget.publish.config" \
  -o "$REPO/desktop/build/api-publish"
cp "$REPO/desktop/build/api-publish/LavaPlayground.Api" "$BIN_DIR/lava-api-$TRIPLE"
chmod +x "$BIN_DIR/lava-api-$TRIPLE"

echo "==> [2/4] Freezing Python linter"
PYTHON="$PYTHON" bash "$REPO/desktop/build/build-linter.sh"

echo "==> [3/4] Building Vue frontend"
cd "$REPO/frontend"
npm install
VITE_RENDER_BASE=http://127.0.0.1:5133 \
  VITE_LINT_BASE=http://127.0.0.1:8000 \
  VITE_UPDATE_REPO="${UPDATE_REPO:-YesuCS/lava-playground}" \
  VITE_BUILD_COMMIT="$(git -C "$REPO" rev-parse origin/main 2>/dev/null || git -C "$REPO" rev-parse HEAD)" \
  npm run build

echo "==> [4/4] Building Tauri app + dmg"
cd "$REPO/desktop"
npm install
npx tauri build

echo
echo "==> Done. Artifacts:"
find "$REPO/desktop/src-tauri/target/release/bundle" -name '*.dmg' -o -name '*.app' -maxdepth 3 2>/dev/null | sed 's/^/    /'
