using System;
using System.Windows;
using CrcCompanion.Core;

namespace CrcCompanion.Ui
{
    /// <summary>
    /// Copy of CRC's ERAM VIEWS Toolbar menu (live capture, Nov 21 2026).
    /// Geometry in DIPs at 100%: cell 94 x 32 (1 px border, 11 px tear-off bar, 2 px border,
    /// 79 px body, 1 px border), 2 px gaps, 2 px gold frame around the 2 x 10 grid.
    /// Only ALTIM SET and WX REPORT are wired; the rest do nothing, as in CRC.
    /// </summary>
    public sealed class ViewsSelectorWindow : EramWindow
    {
        public const string AltimKey = "ALTIM SET";
        public const string WxKey = "WX REPORT";

        private static readonly string?[,] Grid =
        {
            { "ALTIM SET", "AUTO HO INHIB", "CFR", "CODE", "CONFLCT ALERT", "CPDLC ADV", "CPDLC HIST", "CPDLC MSGOUT", "CPDLC TOC SET", "CRR" },
            { "DEPT LIST", "FLIGHT EVENT", "GROUP SUP", "HOLD LIST", "INBND LIST", "MRP LIST", "SAA FILTER", "UA", "WX REPORT", null },
        };

        /// <summary>Two-line labels exactly as CRC splits them.</summary>
        private static (string L1, string L2) Split(string label) => label switch
        {
            "ALTIM SET" => ("ALTIM", "SET"),
            "AUTO HO INHIB" => ("AUTO HO", "INHIB"),
            "CONFLCT ALERT" => ("CONFLCT", "ALERT"),
            "CPDLC ADV" => ("CPDLC", "ADV"),
            "CPDLC HIST" => ("CPDLC", "HIST"),
            "CPDLC MSGOUT" => ("CPDLC", "MSGOUT"),
            "CPDLC TOC SET" => ("CPDLC", "TOC SET"),
            "DEPT LIST" => ("DEPT", "LIST"),
            "FLIGHT EVENT" => ("FLIGHT", "EVENT"),
            "GROUP SUP" => ("GROUP", "SUP"),
            "HOLD LIST" => ("HOLD", "LIST"),
            "INBND LIST" => ("INBND", "LIST"),
            "MRP LIST" => ("MRP", "LIST"),
            "SAA FILTER" => ("SAA", "FILTER"),
            "WX REPORT" => ("WX", "REPORT"),
            _ => (label, ""),
        };

        private const double CellW = 94, CellH = 32, Gap = 2, FrameT = 2;
        private const double BarX = 1, BarW = 11, BodyX = 14, BodyW = 79, InnerY = 1, InnerH = 30;

        private readonly SelectorSettings _settings;
        private readonly Func<string, bool> _isOn;
        private readonly Action<string> _toggle;

        public event Action? Changed;

        public ViewsSelectorWindow(SelectorSettings settings, Func<string, bool> isOn, Action<string> toggle)
        {
            _settings = settings;
            _isOn = isOn;
            _toggle = toggle;
            Surface.Paint = Paint;
            Repositioned += _ =>
            {
                var (x, y) = ScreenPosition;
                _settings.X = x;
                _settings.Y = y;
                Changed?.Invoke();
            };
        }

        public void Refresh() => Surface.Refresh();

        private Size Paint(Painter p)
        {
            double gridW = FrameT * 2 + 10 * CellW + 9 * Gap;
            double gridH = FrameT * 2 + 2 * CellH + Gap;

            // VIEWS menu button, aligned with grid row 1.
            DrawCell(p, 0, FrameT, "VIEWS", "", true,
                _settings.Open ? Eram.TbMenuOpen : Eram.TbMenuClosed,
                onBody: () => { _settings.Open = !_settings.Open; Surface.Refresh(); Changed?.Invoke(); },
                onBar: BeginReposition);

            if (!_settings.Open) return new Size(CellW, gridH);

            double gx = CellW;
            p.Fill(Eram.TbFrame, gx, 0, gridW, gridH);
            p.Fill(Eram.TbGap, gx + FrameT, FrameT, gridW - FrameT * 2, gridH - FrameT * 2);

            for (int r = 0; r < 2; r++)
            {
                for (int c = 0; c < 10; c++)
                {
                    double cx = gx + FrameT + c * (CellW + Gap);
                    double cy = FrameT + r * (CellH + Gap);
                    var label = Grid[r, c];
                    if (label == null) continue; // gray filler cell (gap color shows through)

                    bool wired = label == AltimKey || label == WxKey;
                    bool on = wired && _isOn(label);
                    var (l1, l2) = Split(label);
                    bool hasBar = label != "CRR";
                    DrawCell(p, cx, cy, l1, l2, hasBar, on ? Eram.TbOn : Eram.TbOff,
                        onBody: wired ? () => _toggle(label) : (Action?)null,
                        onBar: null);
                }
            }

            return new Size(CellW + gridW, gridH);
        }

        private static void DrawCell(Painter p, double x, double y, string l1, string l2, bool hasBar,
            System.Windows.Media.Brush body, Action? onBody, Action? onBar)
        {
            double fs = Eram.ToolbarFontSize;
            p.Fill(Eram.TbBorder, x, y, CellW, CellH);
            double bx = hasBar ? x + BodyX : x + BarX;
            double bw = hasBar ? BodyW : CellW - 2;
            if (hasBar) p.Fill(Eram.TbTearoff, x + BarX, y + InnerY, BarW, InnerH);
            p.Fill(body, bx, y + InnerY, bw, InnerH);

            double half = InnerH / 2;
            p.TextCentered(l1, bx, y + InnerY, bw, fs, Eram.TbText, half);
            if (l2.Length > 0) p.TextCentered(l2, bx, y + InnerY + half, bw, fs, Eram.TbText, half);

            if (onBar != null && hasBar) p.Hit(x, y, BarX + BarW + 1, CellH, _ => onBar());
            if (onBody != null) p.Hit(bx, y, bw, CellH, _ => onBody());
        }
    }
}
