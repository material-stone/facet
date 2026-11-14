<div align="center">

# Facet

**A full-screen Windows overlay, one shortcut away**<br>
Keep your clock, weather, schedule, to-dos and reminders on one screen, and add more with plugins.

[한국어](README.md) · **English**

[![YouTube](https://img.shields.io/badge/YouTube-Material%20Stone-FF0000?logo=youtube&logoColor=white)](https://www.youtube.com/@MaterialStone)
[![Release](https://img.shields.io/github/v/release/material-stone/facet?label=download)](https://github.com/material-stone/facet/releases/latest)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

</div>

> The app's interface is currently in Korean only.

## What Facet does

- Press **`Win` + `Alt` + `O`** and an overlay opens on top of whatever you are doing. Press it again or hit `Esc` to hide it.
- The dashboard shows your **clock, weather and today's schedule** at a glance. Widgets sit on a grid where you can move and resize them.
- The **calendar** keeps schedules, to-dos and Markdown notes per day, and **reminders** pop up at the time you set.
- **`Win` + `Alt` + `Space`** opens **Quick Launch** to start apps, open folders or search the web.
- **Skins** change the whole look. Space, Voxel and Midnight are built in.
- Plain Facet stays light. Everything else comes as **plugins** you choose to install.

> A facet is one cut face of a gem. Widgets and plugins come together like facets to make your screen.

## Install

You need a 64-bit PC with Windows 10 (version 2004 or later) or Windows 11. Nothing else, not even .NET, has to be installed.

### Option 1. Installer (recommended)

1. Download `Facet-<version>-setup-x64.exe` from the [latest release](https://github.com/material-stone/facet/releases/latest).
2. Run it. No administrator rights are needed.
3. Start **Facet** from the Start menu.

> The files are not code-signed yet, so Windows SmartScreen may warn you. Choose **More info → Run anyway**.

### Option 2. One command (winget)

```powershell
winget install MaterialStone.Facet
```

This works once Microsoft has reviewed the package. Until then, use option 1.

### Option 3. Portable (no install)

Download `Facet-<version>-win-x64.zip` from the [latest release](https://github.com/material-stone/facet/releases/latest), unzip it anywhere and run `Facet.Launcher.exe`.

### Uninstall

Remove Facet from **Settings → Apps → Installed apps**. Your records stay on the PC, so a later install picks up where you left off.

## Shortcuts

| Shortcut | Action |
| --- | --- |
| `Win` + `Alt` + `O` | Show or hide the overlay (configurable) |
| `Win` + `Alt` + `Space` | Quick Launch |
| `Esc` | Hide the overlay |
| `Win` + `Alt` + `End` | Quit Facet |

Double-clicking the Facet tray icon also opens the overlay. To start Facet when you sign in to Windows, turn it on under **Settings → App start**.

## Plugins

### Install and remove

1. Open the **Settings** tab in the overlay.
2. Under **Plugins → Get skins and plugins**, click **Apply** on a plugin. Facet downloads it and turns it on. You can also install from a zip file.
3. Place the plugin's widgets from **Add widget** in the dashboard's edit mode.
4. To remove one, click the trash button next to it under **Plugins → Plugin setup**.
   - **Delete** removes the plugin and keeps its records, so reinstalling continues where you left off.
   - **Delete with data** also removes its records and settings.

The list lives in this repository's [`registry/`](registry/README.md) folder, and the files are published as this repository's releases. Facet installs a file only when its SHA-256 matches the list.

> ⚠️ Plugins run with the same rights as Facet. Only turn on plugins you trust.

### Write your own

A plugin is a .NET 10 WPF class library that references only `Facet.Plugins`. See the [plugin guide](docs/plugins/README.md) (Korean) and the sample in [`impls/Facet.SamplePlugin`](impls/Facet.SamplePlugin). Submit it to the registry with the [plugin upload form](registry/docs/plugin.md).

## Use it with AI agents (optional)

Facet ships an MCP server (`Facet.Mcp.exe`). Register it with an agent such as Codex or Claude Code and you can say things like "add a meeting tomorrow at 3 pm". The built-in tools are `facet_today`, `facet_list_schedules`, `facet_add_schedule`, `facet_delete_schedule`, `facet_add_reminder` and `facet_weather`. Enabled plugins add their own tools.

## Your data and privacy

- Records are stored in `%LOCALAPPDATA%\MaterialStone\DailyDock` and encrypted with your Windows account. Other users and other PCs cannot read them.
- Facet has no accounts and no server, and it sends no usage data anywhere.
- It goes online only to fetch the weather and to load the plugin list.

## Build from source

You need Windows 10 or later, the [.NET SDK 10.0.302 or later](https://dotnet.microsoft.com/download/dotnet/10.0) and Node.js (for checks).

```powershell
git clone https://github.com/material-stone/facet.git
cd facet
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/check.ps1     # checks
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/build.ps1     # local build -> artifacts/Facet
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/package.ps1   # portable zip -> artifacts/portable
```

`Start-Facet.cmd` opens the build. Design notes and decision records are in [docs](docs/README.md) (Korean).

| Folder | Contents |
| --- | --- |
| `impls/Facet` | WPF app (overlay, dashboard, calendar, settings, plugin host) |
| `impls/Facet.Core` | Schedules, reminders and stored data |
| `impls/Facet.Plugins` | Plugin contract |
| `impls/Facet.Launcher` | Fixed launcher that opens the latest version |
| `impls/Facet.Cli`, `impls/Facet.Mcp` | Reminder CLI and the MCP server for agents |
| `installer/` | Installer (Inno Setup) |
| `registry/` | Plugin and skin registry |

To release, raise `<Version>` in `impls/Facet/Facet.csproj` and push a `v<version>` tag. GitHub Actions runs the checks, builds the packages, test-installs them and publishes the release.

## Made by

**Material Stone** builds Facet. Follow development on YouTube.

▶ **YouTube: [Material Stone](https://www.youtube.com/@MaterialStone)**

Report bugs or share ideas in [Issues](https://github.com/material-stone/facet/issues).

## License

Facet's code is under the [MIT License](LICENSE). The bundled fonts are under their own licenses (SIL Open Font License).

- [Pretendard](impls/Facet/Design/Fonts/Pretendard-LICENSE.txt)
- [Gowun Dodum](impls/Facet/Design/Fonts/GowunDodum-LICENSE.txt)
- [Jua](impls/Facet/Design/Fonts/Jua-LICENSE.txt)
