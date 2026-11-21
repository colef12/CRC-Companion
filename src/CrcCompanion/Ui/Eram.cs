using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace CrcCompanion.Ui
{
    /// <summary>
    /// ERAM colors and text metrics. Hex values come from CRC captures
    /// (see CRC-ERAM-Views-Inventory.md in the project docs).
    /// </summary>
    public static class Eram
    {
        // View chrome
        public static readonly Brush Black = Solid(0x00, 0x00, 0x00);
        public static readonly Brush HalfBlack = Frozen(new SolidColorBrush(Color.FromArgb(0x80, 0, 0, 0)));
        public static readonly Brush ViewBorder = Solid(0x83, 0x83, 0x83);
        public static readonly Brush Marker = Solid(0xA3, 0xA7, 0x09);
        public static readonly Brush ScrollGray = Solid(0x91, 0x91, 0x91);

        // Menus
        public static readonly Brush MenuTitle = Solid(0x99, 0x99, 0x99);
        public static readonly Brush MenuGreen = Solid(0x00, 0x9D, 0x00);
        public static readonly Brush MenuOn = Solid(0x99, 0x99, 0x99);
        public static readonly Brush MenuText = Solid(0xD0, 0xD0, 0xD0);
        public static readonly Brush MenuLine = Solid(0x83, 0x83, 0x83);

        // Toolbar / VIEWS selector (live capture)
        public static readonly Brush TbFrame = Solid(0x63, 0x4B, 0x00);
        public static readonly Brush TbBorder = Solid(0x5E, 0x5E, 0x5E);
        public static readonly Brush TbGap = Solid(0x61, 0x61, 0x61);
        public static readonly Brush TbTearoff = Solid(0x7C, 0x7C, 0x4E);
        public static readonly Brush TbOff = Solid(0x00, 0x00, 0x00);
        public static readonly Brush TbOn = Solid(0x61, 0x61, 0x61);
        public static readonly Brush TbText = Solid(0xD5, 0xD5, 0xD5);
        public static readonly Brush TbMenuOpen = Solid(0x6B, 0x4E, 0x4B);
        public static readonly Brush TbMenuClosed = Solid(0x00, 0x00, 0x75);

        public static readonly FontFamily Family = new FontFamily("Consolas");
        public static readonly Typeface Face = new Typeface(Family, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

        /// <summary>Text size in DIPs for ERAM FONT levels 1-5.</summary>
        public static double FontSize(int level) => level switch
        {
            1 => 11, 2 => 13, 3 => 15, 4 => 17, _ => level >= 5 ? 19 : 11,
        };

        public const double MenuFontSize = 13;
        public const double ToolbarFontSize = 12;

        private static readonly Dictionary<int, Brush> GrayCache = new Dictionary<int, Brush>();

        /// <summary>Text gray for a BRIGHT level (80 -> #BBBBBB, as in CRC's default).</summary>
        public static Brush TextGray(int bright)
        {
            bright = Math.Clamp(bright, 0, 100);
            if (GrayCache.TryGetValue(bright, out var b)) return b;
            var v = (byte)Math.Clamp(Math.Round(234.0 * bright / 100.0), 0, 255);
            b = Solid(v, v, v);
            GrayCache[bright] = b;
            return b;
        }

        public static FormattedText Format(string text, double size, Brush brush, double ppd) =>
            new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, size, brush, ppd);

        private static readonly Dictionary<(double, double), double> CharWidthCache = new Dictionary<(double, double), double>();

        public static double CharWidth(double size, double ppd)
        {
            if (CharWidthCache.TryGetValue((size, ppd), out var w)) return w;
            w = Format("MMMMMMMMMM", size, Black, ppd).WidthIncludingTrailingWhitespace / 10.0;
            CharWidthCache[(size, ppd)] = w;
            return w;
        }

        public static double LineHeight(double size) => Math.Ceiling(size * 1.3);

        private static Brush Solid(byte r, byte g, byte b) => Frozen(new SolidColorBrush(Color.FromRgb(r, g, b)));
        private static Brush Frozen(SolidColorBrush b) { b.Freeze(); return b; }
    }
}
