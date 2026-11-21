using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace CrcCompanion.Ui
{
    /// <summary>
    /// ERAM-styled one-line entry for adding/removing stations, e.g. "IAD DCA BWI RIC" or "-RIC".
    /// Unlike the views, this window takes keyboard focus while open, then hands focus
    /// back to the window that had it (normally CRC) when it closes.
    /// Enter applies (and stays open), Enter on an empty line or Esc closes.
    /// </summary>
    public sealed class StationEntryWindow : Window
    {
        private readonly Func<IReadOnlyList<string>> _getList;
        private readonly Func<string, string> _apply;
        private readonly IntPtr _returnFocusTo;
        private readonly TextBox _box;
        private readonly TextBlock _feedback;
        private readonly TextBlock _list;
        private bool _closing;

        public StationEntryWindow(string title, Func<IReadOnlyList<string>> getList, Func<string, string> apply,
            IntPtr returnFocusTo, double scale)
        {
            _getList = getList;
            _apply = apply;
            _returnFocusTo = returnFocusTo;

            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            Topmost = true;
            ShowInTaskbar = false;
            SizeToContent = SizeToContent.WidthAndHeight;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Background = Eram.Black;
            UseLayoutRounding = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);

            const double fs = Eram.EntryFontSize;
            const double width = 340;

            var titleBar = new Border
            {
                Background = Eram.MenuTitle,
                Child = new TextBlock
                {
                    Text = title,
                    Foreground = Eram.MenuText,
                    FontFamily = Eram.EntryFamily,
                    FontSize = fs,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 1, 0, 1),
                },
            };

            _box = new TextBox
            {
                Background = Eram.Black,
                Foreground = Eram.TextGray(80),
                CaretBrush = Eram.TextGray(80),
                BorderThickness = new Thickness(0, 0, 0, 1),
                BorderBrush = Eram.ViewBorder,
                FontFamily = Eram.EntryFamily,
                FontSize = fs,
                CharacterCasing = CharacterCasing.Upper,
                Padding = new Thickness(3, 2, 3, 2),
            };
            _box.PreviewKeyDown += OnKey;

            _feedback = new TextBlock
            {
                Foreground = Eram.TextGray(80),
                FontFamily = Eram.EntryFamily,
                FontSize = fs,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(3, 2, 3, 0),
                Text = "TYPE IDS: IAD DCA BWI  (-IAD REMOVES)",
            };

            _list = new TextBlock
            {
                Foreground = Eram.ScrollGray,
                FontFamily = Eram.EntryFamily,
                FontSize = fs,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(3, 2, 3, 3),
            };

            var stack = new StackPanel { Width = width };
            stack.Children.Add(titleBar);
            stack.Children.Add(_box);
            stack.Children.Add(_feedback);
            stack.Children.Add(_list);

            var root = new Border
            {
                BorderBrush = Eram.ViewBorder,
                BorderThickness = new Thickness(2),
                Child = stack,
            };
            if (Math.Abs(scale - 1.0) > 0.001) root.LayoutTransform = new ScaleTransform(scale, scale);
            Content = root;

            RefreshList();

            Loaded += (_, _) =>
            {
                Activate();
                _box.Focus();
                Keyboard.Focus(_box);
            };
            Deactivated += (_, _) => SafeClose();
            Closed += (_, _) =>
            {
                if (_returnFocusTo != IntPtr.Zero) Native.SetForegroundWindow(_returnFocusTo);
            };
        }

        /// <summary>Shows the entry at a screen position in physical pixels.</summary>
        public void ShowNear(int x, int y)
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).EnsureHandle();
            Native.MoveWindow(hwnd, x, y);
            Show();
            Native.MoveWindow(hwnd, x, y);
        }

        private void RefreshList()
        {
            var list = _getList();
            _list.Text = list.Count == 0 ? "NO STATIONS" : string.Join(" ", list);
        }

        private void OnKey(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                SafeClose();
            }
            else if (e.Key == Key.Enter || e.Key == Key.Return)
            {
                e.Handled = true;
                var text = _box.Text.Trim();
                if (text.Length == 0) { SafeClose(); return; }
                _feedback.Text = _apply(text);
                _box.Clear();
                RefreshList();
            }
        }

        private void SafeClose()
        {
            if (_closing) return;
            _closing = true;
            Dispatcher.BeginInvoke(new Action(Close));
        }
    }
}
