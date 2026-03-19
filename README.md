# BatteryWattWidget

System tray widget for Windows that displays real-time battery discharge rate in watts.

Reads from the same WMI data source as [G-Helper](https://github.com/seerge/g-helper), so values match. Built for the ASUS ROG Zephyrus G14 (2021) but works on any Windows laptop.

## Quick Start

```powershell
# Run directly
dotnet run

# Or publish a standalone .exe
dotnet publish -c Release
# Output: bin\Release\net10.0-windows\win-x64\publish\BatteryWattWidget.exe
```

Requires [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (or change `TargetFramework` in `.csproj` to match your version).

## What It Shows

The tray icon displays the discharge rate as text, color-coded by load:

| Icon | Color | Meaning |
|------|-------|---------|
| `7.2` | Green | < 8W — efficient idle |
| `12` | Yellow | 8–15W — moderate load |
| `22` | Orange | 15–25W — heavy load |
| `35` | Red | 25W+ — intense |
| `AC` | Green | On AC power |

Hover for the exact reading. Double-click for a balloon notification.

## Configuration

All settings live in `config.json`, placed next to the `.exe`. Edit and restart the widget.

### Font Presets

Three presets control text sizing on the tray icon:

| Preset | Short (≤2 chars) | Medium (3 chars) | Long (4+ chars) | Notes |
|--------|-------------------|-------------------|-------------------|-------|
| `standard` | 40px | 32px | 24px | Default — scaled by character count |
| `big` | 48px | 48px | 48px | Uniform large text, may clip on long values |
| `custom` | `font_size_short` | `font_size_medium` | `font_size_long` | Set your own values |

To switch, change `font_preset` in `config.json`:

```jsonc
"font_preset": "big"
```

Or go fully custom:

```jsonc
"font_preset": "custom",
"font_size_short": 44,
"font_size_medium": 36,
"font_size_long": 28
```

### All Settings

```jsonc
{
  // ─── Display ───
  "font_preset": "standard",       // "standard", "big", or "custom"
  "font_size_short": 40,           // custom preset: 1-2 char text (e.g. "AC")
  "font_size_medium": 32,          // custom preset: 3 char text (e.g. "7.2")
  "font_size_long": 24,            // custom preset: 4+ char text (e.g. "12.3")
  "font_family": "Segoe UI",       // any installed font
  "icon_size": 64,                 // render canvas (px), downscaled by Windows

  // ─── Colors (hex RGB) ───
  "color_default": "#FFFFFF",       // fallback text color
  "color_coding_enabled": true,     // color by watt thresholds?
  "color_thresholds": [
    { "below_watts":  8, "color": "#00C853" },
    { "below_watts": 15, "color": "#FFC800" },
    { "below_watts": 25, "color": "#FF8C00" },
    { "below_watts": 999, "color": "#FF3C3C" }
  ],
  "color_ac": "#00C853",           // color when on AC

  // ─── Polling ───
  "poll_interval_ms": 2000,        // update interval (min 500ms)

  // ─── Battery ───
  "battery_capacity_wh": 76.0      // only used for Win32_Battery fallback
}
```

### Right-Click Menu

| Option | Action |
|--------|--------|
| Refresh Now | Force immediate reading |
| Reload Config | Apply polling changes without restart |
| Open Config File | Opens `config.json` in your default editor |
| Exit | Close the widget |

## Auto-Start

`Win + R` → `shell:startup` → drop a shortcut to the `.exe`.

## How It Works

Battery data is read via WMI every N milliseconds (configurable):

1. **Primary:** `BatteryStatus` from `root\WMI` — `DischargeRate` in milliwatts. Same query G-Helper uses.
2. **Fallback:** Kernel `IOCTL_BATTERY_STATUS` via `DeviceIoControl`.
3. **Last resort:** `Win32_Battery` — estimates watts from runtime and charge percentage.

## Troubleshooting

**Icon shows "AC" while on battery** — run as Administrator, or verify in PowerShell:
```powershell
Get-CimInstance -Query "SELECT * FROM BatteryStatus" -Namespace "root\WMI"
```

**Icon hidden in tray overflow** — right-click taskbar → Taskbar settings → Other system tray icons → toggle BatteryWattWidget on.

**Config changes not applying** — font/size changes require a restart. Polling interval can be reloaded via right-click → Reload Config.

## License

MIT
