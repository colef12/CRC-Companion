using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace CrcCompanion.Tray
{
    /// <summary>Small plain dialog for profile names (tray menu only, not ERAM-styled).</summary>
    public sealed class InputDialog : Window
    {
        private readonly TextBox _box;

        public string Value => _box.Text;

        public InputDialog(string title, string prompt, string initial)
        {
            Title = title;
            SizeToContent = SizeToContent.WidthAndHeight;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Topmost = true;

            _box = new TextBox { Text = initial, Width = 260, Margin = new Thickness(0, 6, 0, 10), CharacterCasing = CharacterCasing.Upper };
            _box.SelectAll();

            var ok = new Button { Content = "OK", Width = 80, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            ok.Click += (_, _) => { DialogResult = true; };
            var cancel = new Button { Content = "Cancel", Width = 80, IsCancel = true };

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);

            var stack = new StackPanel { Margin = new Thickness(14) };
            stack.Children.Add(new TextBlock { Text = prompt });
            stack.Children.Add(_box);
            stack.Children.Add(buttons);
            Content = stack;

            Loaded += (_, _) => { Activate(); _box.Focus(); Keyboard.Focus(_box); };
        }

        public static string? Ask(string title, string prompt, string initial = "")
        {
            var d = new InputDialog(title, prompt, initial);
            return d.ShowDialog() == true ? d.Value : null;
        }
    }
}
