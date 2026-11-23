using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using static VideoMixApp.Theme;

namespace VideoMixApp
{
    // The borderless frame of Axe's small dialogs (same look as the main window and the folder picker):
    // cut corners with accent brackets, a title bar to drag the window, a close button and the dialog's
    // own content below.
    internal static class DialogChrome
    {
        public static void Apply(Window w, string title, string icon, UIElement body, bool resizable)
        {
            Apply(w, title, icon, body, resizable, K.Accent);
        }

        public static void Apply(Window w, string title, string icon, UIElement body, bool resizable, string iconKey)
        {
            Ensure(w);
            w.Title = title;
            w.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            w.WindowStyle = WindowStyle.None;
            w.AllowsTransparency = true;
            w.Background = Brushes.Transparent;
            w.ResizeMode = resizable ? ResizeMode.CanResize : ResizeMode.NoResize;
            w.ShowInTaskbar = false;
            w.FontFamily = UiFont;
            w.UseLayoutRounding = true;
            TextOptions.SetTextFormattingMode(w, TextFormattingMode.Display);
            var root = new Grid { Background = SB(1, 0, 0, 0) };
            Chamfer frame = DialogFrame(root);
            var g = new Grid();
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.0, GridUnitType.Star) });
            frame.Child = g;
            var bar = new Border { Padding = new Thickness(18.0, 10.0, 10.0, 6.0) }.Bg(K.Clear);
            var tg = new Grid();
            tg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
            tg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var ts = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            ts.Children.Add(MakeIcon(icon, iconKey, 18.0));
            ts.Children.Add(new TextBlock { Text = title, FontSize = 14.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(9.0, 0.0, 0.0, 0.0), VerticalAlignment = VerticalAlignment.Center }.Fg(K.Text));
            Button close = MainWindow.MakeWinButton(IconClose, true);
            close.Click += (s, e) => w.Close();
            Grid.SetColumn(close, 1);
            tg.Children.Add(ts);
            tg.Children.Add(close);
            bar.Child = tg;
            bar.MouseLeftButtonDown += (s, e) =>
            {
                try { w.DragMove(); } catch { }
            };
            g.Children.Add(bar);
            // Hairline under the title with a lit segment, as in the main window.
            var under = new Grid { Height = 1.0, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(16.0, 0.0, 16.0, 0.0) };
            under.Children.Add(new Border().Bg(K.Line));
            under.Children.Add(new Border { Width = 90.0, HorizontalAlignment = HorizontalAlignment.Left }.Bg(K.Accent));
            g.Children.Add(under);
            Grid.SetRow(body, 1);
            if (body is FrameworkElement fe && fe.Margin.Top < 8.0) fe.Margin = new Thickness(fe.Margin.Left, 10.0, fe.Margin.Right, fe.Margin.Bottom);
            g.Children.Add(body);
            w.Content = root;
            if (resizable) WindowResizer.Enable(w, 12.0);
        }
    }
}
