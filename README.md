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

## Attribution

Most (if not all) icons are from the [Fugue Icons](https://p.yusukekamiyamane.com/) set.
