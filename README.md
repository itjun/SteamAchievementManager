# Steam Achievement Manager

Steam Achievement Manager (SAM) is a lightweight, portable application used to manage achievements and statistics in the popular PC gaming platform Steam. This application requires the [Steam client](https://store.steampowered.com/about/), a Steam account and network access. Steam must be running and the user must be logged in.

This is the code for SAM. The closed-source version originally released in 2008, last major release in 2011, and last updated in 2013 (a hotfix). The code is being made available so that those interested can do as they like with it.

This fork modernized the client in three steps:

1. **WinForms → WPF** (removed): single-window WPF/.NET 8 rewrite with a self-hosted stats worker.
2. **x64 pivot**: loads Steam's own `steamclient64.dll` directly (32-bit systems are no longer supported).
3. **WinUI 3 + Fluent 2** (current): the UI is built with WinUI 3 on Windows App SDK 2.5, redesigned per the Fluent 2 design language.

## Building

Requirements: .NET SDK (8.0+ target, building with 10.x), Windows 10 1809+ x64, NuGet feed access.

```
dotnet build SAM.sln -c Debug -p:Platform=x64
```

Output: `SAM.WinUI\bin\x64\Debug\net8.0-windows10.0.22621.0\win-x64\SAM.WinUI.exe` (self-contained Windows App Runtime; `SAM.Worker.exe` is deployed alongside).

## Solution layout

| Project | Purpose |
| --- | --- |
| `SAM.API` | Steam `steamclient64.dll` ThisCall/v-table interop (from the original codebase) |
| `SAM.Core` | UI-agnostic business logic: game list, stats worker protocol, family-sharing probes, installed-game scan |
| `SAM.Worker` | Hidden subprocess host (`--stats-worker=<appId>`, `--probe-appctx=<appId>`) — Steam's `ISteamUserStats` binds to the process-level AppId context |
| `SAM.WinUI` | WinUI 3 desktop client (Fluent 2) |

Debug arguments: `--theme=dark|light|system`, `--open-game=<appId>`.

See `docs/MIGRATION.md` (Chinese) for the full migration history, architecture notes, and pitfalls.

## Releases & auto-update (portable)

Releases are published to GitHub Releases via [Velopack](https://velopack.io) (`.github/workflows/release.yml`) as a **portable zip** — no installer. Download the zip, extract it to any writable folder, and run `Steam Achievement Manager.exe`. The app checks for updates on startup (toggle in 设置) and can also check manually; updates download with a progress dialog and apply **in place**, then the app restarts into the new version. Nothing is written outside the extracted folder (settings and caches still live in `%LOCALAPPDATA%\SAM`).

- **Release a version**: `python scripts/release.py release X.Y.Z` — bumps the version (and optionally `--force` to make it a required update, `--prerelease` to rehearse without clients seeing it), commits, tags, and pushes to trigger CI. Add `--dry-run` to preview. The manual equivalent: bump `<Version>` in `SAM.WinUI/SAM.WinUI.csproj`, optionally set `minimumRequired` in `update-policy.json`, then `git tag vX.Y.Z && git push origin vX.Y.Z`.
- **Pack locally** (no GitHub needed): `python scripts/release.py pack` — runs `dotnet publish` + `vpk pack` into `Releases/` and generates `update-manifest.json` for offline update testing.
- **Forced vs optional updates**: a release is optional by default. If `minimumRequired` in `update-policy.json` (synced automatically with `release --force`) is above the user's version, the client shows a non-dismissable update dialog — they must update or exit.
- **Rehearse a release**: run the workflow manually with *prerelease* checked — prereleases are invisible to clients and can be deleted and re-published.
- No code-signing certificate is configured, so the first run of a freshly downloaded exe may trigger SmartScreen ("More info → Run anyway"). Updates afterwards are incremental (a few MB) and applied in place.
- For self-update to work, the portable folder must be writable (don't extract into `Program Files`).

Update source override for testing: set `SAM_UPDATE_SOURCE` to a local `vpk pack` output directory and the app checks that directory instead of GitHub.

## Attribution

Most (if not all) icons are from the [Fugue Icons](https://p.yusukekamiyamane.com/) set.
