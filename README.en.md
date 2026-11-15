<div align="center">

# Facet

**A full-screen Windows overlay, one shortcut away**<br>
Pick only the widgets and skins you want and make the screen your own.

[한국어](README.md) · **English**

[![YouTube](https://img.shields.io/badge/YouTube-Material%20Stone-FF0000?logo=youtube&logoColor=white)](https://www.youtube.com/@MaterialStone)
[![Release](https://img.shields.io/github/v/release/material-stone/facet?label=download)](https://github.com/material-stone/facet/releases/latest)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

</div>

> The app's interface is currently in Korean only.

## What Facet does

- Press a shortcut and an overlay opens on top of whatever you are doing. Press it again and it is gone.
- The overlay has tabs, and each tab is a board. Place widgets on it, move them and resize them.
- Widgets come as **plugins**. Plain Facet starts light and empty; install only what you use.
- **Skins** change the whole look. A calm dark default skin is built in, and more are a download away.
- **Quick Launch** starts a program as soon as you type its name.
- Your records stay on your PC, and you can back them up and restore them from Settings at any time.

> A facet is one cut face of a gem. Widgets and skins come together like facets to make one screen.

## Install

You need a 64-bit PC with Windows 10 (version 2004 or later) or Windows 11. Nothing else, not even .NET, has to be installed.

### Installer (recommended)

1. Download `Facet-<version>-setup-x64.exe` from the [latest release](https://github.com/material-stone/facet/releases/latest).
2. Run it. No administrator rights are needed.
3. Start **Facet** from the Start menu.

> The files are not code-signed yet, so Windows SmartScreen may warn you. Choose **More info → Run anyway**.

### Portable (no install)

Download `Facet-<version>-win-x64.zip` from the [latest release](https://github.com/material-stone/facet/releases/latest), unzip it anywhere and run `Facet.Launcher.exe`.

### Uninstall

Remove Facet from **Windows Settings → Apps → Installed apps**. Your records stay on the PC, so a later install picks up where you left off.

## Using Facet

| Default shortcut | Action |
| --- | --- |
| `Win` + `Alt` + `O` | Show or hide the overlay |
| `Win` + `Alt` + `Space` | Quick Launch |
| `Win` + `Alt` + `End` | Quit Facet |
| `Esc` | Hide the overlay |

All three shortcuts can be changed under **Settings → Shortcuts**. Double-clicking the Facet tray icon also opens the overlay, and **Settings → App start** can start Facet when you sign in to Windows.

### Quick Launch

Type a program name and Quick Launch suggests matching programs on your PATH. Pick one with the arrow keys and press `Enter`. Try `notepad` or `calc`.

## Plugins

Widgets are installed as plugins. 👉 **[Browse plugins](registry/plugins/README.md)** (Korean)

1. Open the **Settings** tab in the overlay.
2. Under **Plugins → Get plugins**, click **Install** on a plugin. It turns on right away.
3. Click a tab's widget edit button and place the plugin's widgets from **Add widget**.
4. To remove one, click the trash button under **Plugins → Installed plugins**.
   - **Delete** removes the plugin and keeps its records, so reinstalling continues where you left off.
   - **Delete with data** also removes its records and settings.

> ⚠️ Plugins run with the same rights as Facet. Only install plugins you trust.

To write your own, see the [plugin guide](docs/plugins/README.md) and the [upload guide](registry/plugins/UPLOAD.md) (both Korean).

## Skins

A dark default skin is built in. 👉 **[Browse skins](registry/skins/README.md)** (Korean)

1. Under **Settings → Skins → Get skins**, click **Install** on a skin. It is applied right away.
2. To switch, pick another one under **Settings → Skins → Skin**.

To make your own, see the [skin upload guide](registry/skins/UPLOAD.md) (Korean).

## Your records and backups

- Records are stored in `%LOCALAPPDATA%\MaterialStone\Facet` and encrypted with your Windows account. Other users and other PCs cannot read them.
- **Settings → Data → Backup → Back up now** saves a zip file to `Documents\Facet 백업`.
- **Settings → Data → Restore** lets you pick a backup; Facet restores it the next time it starts and keeps the replaced records in a folder beside them. A backup restores only for the same Windows account.
- Facet has no accounts and no server, and it sends no usage data anywhere. It goes online only to load the plugin and skin lists, and when a plugin needs it (for example, weather).

## Made by

**Material Stone** builds Facet. Follow development on YouTube.

▶ **YouTube: [Material Stone](https://www.youtube.com/@MaterialStone)**

Report bugs or share ideas in [Issues](https://github.com/material-stone/facet/issues).

## License

Facet is under the [MIT License](LICENSE). The bundled [Pretendard](impls/Facet/Design/Fonts/Pretendard-LICENSE.txt) font is under the SIL Open Font License. Fonts inside skins follow the license in each skin's folder.
