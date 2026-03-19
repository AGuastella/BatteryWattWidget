using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text.Json;

namespace BatteryWattWidget
{
    public class ColorThreshold
    {
        public double BelowWatts { get; set; }
        public Color Color { get; set; }
    }

    public class WidgetConfig
    {
        // Display
        public string FontPreset { get; set; } = "standard";
        public float FontSizeShort { get; set; } = 40f;
        public float FontSizeMedium { get; set; } = 32f;
        public float FontSizeLong { get; set; } = 24f;
        public string FontFamily { get; set; } = "Segoe UI";
        public int IconSize { get; set; } = 64;

        // Colors
        public Color ColorDefault { get; set; } = Color.White;
        public bool ColorCodingEnabled { get; set; } = true;
        public List<ColorThreshold> ColorThresholds { get; set; } = new()
        {
            new() { BelowWatts = 8, Color = Color.FromArgb(0, 200, 83) },
            new() { BelowWatts = 15, Color = Color.FromArgb(255, 200, 0) },
            new() { BelowWatts = 25, Color = Color.FromArgb(255, 140, 0) },
            new() { BelowWatts = 999, Color = Color.FromArgb(255, 60, 60) },
        };
        public Color ColorAc { get; set; } = Color.FromArgb(0, 200, 83);

        // Polling
        public int PollIntervalMs { get; set; } = 2000;

        // Battery
        public double BatteryCapacityWh { get; set; } = 76.0;

        /// <summary>
        /// Returns the font size for a given text length, respecting the preset.
        /// </summary>
        public float GetFontSize(int textLength)
        {
            float shortSize, mediumSize, longSize;

            switch (FontPreset.ToLowerInvariant())
            {
                case "big":
                    shortSize = 48f;
                    mediumSize = 48f;
                    longSize = 48f;
                    break;
                case "custom":
                    shortSize = FontSizeShort;
                    mediumSize = FontSizeMedium;
                    longSize = FontSizeLong;
                    break;
                case "standard":
                default:
                    shortSize = 40f;
                    mediumSize = 32f;
                    longSize = 24f;
                    break;
            }

            if (textLength <= 2) return shortSize;
            if (textLength <= 3) return mediumSize;
            return longSize;
        }

        /// <summary>
        /// Returns the color for a given watt value, using thresholds if enabled.
        /// </summary>
        public Color GetWattColor(double watts)
        {
            if (!ColorCodingEnabled)
                return ColorDefault;

            foreach (var threshold in ColorThresholds)
            {
                if (watts < threshold.BelowWatts)
                    return threshold.Color;
            }

            return ColorDefault;
        }

        /// <summary>
        /// Loads config from config.json next to the executable.
        /// Returns defaults if the file is missing or malformed.
        /// </summary>
        public static WidgetConfig Load()
        {
            var config = new WidgetConfig();

            try
            {
                // Look for config.json next to the executable
                string exeDir = AppDomain.CurrentDomain.BaseDirectory;
                string configPath = Path.Combine(exeDir, "config.json");

                if (!File.Exists(configPath))
                    return config;

                string json = File.ReadAllText(configPath);

                // Strip single-line comments (// ...) for lenient JSON parsing
                var lines = json.Split('\n');
                var cleanLines = new List<string>();
                foreach (var line in lines)
                {
                    string trimmed = line.TrimStart();
                    if (!trimmed.StartsWith("//"))
                        cleanLines.Add(line);
                }
                json = string.Join('\n', cleanLines);

                using var doc = JsonDocument.Parse(json, new JsonDocumentOptions
                {
                    CommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true
                });

                var root = doc.RootElement;

                // Display
                if (root.TryGetProperty("font_preset", out var fp))
                    config.FontPreset = fp.GetString() ?? "standard";
                if (root.TryGetProperty("font_size_short", out var fss))
                    config.FontSizeShort = (float)fss.GetDouble();
                if (root.TryGetProperty("font_size_medium", out var fsm))
                    config.FontSizeMedium = (float)fsm.GetDouble();
                if (root.TryGetProperty("font_size_long", out var fsl))
                    config.FontSizeLong = (float)fsl.GetDouble();
                if (root.TryGetProperty("font_family", out var ff))
                    config.FontFamily = ff.GetString() ?? "Segoe UI";
                if (root.TryGetProperty("icon_size", out var ics))
                    config.IconSize = ics.GetInt32();

                // Colors
                if (root.TryGetProperty("color_default", out var cd))
                    config.ColorDefault = ParseHexColor(cd.GetString(), config.ColorDefault);
                if (root.TryGetProperty("color_coding_enabled", out var cce))
                    config.ColorCodingEnabled = cce.GetBoolean();
                if (root.TryGetProperty("color_ac", out var ca))
                    config.ColorAc = ParseHexColor(ca.GetString(), config.ColorAc);

                if (root.TryGetProperty("color_thresholds", out var ct) && ct.ValueKind == JsonValueKind.Array)
                {
                    var thresholds = new List<ColorThreshold>();
                    foreach (var item in ct.EnumerateArray())
                    {
                        double belowWatts = 999;
                        Color color = Color.White;

                        if (item.TryGetProperty("below_watts", out var bw))
                            belowWatts = bw.GetDouble();
                        if (item.TryGetProperty("color", out var c))
                            color = ParseHexColor(c.GetString(), Color.White);

                        thresholds.Add(new ColorThreshold { BelowWatts = belowWatts, Color = color });
                    }
                    if (thresholds.Count > 0)
                        config.ColorThresholds = thresholds;
                }

                // Polling
                if (root.TryGetProperty("poll_interval_ms", out var pi))
                    config.PollIntervalMs = Math.Max(500, pi.GetInt32()); // minimum 500ms

                // Battery
                if (root.TryGetProperty("battery_capacity_wh", out var bc))
                    config.BatteryCapacityWh = bc.GetDouble();
            }
            catch
            {
                // Any parse error → return defaults
            }

            return config;
        }

        private static Color ParseHexColor(string? hex, Color fallback)
        {
            if (string.IsNullOrWhiteSpace(hex))
                return fallback;

            try
            {
                hex = hex.TrimStart('#');
                if (hex.Length == 6)
                {
                    int r = Convert.ToInt32(hex.Substring(0, 2), 16);
                    int g = Convert.ToInt32(hex.Substring(2, 2), 16);
                    int b = Convert.ToInt32(hex.Substring(4, 2), 16);
                    return Color.FromArgb(r, g, b);
                }
            }
            catch { }

            return fallback;
        }
    }
}
