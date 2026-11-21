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

    /// <summary>
    /// The O/T row. As in CRC it shows the mode a click switches to:
    /// "T" while the view is opaque, "O" while it is semi-transparent.
    /// </summary>
    public sealed class ModeRow : MenuRow
    {
        private readonly Func<bool> _opaque;
        public Action Toggle { get; }
        public ModeRow(Func<bool> opaque, Action toggle) { _opaque = opaque; Toggle = toggle; }
        public override string Label => _opaque() ? "T" : "O";
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

    /// <summary>
    /// ERAM view menu, measured from CRC (screen pixels): 1 px border, 17 px rows separated by
    /// 1 px lines, gray title row with a 23 px X cell, text 5 px in from the border.
    /// Inner width fits 11 characters (or the longest label plus 2).
    /// </summary>
    public sealed class MenuWindow : EramWindow
    {
        private readonly string _title;
        private readonly IReadOnlyList<MenuRow> _rows;
        private static readonly EramMetrics M = new EramMetrics(2);
        private const int TextInset = 5;

        public MenuWindow(string title, IReadOnlyList<MenuRow> rows)
        {
            _title = title;
            _rows = rows;
            Surface.Paint = Paint;
            Surface.Wheel = OnWheel;
        }

        public void Refresh() => Surface.Refresh();

        private Size Paint(Painter p)
        {
            var m = M;
            int rh = m.TitleH, box = m.BoxW;
            int chars = Math.Max(11, _rows.Select(r => r.Label.Length).DefaultIfEmpty(0).Max() + 2);
            chars = Math.Max(chars, _title.Length + 4);
            double inner = 6 + m.TextWidth(chars);
            double w = inner + 2;
            double h = 1 + rh + _rows.Count * (rh + 1) + 1;
            double gt = m.GlyphTopInTitle;

            // Title row: [   TITLE   |X]
            double y = 1;
            p.Fill(Eram.MenuTitle, 1, y, inner, rh);
            double titleW = inner - box - 1;
            p.TextCentered(_title, 1, titleW, y + gt, m, Eram.MenuText);
            p.Fill(Eram.MenuLine, 1 + titleW, y, 1, rh);
            p.TextCentered("X", 2 + titleW, box, y + gt, m, Eram.MenuText);
            p.Hit(2 + titleW, y, box, rh, _ => Close());
            p.Hit(1, y, titleW, rh, _ => BeginReposition());
            y += rh;

            foreach (var row in _rows)
            {
                p.Fill(Eram.MenuLine, 1, y, inner, 1);
                y += 1;
                Brush bg = Eram.Black;
                bool centered = false;
                switch (row)
                {
                    case ModeRow md:
                        centered = true;
                        p.Hit(1, y, inner, rh, _ => { md.Toggle(); Refresh(); });
                        break;
                    case ToggleRow t:
                        bg = t.IsOn() ? Eram.MenuOn : Eram.Black;
                        p.Hit(1, y, inner, rh, _ => { t.Toggle(); Refresh(); });
                        break;
                    case StepRow s:
                        bg = Eram.MenuGreen;
                        p.Hit(1, y, inner, rh, b => { s.Nudge(b == MouseButton.Middle ? +1 : -1); Refresh(); });
                        break;
                    case CommandRow c:
                        var run = c.Run;
                        if (run != null) p.Hit(1, y, inner, rh, b => run(b));
                        break;
                }
                p.Fill(bg, 1, y, inner, rh);
                if (centered) p.TextCentered(row.Label, 1, inner, y + gt, m, Eram.MenuText);
                else p.Text(row.Label, 1 + TextInset, y + gt, m, Eram.MenuText);
                p.Tag(row, y, rh);
                y += rh;
            }

            p.Frame(Eram.ViewBorder, 0, 0, w, h, 1);
            return new Size(w, h);
        }

        private void OnWheel(int delta, Point pos)
        {
            if (Surface.RowAt(pos.Y) is StepRow row)
            {
                row.Nudge(delta > 0 ? +1 : -1);
                Refresh();
            }
        }
    }
}
