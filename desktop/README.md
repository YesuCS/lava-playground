# Lava Playground — macOS desktop app

Packages the three-service Lava Playground (Vue UI + C# render API + Python
linter) into a single native macOS `.app` you can install by dragging to
Applications and uninstall by dragging to the Trash. No Docker, no runtimes to
install — the .NET and Python backends are bundled as self-contained sidecar
binaries and launched (and shut down) automatically by the app.

## How it works

```
Lava Playground.app
├─ native window (WKWebView)  ── loads the built Vue frontend
├─ lava-api  (sidecar)        ── C# render API on 127.0.0.1:5133
└─ lava-linter (sidecar)      ── Python linter on 127.0.0.1:8000
```

The Rust shell (`src-tauri/src/lib.rs`) spawns both sidecars on launch and
kills them on quit. The frontend talks to them over localhost HTTP (the build
bakes the ports in via `VITE_RENDER_BASE` / `VITE_LINT_BASE`).

## Connecting to your Rock server

The app works the same as the web version: in the header, switch the engine
from **Local** to **Rock server…** and enter your Rock URL plus either a REST
API key or a username/password. The bundled C# API proxies rendering to your
Rock instance's `POST /api/Lava/RenderTemplate` endpoint, so templates render
with Rock's real Lava engine and live merge fields. Credentials live only in
the sidecar's memory — nothing is written to disk.

You can also auto-connect on launch by setting `ROCK_BASE_URL` (+ `ROCK_API_KEY`
or `ROCK_USERNAME`/`ROCK_PASSWORD`) in the environment before starting the app.

Notes on remote rendering:

- **Charts and script-driven shortcodes render for real.** In Rock mode the
  Rendered preview runs inside a sandboxed `<iframe>` with a `<base>` pointing
  at your Rock server, so `{[ chart ]}` output (which loads Chart.js from Rock)
  and other `<script>`-based shortcodes actually draw, and relative asset URLs
  resolve. The iframe is `sandbox="allow-scripts"` only, so it can't touch the
  app or your sidecars.
- **Use `CurrentPerson`, not `Person`.** Rock's `RenderTemplate` API renders
  without a page context, so `{{ Person.NickName }}` is empty but
  `{{ CurrentPerson.NickName }}` (the logged-in user) is populated. Entity
  commands (`{% person %}`, `{% group %}`, `{% sql %}`, `{% calendarevents %}`)
  work because they query the database directly.

## Updates (check & notify)

On launch the app asks GitHub for the latest commit on `main` and compares it
to the commit it was built from (`VITE_BUILD_COMMIT`, baked in at build time).
If the repo has moved ahead, a banner appears with a **View on GitHub** button.
Updating is manual: pull the repo and re-run `build-all.sh`. Nothing is
downloaded or installed automatically.

## Build it

Prerequisites:

- **Rust** — <https://rustup.rs>
- **.NET SDK** (8+)
- **Node** 18+
- **A stable Python** for PyInstaller. The repo default assumes
  `/opt/homebrew/bin/python3.12` (`brew install python@3.12`); override with the
  `PYTHON` env var.

Then, from the repo root:

```bash
bash desktop/build/build-all.sh
```

That publishes the C# API, freezes the Python linter, builds the frontend, and
runs `tauri build`. The result lands in:

```
desktop/src-tauri/target/release/bundle/dmg/Lava Playground_1.0.0_aarch64.dmg
desktop/src-tauri/target/release/bundle/macos/Lava Playground.app
```

### Rebuilding one piece

- C# API sidecar only: re-run the publish step in `build-all.sh`.
- Python linter sidecar only: `bash desktop/build/build-linter.sh`.
- Frontend only: `cd frontend && VITE_RENDER_BASE=http://127.0.0.1:5133 VITE_LINT_BASE=http://127.0.0.1:8000 npm run build`.
- App only (after sidecars + frontend exist): `cd desktop && npx tauri build`.

## Install

Open the `.dmg` and drag **Lava Playground** to **Applications**.

The app is **not code-signed or notarized**, so the first launch Gatekeeper will
warn "unidentified developer". To open it: **right-click the app → Open →
Open**, or run once:

```bash
xattr -dr com.apple.quarantine "/Applications/Lava Playground.app"
```

To distribute to other people without that warning you need an Apple Developer
ID certificate ($99/yr) to sign and notarize — see the "Signing" note below.

## Uninstall

Drag **Lava Playground** from Applications to the Trash. That's it. To also
clear the WebView cache it created:

```bash
rm -rf ~/Library/Caches/com.yesucs.lavaplayground \
       ~/Library/WebKit/com.yesucs.lavaplayground \
       "~/Library/Saved Application State/com.yesucs.lavaplayground.savedState"
```

## Signing & notarization (optional, for distribution)

With an Apple Developer account, set these before `tauri build` and Tauri will
sign + notarize automatically:

```bash
export APPLE_SIGNING_IDENTITY="Developer ID Application: Your Name (TEAMID)"
export APPLE_ID="you@example.com"
export APPLE_PASSWORD="app-specific-password"
export APPLE_TEAM_ID="TEAMID"
```

## Note on architecture

The build targets the machine's own architecture (Apple Silicon `aarch64` here).
For an Intel build, run the pipeline on an Intel Mac (or add cross-compilation
targets); the sidecar binaries are architecture-specific.
