using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace CrcCompanion.Ui
{
    public abstract class MenuRow
    {
        public abstract string Label { get; }
    }

    /// <summary>The O/T row: shows the current mode (O = opaque, T = semi-transparent).</summary>
    public sealed class ModeRow : MenuRow
    {
        private readonly Func<bool> _opaque;
        public Action Toggle { get; }
        public ModeRow(Func<bool> opaque, Action toggle) { _opaque = opaque; Toggle = toggle; }
        public override string Label => _opaque() ? "O" : "T";
    }

    public sealed class ToggleRow : MenuRow
    {
        private readonly string _label;
        public Func<bool> IsOn { get; }
        public Action Toggle { get; }
        public ToggleRow(string label, Func<bool> isOn, Action toggle) { _label = label; IsOn = isOn; Toggle = toggle; }
        public override string Label => _label;
    }

    /// <summary>Numeric row. As in CRC: middle click increments, left click decrements. Wheel also works.</summary>
    public sealed class StepRow : MenuRow
    {
        private readonly string _label;
        public Func<int> Get { get; }
        public Action<int> Set { get; }
        public int Min { get; }
        public int Max { get; }
        public int Step { get; }
        public StepRow(string label, Func<int> get, Action<int> set, int min, int max, int step = 1)
        { _label = label; Get = get; Set = set; Min = min; Max = max; Step = step; }
        public override string Label => _label + " " + Get();
        public void Nudge(int dir) => Set(Math.Clamp(Get() + dir * Step, Min, Max));
    }

    public sealed class CommandRow : MenuRow
    {
        private readonly string _label;
        public Action<MouseButton>? Run { get; }
        public CommandRow(string label, Action<MouseButton>? run) { _label = label; Run = run; }
        public override string Label => _label;
    }

    /// <summary>ERAM view menu: gray title row with X, then green/gray/black rows.</summary>
    public sealed class MenuWindow : EramWindow
    {
        private readonly string _title;
        private readonly IReadOnlyList<MenuRow> _rows;
        private const int MinChars = 12;
        private const double Pad = 3;

        public MenuWindow(string title, IReadOnlyList<MenuRow> rows)
        {
            _title = title;
            _rows = rows;
            Surface.Paint = Paint;
            Surface.Wheel = OnWheel;
        }

        public void Refresh() => Surface.Refresh();

        private double RowH(Painter p) => p.LineH(Eram.MenuFontSize) + 1;

        private Size Paint(Painter p)
        {
            double fs = Eram.MenuFontSize, cw = p.CharW(fs), rh = RowH(p);
            int chars = Math.Max(MinChars, _rows.Select(r => r.Label.Length).DefaultIfEmpty(0).Max() + 1);
            chars = Math.Max(chars, _title.Length + 4);
            double xW = cw + 6;
            double w = 2 + Pad + chars * cw + Pad + 2;
            double y = 2;

            // Title row
            p.Fill(Eram.MenuTitle, 2, y, w - 4, rh);
            p.TextCentered(_title, 2, y, w - 4 - xW, fs, Eram.MenuText, rh);
            p.Fill(Eram.MenuLine, w - 2 - xW - 1, y, 1, rh);
            p.TextCentered("X", w - 2 - xW, y, xW, fs, Eram.MenuText, rh);
            p.Hit(w - 2 - xW, y, xW, rh, _ => Close());
            p.Hit(2, y, w - 4 - xW - 1, rh, _ => BeginReposition());
            y += rh;

            foreach (var row in _rows)
            {
                p.Fill(Eram.MenuLine, 2, y, w - 4, 1);
                y += 1;
                Brush bg = Eram.Black;
                bool centered = false;
                switch (row)
                {
                    case ModeRow m:
                        centered = true;
                        p.Hit(2, y, w - 4, rh, _ => { m.Toggle(); Refresh(); });
                        break;
                    case ToggleRow t:
                        bg = t.IsOn() ? Eram.MenuOn : Eram.Black;
                        p.Hit(2, y, w - 4, rh, _ => { t.Toggle(); Refresh(); });
                        break;
                    case StepRow s:
                        bg = Eram.MenuGreen;
                        p.Hit(2, y, w - 4, rh, b => { s.Nudge(b == MouseButton.Middle ? +1 : -1); Refresh(); });
                        break;
                    case CommandRow c:
                        var run = c.Run;
                        if (run != null) p.Hit(2, y, w - 4, rh, b => run(b));
                        break;
                }
                p.Fill(bg, 2, y, w - 4, rh);
                if (centered) p.TextCentered(row.Label, 2, y, w - 4, fs, Eram.MenuText, rh);
                else p.Text(row.Label, 2 + Pad, y, fs, Eram.MenuText, rh);
                p.Tag(row, y, rh);
                y += rh;
            }

            y += 2;
            p.Frame(Eram.ViewBorder, 0, 0, w, y, 2);
            return new Size(w, y);
        }

        private void OnWheel(int delta, Point pos)
        {
            var row = Surface.RowAt(pos.Y) as StepRow;
            if (row == null) return;
            row.Nudge(delta > 0 ? +1 : -1);
            Refresh();
        }
    }
}
