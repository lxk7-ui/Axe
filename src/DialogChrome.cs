using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using static VideoMixApp.Theme;

namespace VideoMixApp
{
    // The rounded, borderless frame of Axe's small dialogs (same look as the folder picker): a title
    // bar to drag the window, a close button and the dialog's own content below.
    internal static class DialogChrome
    {
        public static void Apply(Window w, string title, string icon, UIElement body, bool resizable)
        {
            w.Title = title;
            w.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            w.WindowStyle = WindowStyle.None;
            w.AllowsTransparency = true;
            w.Background = Brushes.Transparent;
            w.ResizeMode = resizable ? ResizeMode.CanResize : ResizeMode.NoResize;
            w.ShowInTaskbar = false;
            w.FontFamily = new FontFamily("Microsoft YaHei");
            w.UseLayoutRounding = true;
            var root = new Grid { Background = SB(1, 255, 255, 255) };
            var frame = new Border
            {
                Margin = new Thickness(12.0),
                Background = Brushes.White,
                CornerRadius = new CornerRadius(18.0),
                BorderBrush = SB(CBorder),
                BorderThickness = new Thickness(1.0),
                Effect = Shadow(Color.FromRgb(110, 126, 150), 24.0, 0.0, 0.35)
            };
            root.Children.Add(frame);
            var g = new Grid();
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.0, GridUnitType.Star) });
            frame.Child = g;
            var bar = new Border { Background = Brushes.White, CornerRadius = new CornerRadius(18.0, 18.0, 0.0, 0.0), Padding = new Thickness(18.0, 10.0, 10.0, 4.0) };
            var tg = new Grid();
            tg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
            tg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var ts = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            ts.Children.Add(MakeIcon(icon, SB(CPrimary), 18.0));
            ts.Children.Add(new TextBlock { Text = title, FontSize = 14.0, FontWeight = FontWeights.SemiBold, Foreground = SB(CTextMain), Margin = new Thickness(9.0, 0.0, 0.0, 0.0), VerticalAlignment = VerticalAlignment.Center });
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
            Grid.SetRow(body, 1);
            g.Children.Add(body);
            w.Content = root;
            if (resizable) WindowResizer.Enable(w, 12.0);
        }
    }
}
