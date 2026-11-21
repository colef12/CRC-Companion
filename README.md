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
| INBND LIST, IN SECTOR and DEPT LIST strip views | ✅ Live from VATSIM data |

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

## Strip views (INBND LIST, IN SECTOR, DEPT LIST)

Set your **VATSIM CID** from the tray menu. While you're logged on, the app finds your position in VATSIM's live data, works out the ARTCC from the callsign (`DC_`/`WAS_` → ZDC), and can switch to that facility's profile automatically. When you're not connected, a profile named after a facility (e.g. `ZDC`) uses that facility.

| View | Shows |
|---|---|
| INBND LIST | Flights outside the ARTCC projected to enter within 20 minutes (M → MIN), sorted by entry time |
| IN SECTOR | Airborne flights inside the ARTCC. Flights working another local controller are hidden unless M → OTHERS is on. |
| DEPT LIST | Flights on the ground at an ARTCC airport with a flight plan, plus prefiles, grouped by airport |

Each strip is one line: `mark callsign type time/destination altitude beacon route`. The route width is set by M → ROUTE. The mark shows whose frequency the aircraft is tuned to (from VATSIM's voice data):

| Mark | Meaning |
|---|---|
| `*` | Your frequency |
| `A` / `T` / `G` | A local approach, tower or ground/delivery frequency |
| `C` | Another Center sector in your ARTCC |
| `?` | In your airspace on nobody's frequency (only while you're connected) |
| `P` | Prefiled, not connected yet |

ARTCC boundaries, callsign prefixes and airports come from VATSIM's `vatspy-data-project`, refreshed weekly. Traffic refreshes every 15 seconds.

## Weather data

By default the app shows **real-world METARs** from aviationweather.gov, fetched for all stations in one request. VATSIM's METAR service (`metar.vatsim.net`) fills in any station aviationweather.gov doesn't return. Use the tray menu's **Weather source** to make VATSIM the primary source instead.

Reports refresh every 2 minutes. A newly added station, or a change of source, is fetched right away. If a refresh fails, the last good report stays on screen.

## Building

The project is .NET 8 WPF. GitHub Actions (`.github/workflows/build.yml`) builds it on Windows and publishes the .exe. The platform-neutral logic in `src/CrcCompanion/Core` has a dependency-free test runner:

```
dotnet run --project tests/CoreTests
```
