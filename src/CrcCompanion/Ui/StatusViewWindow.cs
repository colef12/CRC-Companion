using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using CrcCompanion.Core;

namespace CrcCompanion.Ui
{
    /// <summary>
    /// ERAM STATUS view with text you type. Click inside to edit: the window takes the keyboard
    /// (underscore cursor, as in the MCA), then hands it back to CRC when you press Esc or click
    /// elsewhere. Defaults to the line order read from the FAA Academy ERAM frame
    /// (ERAM-R-Side-Handoff.md). Lines are 17 px apart at FONT 2, like the WX view.
    /// </summary>
    public sealed class StatusViewWindow : ListViewWindow
    {
        public static readonly string[] DefaultLines =
        {
            "CH: B - ACTIVE",
            "CA/MCI/EMSAW",
            "CA ON",
            "MCI ON",
            "EM OFF",
            "WEATHER",
            "UPPER WINDS DOWN",
            "METERING INTERFACE OFF",
            "LOCAL CPDLC DOWN",
            "CPDLC SETTINGS",
            "SETTINGS NOT IN SYNC",
        };

        private const int MinChars = 22;
        private const int MaxLineChars = 40;
        private const int MaxLineCount = 40;
        private int _line, _col;
        private double[] _tops = Array.Empty<double>();
        private double _textX, _textY;

        public StatusViewWindow(IViewHost host, ViewSettings settings) : base(host, settings, "STATUS")
        {
            Surface.KeyHandler = OnKey;
            Surface.TextHandler = OnText;
            KeyboardEnded += () => { Surface.Refresh(); Host.MarkDirty(); };
        }

        protected override string MenuTitle => "ST";

        private List<string> Lines => Settings.TextLines ??= DefaultLines.ToList();

        protected override IReadOnlyList<MenuRow> BuildMenuRows() => new List<MenuRow>
        {
            new ModeRow(() => Settings.Opaque, () => { Settings.Opaque = !Settings.Opaque; Changed(); }),
            new ToggleRow("BORDER", () => Settings.Border, () => { Settings.Border = !Settings.Border; Changed(); }),
            FontRow(),
            BrightRow(),
            new CommandRow("DEFAULT", _ => { Settings.TextLines = DefaultLines.ToList(); _line = _col = 0; Changed(); }),
        };

        protected override Size PaintBody(Painter p, double x0, double y0)
        {
            var m = M;
            int pitch = m.TitleH;   // 17 at FONT 2
            var lines = Settings.TextLines ?? DefaultLines.ToList();
            int count = Math.Max(1, lines.Count);
            int chars = Math.Max(MinChars, lines.Select(l => l.Length).DefaultIfEmpty(0).Max() + 1);
            double width = LeftPad + m.TextWidth(chars) + EntryGap;
            double contentH = (count - 1) * pitch + m.GlyphTopInRow + m.Cap;

            if (!p.Drawing) return new Size(width, contentH);

            _textX = x0 + LeftPad;
            _textY = y0;
            _tops = Enumerable.Range(0, count).Select(i => (double)i * pitch).ToArray();
            var tb = TextBrush;
            for (int i = 0; i < lines.Count; i++)
                p.Text(lines[i], _textX, y0 + _tops[i] + m.GlyphTopInRow, m, tb);

            if (KeyboardActive)
            {
                int line = Math.Clamp(_line, 0, count - 1);
                p.Fill(tb, _textX + _col * m.Cell + 1, y0 + _tops[line] + m.GlyphTopInRow + m.Cap + 2, m.Cell - 2, 2);
            }

            // Clicking anywhere in the body starts typing at that spot.
            p.Hit(x0, y0 - BodyPad, width, contentH + BodyPad * 2, _ => StartEditing());
            return new Size(width, contentH);
        }

        private void StartEditing()
        {
            var m = M;
            var pos = Mouse.GetPosition(Surface);
            var lines = Lines;
            int line = 0;
            for (int i = 0; i < _tops.Length; i++)
                if (pos.Y >= _textY + _tops[i]) line = i;
            if (lines.Count == 0) lines.Add("");
            _line = Math.Clamp(line, 0, lines.Count - 1);
            _col = Math.Clamp((int)Math.Round((pos.X - _textX) / m.Cell), 0, lines[_line].Length);
            BeginKeyboard();
            Surface.Refresh();
        }

        private bool OnKey(Key key, ModifierKeys mods)
        {
            if (!KeyboardActive) return false;
            var lines = Lines;
            if (lines.Count == 0) lines.Add("");
            _line = Math.Clamp(_line, 0, lines.Count - 1);
            _col = Math.Clamp(_col, 0, lines[_line].Length);

            switch (key)
            {
                case Key.Escape:
                    EndKeyboard();
                    return true;
                case Key.Enter:
                    if (lines.Count >= MaxLineCount) return true;
                    var rest = lines[_line].Substring(_col);
                    lines[_line] = lines[_line].Substring(0, _col);
                    lines.Insert(_line + 1, rest);
                    _line++; _col = 0;
                    break;
                case Key.Back:
                    if (_col > 0) { lines[_line] = lines[_line].Remove(_col - 1, 1); _col--; }
                    else if (_line > 0)
                    {
                        _col = lines[_line - 1].Length;
                        lines[_line - 1] += lines[_line];
                        lines.RemoveAt(_line);
                        _line--;
                    }
                    break;
                case Key.Delete:
                    if (_col < lines[_line].Length) lines[_line] = lines[_line].Remove(_col, 1);
                    else if (_line + 1 < lines.Count) { lines[_line] += lines[_line + 1]; lines.RemoveAt(_line + 1); }
                    break;
                case Key.Left:
                    if (_col > 0) _col--;
                    else if (_line > 0) { _line--; _col = lines[_line].Length; }
                    break;
                case Key.Right:
                    if (_col < lines[_line].Length) _col++;
                    else if (_line + 1 < lines.Count) { _line++; _col = 0; }
                    break;
                case Key.Up:
                    if (_line > 0) { _line--; _col = Math.Min(_col, lines[_line].Length); }
                    break;
                case Key.Down:
                    if (_line + 1 < lines.Count) { _line++; _col = Math.Min(_col, lines[_line].Length); }
                    break;
                case Key.Home:
                    _col = 0;
                    break;
                case Key.End:
                    _col = lines[_line].Length;
                    break;
                default:
                    return false; // printable characters arrive through OnText
            }
            Changed();
            return true;
        }

        private void OnText(string text)
        {
            if (!KeyboardActive) return;
            var lines = Lines;
            if (lines.Count == 0) lines.Add("");
            _line = Math.Clamp(_line, 0, lines.Count - 1);
            foreach (var raw in text.ToUpperInvariant())
            {
                if (char.IsControl(raw)) continue;
                var c = raw == ' ' || EramFont.Has(raw) ? raw : '?';
                if (lines[_line].Length >= MaxLineChars) break;
                _col = Math.Clamp(_col, 0, lines[_line].Length);
                lines[_line] = lines[_line].Insert(_col, c.ToString());
                _col++;
            }
            Changed();
        }
    }
}
