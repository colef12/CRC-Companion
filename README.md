# CRC Companion

A Windows app that runs next to VATSIM's CRC and draws ERAM views in their own movable windows. That lets them sit outside CRC's radar display, for example in the side areas of a 43" 4K R-side layout, while looking like CRC's own views.

## Download

Get **CRCCompanion.exe** from the [latest release](../../releases/tag/latest). It's a single file with nothing to install. A new build is published automatically on every push to `main`.

## What's in this version

| Item | Status |
|---|---|
| VIEWS selector, a copy of CRC's ERAM VIEWS menu | ✅ ALTIM SET and WX REPORT work. The other buttons do nothing, as in CRC. |
| Altimeter Settings view | ✅ |
| Weather Station Report view | ✅ |
| Per-facility profiles | ✅ |

In CRC, turn off its own ALTIM SET and WX REPORT views so they aren't shown twice.

## Using it

| Action | How |
|---|---|
| Show or hide a view | Click **ALTIM SET** or **WX REPORT** in the VIEWS selector, or click a view's **-** |
| Open or close the selector grid | Click the **VIEWS** button |
| Move the selector | Click the olive bar on the VIEWS button, move the mouse, click again to place it |
| Move a view or menu | Click its title, move the mouse, click again to place it. Esc or a right click cancels. |
| View menu | Click **M** |
| Change a number (LINES, COL, FONT, BRIGHT) | Middle click raises it, left click lowers it, as in CRC. The mouse wheel works too. |
| Add or remove stations | **M → STATIONS**, then type `IAD DCA BWI RIC` and press Enter. `-RIC` removes. `KIAD` and `IAD` both work. Esc closes. |
| Delete one station | Click its line, then click DELETE |
| Scroll the WX view | Arrows, the track, or the mouse wheel |

Clicking a view never takes the keyboard away from CRC. The station entry box takes the keyboard only while it's open, then hands it back to CRC.

### Display rules (same as ERAM)

| Field | Rule |
|---|---|
| Altimeter | First digit and decimal point removed: 29.92 → `992`, 30.12 → `012`. Underlined when below 29.92. |
| Observation time | Underlined when the report is more than 65 minutes old |
| `-M-` | Shown when the report is missing or more than 120 minutes old |
| WX `>` | The report continues below the visible lines |

## Profiles

Right-click the tray icon to switch, create, duplicate, rename or delete profiles. A profile saves everything: station lists, view positions, which views are shown, every menu setting and the selector position. Changes save automatically.

Profiles are stored in `%APPDATA%\CRCCompanion\profiles\NAME.json`. The tray menu's **Open profiles folder** opens it, so profiles can be backed up or shared.

The tray menu also has **Size**, an extra scale on top of Windows display scaling so the views can match CRC's size exactly, and **Reset view positions**.

## Weather data

METARs come from VATSIM's METAR service (`metar.vatsim.net`), with aviationweather.gov as a fallback. They refresh every 2 minutes, and a newly added station is fetched right away.

## Building

The project is .NET 8 WPF. GitHub Actions (`.github/workflows/build.yml`) builds it on Windows and publishes the .exe. The platform-neutral logic in `src/CrcCompanion/Core` has a dependency-free test runner:

```
dotnet run --project tests/CoreTests
```
