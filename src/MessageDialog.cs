using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using static VideoMixApp.Theme;

namespace VideoMixApp
{
    // Axe's own message box (v2.7.1): the rounded frame of the other dialogs instead of the plain
    // Windows one. Enter takes the main button (on the right), Esc the other one or closes a message.
    internal static class MessageDialog
    {
        public enum Kind
        {
            Info,
            Question,
            // Stops or deletes something: red main button.
            Danger
        }

        // A message with 确定.
        public static void Show(Window owner, string text)
        {
            Create(owner, "提示", text, "确定", null, Kind.Info).ShowDialog();
        }

        // true: the main button was chosen; false: the other one, Esc or the × of the title bar.
        public static bool Confirm(Window owner, string title, string text, string yes, string no, Kind kind)
        {
            return Create(owner, title, text, yes, no, kind).ShowDialog() == true;
        }

        internal static Window Create(Window owner, string title, string text, string yes, string no, Kind kind)
        {
            var w = new Window { SizeToContent = SizeToContent.WidthAndHeight };
            var body = new StackPanel { Margin = new Thickness(22.0, 4.0, 22.0, 18.0), MinWidth = 300.0 };
            body.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, MaxWidth = 420.0, FontSize = 13.0, LineHeight = 21.0 }.Fg(K.Text));
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0.0, 20.0, 0.0, 0.0) };
            if (no != null)
            {
                var other = new Button
                {
                    Content = no,
                    MinWidth = 84.0,
                    Height = 34.0,
                    Padding = new Thickness(14.0, 0.0, 14.0, 0.0),
                    FontSize = 13.0,
                    Margin = new Thickness(0.0, 0.0, 10.0, 0.0),
                    IsCancel = true,
                    Style = MainWindow.MakeSecondaryButtonStyle()
                };
                other.Click += (s, e) => w.DialogResult = false;
                buttons.Children.Add(other);
            }
            var main = new Button
            {
                MinWidth = 92.0,
                Height = 34.0,
                Padding = new Thickness(16.0, 0.0, 16.0, 0.0),
                IsDefault = true,
                IsCancel = no == null,
                Style = MainButtonStyle(kind),
                Content = new TextBlock { Text = yes, FontSize = 13.0, FontWeight = FontWeights.SemiBold }
            };
            main.Click += (s, e) => w.DialogResult = true;
            buttons.Children.Add(main);
            body.Children.Add(buttons);
            string icon = kind == Kind.Danger ? IconAlert : kind == Kind.Question ? IconHelp : IconInfo;
            DialogChrome.Apply(w, title, icon, body, false, kind == Kind.Danger ? K.Danger : K.Accent);
            if (owner != null && owner.IsLoaded && owner.IsVisible)
            {
                w.Owner = owner;
            }
            else
            {
                // No window to sit on (yet): the middle of the screen, in the taskbar so it cannot hide.
                w.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                w.ShowInTaskbar = true;
            }
            w.Loaded += (s, e) => main.Focus();
            return w;
        }

        // Danger: a red outline that fills on hover; otherwise the bright start-button face.
        private static Style MainButtonStyle(Kind kind)
        {
            if (kind != Kind.Danger) return MainWindow.MakePrimaryButtonStyle();
            var s = new Style(typeof(Button));
            s.Setters.Add(new Setter(FrameworkElement.CursorProperty, Cursors.Hand));
            s.Setters.Add(DynSetter(Control.ForegroundProperty, K.Danger));
            s.Setters.Add(new Setter(Control.TemplateProperty, MainWindow.ButtonTpl(K.Soft(K.Danger), K.Edge(K.Danger), K.Danger, K.Danger, K.Danger, 8.0)));
            return s;
        }
    }
}
