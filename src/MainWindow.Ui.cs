using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Path = System.Windows.Shapes.Path;
using static VideoMixApp.Theme;

namespace VideoMixApp
{
    // v2.8.0: the angular "tech" look. All colours are DynamicResource keys (K.*), so a new 背景色 repaints
    // every window in place; panels, buttons and inputs are cut-corner shapes (Chamfer).
    public partial class MainWindow
    {
        private void BuildStyles()
        {
            Resources["PrimaryButton"] = MakePrimaryButtonStyle();
            Resources["StopButton"] = MakeStopButtonStyle();
            Resources["Menu"] = MakeMenuStyle();
            Resources["MenuItem"] = MakeMenuItemStyle();
            Resources["SecondaryButton"] = MakeSecondaryButtonStyle();
            Resources["GhostButton"] = MakeGhostButtonStyle();
            Resources["InputBox"] = MakeTextBoxStyle();
            Resources["Combo"] = MakeComboBoxStyle();
            Resources["Check"] = MakeCheckBoxStyle();
            Resources["Bar"] = MakeProgressBarStyle(6.0);
        }

        // Button template: a Chamfer named "bd" around the content. Colours are resource keys; border keys may be null.
        internal static ControlTemplate ButtonTpl(string normal, string hover, string pressed, string border, string borderHover, double cut, Cuts corners = Cuts.Diagonal, string accent = null)
        {
            var bd = new FrameworkElementFactory(typeof(Chamfer), "bd");
            bd.SetResourceReference(Chamfer.BackgroundProperty, normal);
            bd.SetValue(Chamfer.CutProperty, cut);
            bd.SetValue(Chamfer.CornersProperty, corners);
            bd.SetValue(Chamfer.AccentSizeProperty, 6.0);
            if (border != null)
            {
                bd.SetResourceReference(Chamfer.BorderBrushProperty, border);
                bd.SetValue(Chamfer.BorderThicknessProperty, 1.0);
            }
            else
            {
                bd.SetValue(Chamfer.BorderThicknessProperty, 0.0);
            }
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            cp.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            cp.SetValue(FrameworkElement.MarginProperty, new TemplateBindingExtension(Control.PaddingProperty));
            bd.AppendChild(cp);
            var tpl = new ControlTemplate(typeof(Button)) { VisualTree = bd };
            var over = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            over.Setters.Add(DynSetter(Chamfer.BackgroundProperty, hover, "bd"));
            if (borderHover != null) over.Setters.Add(DynSetter(Chamfer.BorderBrushProperty, borderHover, "bd"));
            if (accent != null) over.Setters.Add(DynSetter(Chamfer.AccentProperty, accent, "bd"));
            tpl.Triggers.Add(over);
            var press = new Trigger { Property = ButtonBase.IsPressedProperty, Value = true };
            press.Setters.Add(DynSetter(Chamfer.BackgroundProperty, pressed, "bd"));
            tpl.Triggers.Add(press);
            var off = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            off.Setters.Add(new Setter(UIElement.OpacityProperty, 0.42));
            tpl.Triggers.Add(off);
            return tpl;
        }

        internal static Style MakePrimaryButtonStyle()
        {
            var s = new Style(typeof(Button));
            s.Setters.Add(DynSetter(Control.ForegroundProperty, K.OnAccent));
            s.Setters.Add(new Setter(FrameworkElement.CursorProperty, System.Windows.Input.Cursors.Hand));
            s.Setters.Add(new Setter(Control.TemplateProperty, ButtonTpl(K.StartBg, K.StartHover, K.StartPress, null, null, 14.0)));
            return s;
        }

        // The big button while a task runs (停止).
        private static Style MakeStopButtonStyle()
        {
            var s = new Style(typeof(Button));
            s.Setters.Add(DynSetter(Control.ForegroundProperty, K.Danger));
            s.Setters.Add(new Setter(FrameworkElement.CursorProperty, System.Windows.Input.Cursors.Hand));
            s.Setters.Add(new Setter(Control.TemplateProperty, ButtonTpl(K.Soft(K.Danger), K.Edge(K.Danger), K.Danger, K.Danger, K.Danger, 14.0)));
            return s;
        }

        // Drop-down used by the 变量 button.
        private static Style MakeMenuStyle()
        {
            const string xaml =
                "<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='ContextMenu'>" +
                "<Setter Property='OverridesDefaultStyle' Value='True'/><Setter Property='HasDropShadow' Value='False'/>" +
                "<Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ContextMenu'>" +
                "<Border Background='{DynamicResource ax.panel}' BorderBrush='{DynamicResource ax.accentLine}' BorderThickness='1' Padding='5' Margin='0,4,10,10'>" +
                "<Border.Effect><DropShadowEffect Color='Black' BlurRadius='16' ShadowDepth='3' Opacity='0.4'/></Border.Effect>" +
                "<StackPanel IsItemsHost='True'/></Border>" +
                "</ControlTemplate></Setter.Value></Setter></Style>";
            return (Style)System.Windows.Markup.XamlReader.Parse(xaml);
        }

        private static Style MakeMenuItemStyle()
        {
            const string xaml =
                "<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='MenuItem'>" +
                "<Setter Property='OverridesDefaultStyle' Value='True'/><Setter Property='Cursor' Value='Hand'/>" +
                "<Setter Property='Template'><Setter.Value><ControlTemplate TargetType='MenuItem'>" +
                "<Border x:Name='bd' Background='Transparent' Padding='10,6,12,6'>" +
                "<ContentPresenter ContentSource='Header' VerticalAlignment='Center'/></Border>" +
                "<ControlTemplate.Triggers><Trigger Property='IsHighlighted' Value='True'>" +
                "<Setter TargetName='bd' Property='Background' Value='{DynamicResource ax.accentSoft}'/></Trigger></ControlTemplate.Triggers>" +
                "</ControlTemplate></Setter.Value></Setter></Style>";
            return (Style)System.Windows.Markup.XamlReader.Parse(xaml);
        }

        internal static Style MakeSecondaryButtonStyle()
        {
            var s = new Style(typeof(Button));
            s.Setters.Add(DynSetter(Control.ForegroundProperty, K.Text));
            s.Setters.Add(new Setter(FrameworkElement.CursorProperty, System.Windows.Input.Cursors.Hand));
            s.Setters.Add(new Setter(Control.TemplateProperty, ButtonTpl(K.Panel2, K.Panel3, K.AccentSoft, K.Line, K.AccentLine, 7.0, Cuts.Diagonal, K.Accent)));
            return s;
        }

        internal static Style MakeGhostButtonStyle()
        {
            var s = new Style(typeof(Button));
            s.Setters.Add(new Setter(FrameworkElement.CursorProperty, System.Windows.Input.Cursors.Hand));
            s.Setters.Add(new Setter(Control.TemplateProperty, ButtonTpl(K.Clear, K.AccentSoft, K.AccentLine, null, null, 6.0)));
            return s;
        }

        // 强制重建: an outline in the warning colour.
        internal static Style MakeWarnButtonStyle()
        {
            var s = new Style(typeof(Button));
            s.Setters.Add(DynSetter(Control.ForegroundProperty, K.Warn));
            s.Setters.Add(new Setter(FrameworkElement.CursorProperty, System.Windows.Input.Cursors.Hand));
            s.Setters.Add(new Setter(Control.TemplateProperty, ButtonTpl(K.Clear, K.Soft(K.Warn), K.Edge(K.Warn), K.Edge(K.Warn), K.Warn, 7.0, Cuts.Diagonal, K.Warn)));
            return s;
        }

        internal static Style MakeTextBoxStyle()
        {
            var bd = new FrameworkElementFactory(typeof(Chamfer), "bd");
            bd.SetResourceReference(Chamfer.BackgroundProperty, K.Panel2);
            bd.SetResourceReference(Chamfer.BorderBrushProperty, K.Line);
            bd.SetValue(Chamfer.BorderThicknessProperty, 1.0);
            bd.SetValue(Chamfer.CutProperty, 7.0);
            bd.SetValue(Chamfer.CornersProperty, Cuts.TopLeft);
            var host = new FrameworkElementFactory(typeof(ScrollViewer), "PART_ContentHost");
            host.SetValue(FrameworkElement.MarginProperty, new TemplateBindingExtension(Control.PaddingProperty));
            host.SetValue(Control.BackgroundProperty, Brushes.Transparent);
            host.SetValue(UIElement.FocusableProperty, false);
            host.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Hidden);
            host.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Hidden);
            bd.AppendChild(host);
            var tpl = new ControlTemplate(typeof(TextBox)) { VisualTree = bd };
            var over = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            over.Setters.Add(DynSetter(Chamfer.BorderBrushProperty, K.LineHi, "bd"));
            tpl.Triggers.Add(over);
            var focus = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true };
            focus.Setters.Add(DynSetter(Chamfer.BorderBrushProperty, K.Accent, "bd"));
            focus.Setters.Add(DynSetter(Chamfer.AccentProperty, K.Accent, "bd"));
            tpl.Triggers.Add(focus);
            var off = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            off.Setters.Add(new Setter(UIElement.OpacityProperty, 0.55));
            tpl.Triggers.Add(off);
            var s = new Style(typeof(TextBox));
            s.Setters.Add(DynSetter(Control.ForegroundProperty, K.Text));
            s.Setters.Add(DynSetter(System.Windows.Controls.Primitives.TextBoxBase.CaretBrushProperty, K.Accent));
            s.Setters.Add(DynSetter(System.Windows.Controls.Primitives.TextBoxBase.SelectionBrushProperty, K.Accent));
            s.Setters.Add(new Setter(Control.FontSizeProperty, 13.0));
            s.Setters.Add(new Setter(Control.TemplateProperty, tpl));
            return s;
        }

        internal static Style MakeComboBoxStyle()
        {
            var bd = new FrameworkElementFactory(typeof(Chamfer), "bd");
            bd.SetResourceReference(Chamfer.BackgroundProperty, K.Panel2);
            bd.SetResourceReference(Chamfer.BorderBrushProperty, K.Line);
            bd.SetValue(Chamfer.BorderThicknessProperty, 1.0);
            bd.SetValue(Chamfer.CutProperty, 7.0);
            bd.SetValue(Chamfer.CornersProperty, Cuts.TopLeft);
            var grid = new FrameworkElementFactory(typeof(Grid));
            var c0 = new FrameworkElementFactory(typeof(ColumnDefinition));
            c0.SetValue(ColumnDefinition.WidthProperty, new GridLength(1.0, GridUnitType.Star));
            var c1 = new FrameworkElementFactory(typeof(ColumnDefinition));
            c1.SetValue(ColumnDefinition.WidthProperty, GridLength.Auto);
            grid.AppendChild(c0);
            grid.AppendChild(c1);

            var toggleTpl = new ControlTemplate(typeof(ToggleButton));
            var toggleBd = new FrameworkElementFactory(typeof(Border));
            toggleBd.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            toggleTpl.VisualTree = toggleBd;
            var toggle = new FrameworkElementFactory(typeof(ToggleButton));
            toggle.SetValue(Grid.ColumnSpanProperty, 2);
            toggle.SetValue(UIElement.FocusableProperty, false);
            toggle.SetValue(Control.TemplateProperty, toggleTpl);
            toggle.SetBinding(ToggleButton.IsCheckedProperty, new Binding("IsDropDownOpen") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent), Mode = BindingMode.TwoWay });
            grid.AppendChild(toggle);

            var content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(FrameworkElement.MarginProperty, new Thickness(10.0, 0.0, 6.0, 0.0));
            content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            content.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
            content.SetValue(UIElement.IsHitTestVisibleProperty, false);
            content.SetBinding(ContentPresenter.ContentProperty, new Binding("SelectionBoxItem") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
            grid.AppendChild(content);

            var arrow = new FrameworkElementFactory(typeof(Path));
            arrow.SetValue(Grid.ColumnProperty, 1);
            arrow.SetValue(Path.DataProperty, Geo("M7,10 L12,15 L17,10 Z"));
            arrow.SetResourceReference(Shape.FillProperty, K.Accent);
            arrow.SetValue(FrameworkElement.WidthProperty, 10.0);
            arrow.SetValue(FrameworkElement.HeightProperty, 6.0);
            arrow.SetValue(Shape.StretchProperty, Stretch.Uniform);
            arrow.SetValue(FrameworkElement.MarginProperty, new Thickness(8.0, 0.0, 10.0, 0.0));
            arrow.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            arrow.SetValue(UIElement.IsHitTestVisibleProperty, false);
            grid.AppendChild(arrow);
            bd.AppendChild(grid);

            var popup = new FrameworkElementFactory(typeof(Popup), "PART_Popup");
            popup.SetValue(Popup.PlacementProperty, PlacementMode.Bottom);
            popup.SetValue(Popup.AllowsTransparencyProperty, true);
            popup.SetValue(Popup.VerticalOffsetProperty, 3.0);
            popup.SetBinding(Popup.IsOpenProperty, new Binding("IsDropDownOpen") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent), Mode = BindingMode.TwoWay });
            var popupBd = new FrameworkElementFactory(typeof(Chamfer));
            popupBd.SetResourceReference(Chamfer.BackgroundProperty, K.Panel);
            popupBd.SetResourceReference(Chamfer.BorderBrushProperty, K.AccentLine);
            popupBd.SetValue(Chamfer.CutProperty, 8.0);
            popupBd.SetBinding(FrameworkElement.MinWidthProperty, new Binding("ActualWidth") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
            var scroller = new FrameworkElementFactory(typeof(ScrollViewer));
            scroller.SetValue(FrameworkElement.MaxHeightProperty, 320.0);
            scroller.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
            var items = new FrameworkElementFactory(typeof(ItemsPresenter));
            items.SetValue(FrameworkElement.MarginProperty, new Thickness(4.0));
            scroller.AppendChild(items);
            popupBd.AppendChild(scroller);
            popup.AppendChild(popupBd);

            var root = new FrameworkElementFactory(typeof(Grid));
            root.AppendChild(bd);
            root.AppendChild(popup);
            var tpl = new ControlTemplate(typeof(ComboBox)) { VisualTree = root };
            var over = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            over.Setters.Add(DynSetter(Chamfer.BorderBrushProperty, K.LineHi, "bd"));
            tpl.Triggers.Add(over);
            var focus = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true };
            focus.Setters.Add(DynSetter(Chamfer.BorderBrushProperty, K.Accent, "bd"));
            tpl.Triggers.Add(focus);

            var ib = new FrameworkElementFactory(typeof(Chamfer), "ib");
            ib.SetResourceReference(Chamfer.BackgroundProperty, K.Clear);
            ib.SetValue(Chamfer.BorderThicknessProperty, 0.0);
            ib.SetValue(Chamfer.CutProperty, 5.0);
            ib.SetValue(Chamfer.PaddingProperty, new Thickness(9.0, 7.0, 9.0, 7.0));
            ib.SetValue(FrameworkElement.MarginProperty, new Thickness(1.0));
            var icp = new FrameworkElementFactory(typeof(ContentPresenter));
            icp.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            ib.AppendChild(icp);
            var itemTpl = new ControlTemplate(typeof(ComboBoxItem)) { VisualTree = ib };
            var itemOver = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            itemOver.Setters.Add(DynSetter(Chamfer.BackgroundProperty, K.Panel3, "ib"));
            itemTpl.Triggers.Add(itemOver);
            var itemSel = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
            itemSel.Setters.Add(DynSetter(Chamfer.BackgroundProperty, K.AccentSoft, "ib"));
            itemTpl.Triggers.Add(itemSel);
            var itemStyle = new Style(typeof(ComboBoxItem));
            itemStyle.Setters.Add(DynSetter(Control.ForegroundProperty, K.Text));
            itemStyle.Setters.Add(new Setter(Control.FontSizeProperty, 13.0));
            itemStyle.Setters.Add(new Setter(Control.TemplateProperty, itemTpl));

            var s = new Style(typeof(ComboBox));
            s.Setters.Add(DynSetter(Control.ForegroundProperty, K.Text));
            s.Setters.Add(new Setter(FrameworkElement.HeightProperty, 34.0));
            s.Setters.Add(new Setter(FrameworkElement.CursorProperty, System.Windows.Input.Cursors.Hand));
            s.Setters.Add(new Setter(Control.TemplateProperty, tpl));
            s.Setters.Add(new Setter(ItemsControl.ItemContainerStyleProperty, itemStyle));
            return s;
        }

        // Square tick box with a cut corner.
        internal static Style MakeCheckBoxStyle()
        {
            var panel = new FrameworkElementFactory(typeof(StackPanel));
            panel.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
            panel.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            panel.SetValue(Panel.BackgroundProperty, Brushes.Transparent);
            var box = new FrameworkElementFactory(typeof(Chamfer), "box");
            box.SetValue(FrameworkElement.WidthProperty, 18.0);
            box.SetValue(FrameworkElement.HeightProperty, 18.0);
            box.SetResourceReference(Chamfer.BackgroundProperty, K.Panel2);
            box.SetResourceReference(Chamfer.BorderBrushProperty, K.LineHi);
            box.SetValue(Chamfer.BorderThicknessProperty, 1.4);
            box.SetValue(Chamfer.CutProperty, 5.0);
            box.SetValue(Chamfer.CornersProperty, Cuts.TopRight);
            box.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            var chk = new FrameworkElementFactory(typeof(Path), "chk");
            chk.SetValue(Path.DataProperty, Geo("M3.5,12.5 L9,18 L20.5,6"));
            chk.SetResourceReference(Shape.StrokeProperty, K.OnAccent);
            chk.SetValue(Shape.StrokeThicknessProperty, 2.6);
            chk.SetValue(Shape.StrokeLineJoinProperty, PenLineJoin.Miter);
            chk.SetValue(Shape.StretchProperty, Stretch.Uniform);
            chk.SetValue(FrameworkElement.MarginProperty, new Thickness(3.0));
            chk.SetValue(UIElement.VisibilityProperty, Visibility.Collapsed);
            box.AppendChild(chk);
            panel.AppendChild(box);
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(FrameworkElement.MarginProperty, new Thickness(10.0, 0.0, 0.0, 0.0));
            cp.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            panel.AppendChild(cp);
            var tpl = new ControlTemplate(typeof(CheckBox)) { VisualTree = panel };
            var over = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            over.Setters.Add(DynSetter(Chamfer.BorderBrushProperty, K.Accent, "box"));
            tpl.Triggers.Add(over);
            var on = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true };
            on.Setters.Add(DynSetter(Chamfer.BackgroundProperty, K.Accent, "box"));
            on.Setters.Add(DynSetter(Chamfer.BorderBrushProperty, K.Accent, "box"));
            on.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible, "chk"));
            tpl.Triggers.Add(on);
            var off = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            off.Setters.Add(new Setter(UIElement.OpacityProperty, 0.45));
            tpl.Triggers.Add(off);
            var s = new Style(typeof(CheckBox));
            s.Setters.Add(new Setter(FrameworkElement.CursorProperty, System.Windows.Input.Cursors.Hand));
            s.Setters.Add(DynSetter(Control.ForegroundProperty, K.Text));
            s.Setters.Add(new Setter(Control.TemplateProperty, tpl));
            return s;
        }

        // Thin track with a bright bar and a slanted end, like a gauge.
        internal static Style MakeProgressBarStyle(double height)
        {
            var bd = new FrameworkElementFactory(typeof(Chamfer));
            bd.SetResourceReference(Chamfer.BackgroundProperty, K.Panel3);
            bd.SetValue(Chamfer.BorderThicknessProperty, 0.0);
            bd.SetValue(Chamfer.CutProperty, height * 0.75);
            bd.SetValue(Chamfer.CornersProperty, Cuts.BottomRight);
            var track = new FrameworkElementFactory(typeof(Grid), "PART_Track");
            var ind = new FrameworkElementFactory(typeof(Chamfer), "PART_Indicator");
            ind.SetResourceReference(Chamfer.BackgroundProperty, K.StartBg);
            ind.SetValue(Chamfer.BorderThicknessProperty, 0.0);
            ind.SetValue(Chamfer.CutProperty, height * 0.75);
            ind.SetValue(Chamfer.CornersProperty, Cuts.BottomRight);
            ind.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
            track.AppendChild(ind);
            bd.AppendChild(track);
            var tpl = new ControlTemplate(typeof(ProgressBar)) { VisualTree = bd };
            var s = new Style(typeof(ProgressBar));
            s.Setters.Add(new Setter(FrameworkElement.HeightProperty, height));
            s.Setters.Add(new Setter(Control.TemplateProperty, tpl));
            return s;
        }

        private DropShadowEffect _windowGlow;
        private Chamfer _glowFrame;
        private Button _themeBtn;
        private Popup _themePopup;
        private string _statusKey = K.Keep;

        private void BuildUi()
        {
            Title = "Axe " + AppInfo.Version;
            Rect work = SystemParameters.WorkArea;
            Width = Math.Min(1220.0, work.Width);
            Height = Math.Min(780.0, work.Height);
            MinWidth = Math.Min(1000.0, work.Width);
            MinHeight = Math.Min(640.0, work.Height);
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            FontFamily = UiFont;
            UseLayoutRounding = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            StateChanged += WindowStateChanged;
            string ico = System.IO.Path.Combine(_appDir, "Axe.ico");
            if (File.Exists(ico))
            {
                try
                {
                    var bi = new BitmapImage();
                    bi.BeginInit();
                    bi.CacheOption = BitmapCacheOption.OnLoad;
                    bi.UriSource = new Uri(ico);
                    bi.EndInit();
                    bi.Freeze();
                    Icon = bi;
                }
                catch
                {
                }
            }

            // Almost transparent instead of empty, so the shadow margin receives the mouse for resizing.
            var root = new Grid { Background = SB(1, 0, 0, 0) };
            _windowFrame = new Chamfer { Margin = new Thickness(10.0), Cut = 18.0, BorderThickness = 1.0, AccentSize = 22.0 }.Bg(K.Page).Bd(K.LineHi);
            _windowFrame.SetResourceReference(Chamfer.AccentProperty, K.Accent);
            _windowGlow = new DropShadowEffect { BlurRadius = 26.0, ShadowDepth = 0.0 };
            // v2.8.3: the glow comes from an empty copy of the frame behind it, kept as a bitmap. On the
            // frame itself the effect made WPF draw the whole window again and blur it at every change
            // inside (progress bar, status line, each log line), all through a batch, and on the CPU
            // where the graphics driver does not take it.
            _glowFrame = new Chamfer { Margin = _windowFrame.Margin, Cut = _windowFrame.Cut, BorderThickness = 0.0, IsHitTestVisible = false }.Bg(K.Page);
            _glowFrame.Effect = _windowGlow;
            var glowHost = new Grid { IsHitTestVisible = false, CacheMode = new BitmapCache() };
            glowHost.Children.Add(_glowFrame);
            root.Children.Add(glowHost);
            var shell = new Grid();
            shell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(48.0) });
            shell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.0, GridUnitType.Star) });
            shell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _windowFrame.Child = shell;
            root.Children.Add(_windowFrame);
            Content = root;

            BuildTitleBar(shell);

            var body = new Grid { Margin = new Thickness(16.0, 12.0, 16.0, 4.0) };
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.35, GridUnitType.Star) });
            Grid.SetRow(body, 1);
            shell.Children.Add(body);

            var left = new Grid();
            left.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.0, GridUnitType.Star) });
            left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetColumn(left, 0);
            body.Children.Add(left);

            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            scroll.Resources.Add(typeof(ScrollBar), FolderPicker.ScrollBarStyle());
            Chamfer card = Card(Cuts.TopLeft | Cuts.BottomRight);
            card.Margin = new Thickness(0.0, 0.0, 8.0, 0.0);
            var form = new StackPanel { Margin = new Thickness(14.0, 7.0, 14.0, 5.0) };
            card.Child = form;
            scroll.Content = card;
            Grid.SetRow(scroll, 0);
            left.Children.Add(scroll);
            _leftScroll = scroll;
            _form = form;
            // The window or the card's content changed height: roomy or compact spacing (inner
            // scroll viewers, e.g. of the text boxes, report here too and are ignored).
            scroll.ScrollChanged += (s, e) =>
            {
                if (e.OriginalSource == _leftScroll && (e.ViewportHeightChange != 0.0 || e.ExtentHeightChange != 0.0)) FitDensity();
            };

            form.Children.Add(GroupTitle("01", "素材与输出"));
            _srcBox = CompactTextBox();
            _srcBox.Text = _shownSource;
            _srcBox.LostFocus += (s, e) => RefreshCategories();
            ShowPathEnd(_srcBox);
            Grid srcRow = WithBrowse(_srcBox, false);
            srcRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _rulesBtn = MiniButton("规则");
            _rulesBtn.ToolTip = "素材规则：开头不用 / 成片不用";
            _rulesBtn.Click += (s, e) => EditRules();
            Grid.SetColumn(_rulesBtn, 2);
            srcRow.Children.Add(_rulesBtn);
            form.Children.Add(FormRow("素材目录", 60.0, srcRow));
            // 规格统一 status of the chosen folder, filled in by RunUnifyCheck.
            _unifyLine = HintLine();
            _denseHints.Add(_unifyLine);
            form.Children.Add(_unifyLine);
            _outBox = CompactTextBox();
            _outBox.Text = _shownOutput;
            ShowPathEnd(_outBox);
            form.Children.Add(FormRow("输出目录", 60.0, WithBrowse(_outBox, false)));
            _nameBox = CompactTextBox();
            _nameBox.Text = _settings.NameFormat;
            _nameBox.TextChanged += (s, e) => UpdateNamePreview();
            form.Children.Add(FormRow("成片命名", 60.0, WithTokens(_nameBox)));
            _namePreview = HintLine().Fg(K.TextHint);
            _denseHints.Add(_namePreview);
            _namePreview.Visibility = Visibility.Visible;
            form.Children.Add(_namePreview);
            _introBox = CompactTextBox();
            _introBox.Text = _shownIntro;
            _introBox.TextChanged += (s, e) => RefreshCombos();
            ShowPathEnd(_introBox);
            form.Children.Add(FormRow("片头素材", 60.0, WithBrowse(_introBox, true)));

            form.Children.Add(GroupTitle("02", "成片规格"));
            _catCombo = CompactCombo();
            RefreshCategories();
            _catCombo.SelectionChanged += (s, e) =>
            {
                RefreshUnify();
                UpdateNamePreview();
            };
            form.Children.Add(FormRow("素材分类", 60.0, _catCombo));
            _ratioCombo = CompactCombo();
            foreach (string label in RatioLabels) _ratioCombo.Items.Add(label);
            _ratioCombo.SelectedIndex = FindRatioIndex(_settings.Width, _settings.Height);
            _ratioCombo.SelectionChanged += (s, e) =>
            {
                RefreshUnify();
                int r = Math.Max(0, _ratioCombo.SelectedIndex);
                if (_effects != null) _effects.SetRatio(RatioW[r], RatioH[r]);
            };
            form.Children.Add(FormRow("成片比例", 60.0, _ratioCombo));
            _countBox = CompactTextBox();
            _countBox.Text = _settings.Count.ToString();
            _countBox.TextChanged += (s, e) => RefreshCombos();
            _durBox = CompactTextBox();
            _durBox.Text = _settings.DurationSec.ToString();
            _durBox.TextChanged += (s, e) =>
            {
                UpdateNamePreview();
                RefreshCombos();
            };
            UpdateNamePreview();
            form.Children.Add(TwoField("成片数量", _countBox, "目标时长", _durBox));
            _tolBox = CompactTextBox();
            _tolBox.Text = _settings.ToleranceSec.ToString();
            _tolBox.TextChanged += (s, e) => RefreshCombos();
            _parallelBox = CompactTextBox();
            _parallelBox.Text = _settings.ParallelJobs.ToString();
            _parallelBox.ToolTip = "这台电脑建议 " + DeviceCheck.Suggest();
            form.Children.Add(TwoField("浮动秒数", _tolBox, "并行进程", _parallelBox));
            // 已用组合 / 剩余组合 / 总组合, filled in by RunComboCheck: one line in the compact
            // spacing, a block with the four figures when the card has room.
            _comboLine = HintLine();
            _comboLine.Margin = new Thickness(2.0, -1.0, 4.0, 3.0);
            form.Children.Add(_comboLine);
            form.Children.Add(BuildComboBlock());

            form.Children.Add(GroupTitle("03", "性能与操作"));
            _nvencCheck = new CheckBox { IsChecked = _settings.UseNvenc, Style = (Style)Resources["Check"], VerticalAlignment = VerticalAlignment.Center };
            _nvencCheck.Content = new TextBlock { Text = "硬件加速", FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center }.Fg(K.Text);
            _nvencCheck.ToolTip = "自动检测 NVIDIA / Intel / AMD 显卡编码，与 CPU 同时分担规格统一和成片效果；不可用时自动回退 CPU。";
            _nvencCheck.Checked += NvencToggle;
            _nvencCheck.Unchecked += NvencToggle;
            Button openBtn = CompactActionButton("打开输出目录", IconFolder, false);
            openBtn.Click += (s, e) => OpenOutputFolder();
            form.Children.Add(TwoButtons(_nvencCheck, openBtn));
            _preBtn = CompactActionButton("规格统一（增量）", IconRefresh, false);
            _preBtn.Click += PreClick;
            _forceBtn = CompactActionButton("强制重建", IconBolt, true);
            _forceBtn.Click += ForceClick;
            form.Children.Add(TwoButtons(_preBtn, _forceBtn));

            // 加入队列 beside the big button: the settings above become a task of the 任务队列.
            var actions = new Grid { Margin = new Thickness(0.0, 12.0, 8.0, 0.0) };
            actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
            _queueBtn = new Button { Height = 46.0, Width = 112.0, Margin = new Thickness(0.0, 0.0, 8.0, 0.0), FontSize = 13.5, Style = (Style)Resources["SecondaryButton"] };
            var queueContent = new StackPanel { Orientation = Orientation.Horizontal };
            queueContent.Children.Add(MakeIcon(IconQueue, K.Accent, 15.0));
            queueContent.Children.Add(new TextBlock { Text = "加入队列", Margin = new Thickness(7.0, 0.0, 0.0, 0.0), FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            _queueBtn.Content = queueContent;
            _queueBtn.Click += (s, e) => QueueAddClick();
            actions.Children.Add(_queueBtn);
            _mixBtn = new Button { Height = 46.0, Style = (Style)Resources["PrimaryButton"] };
            Grid.SetColumn(_mixBtn, 1);
            actions.Children.Add(_mixBtn);
            var mixContent = new StackPanel { Orientation = Orientation.Horizontal };
            _mixIcon = MakeIcon(IconPlay, (Brush)null, 17.0);
            // The icon takes the button's text colour (dark on the bright start button, red while 停止).
            _mixIcon.SetBinding(Shape.FillProperty, new Binding("Foreground") { Source = _mixBtn });
            mixContent.Children.Add(_mixIcon);
            _mixText = new TextBlock
            {
                Text = "开始快拼",
                FontWeight = FontWeights.Bold,
                FontSize = 15.5,
                Margin = new Thickness(10.0, 0.0, 0.0, 0.0),
                VerticalAlignment = VerticalAlignment.Center
            };
            mixContent.Children.Add(_mixText);
            _mixBtn.Content = mixContent;
            _mixBtn.Click += MixClick;
            Grid.SetRow(actions, 1);
            left.Children.Add(actions);

            // Right: 成片效果, 任务队列 and 运行日志 as tabs of one card. A task switches to the log; a new log
            // line while another page is shown puts a dot on the 运行日志 tab.
            Chamfer side = Card(Cuts.TopRight | Cuts.BottomLeft);
            side.Margin = new Thickness(8.0, 0.0, 0.0, 0.0);
            var sideGrid = new Grid { Margin = new Thickness(16.0, 12.0, 16.0, 16.0) };
            sideGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            sideGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.0, GridUnitType.Star) });
            var tabRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0.0, 0.0, 0.0, 12.0) };
            Style seg = EffectsPanel.SegmentStyle();
            _logDot = new Rectangle { Width = 7.0, Height = 7.0, Visibility = Visibility.Collapsed, Margin = new Thickness(8.0, 0.0, 0.0, 0.0), VerticalAlignment = VerticalAlignment.Center, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new RotateTransform(45.0) }.Fill(K.Warn);
            _queueBadge = new TextBlock { FontSize = 11.0, FontFamily = TechFont, FontWeight = FontWeights.SemiBold, Visibility = Visibility.Collapsed }.Fg(K.OnAccent);
            var queueBadge = new Chamfer { Cut = 4.0, Corners = Cuts.TopRight, BorderThickness = 0.0, Padding = new Thickness(5.0, 0.0, 5.0, 1.0), Margin = new Thickness(7.0, 0.0, 0.0, 0.0), VerticalAlignment = VerticalAlignment.Center, Child = _queueBadge }.Bg(K.Accent);
            queueBadge.SetBinding(UIElement.VisibilityProperty, new Binding("Visibility") { Source = _queueBadge });
            _tabFx = new ToggleButton { Style = seg, IsChecked = true, Content = TabContent(IconSparkle, "成片效果", null) };
            _tabQueue = new ToggleButton { Style = seg, Content = TabContent(IconQueue, "任务队列", queueBadge) };
            _tabLog = new ToggleButton { Style = seg, Content = TabContent(IconTerminal, "运行日志", _logDot) };
            _tabFx.Click += (s, e) => ShowPage(PageEffects);
            _tabQueue.Click += (s, e) => ShowPage(PageQueue);
            _tabLog.Click += (s, e) => ShowPage(PageLog);
            tabRow.Children.Add(_tabFx);
            tabRow.Children.Add(_tabQueue);
            tabRow.Children.Add(_tabLog);
            sideGrid.Children.Add(tabRow);
            _effects = new EffectsPanel(_appDir, CurrentEffects());
            _effects.Changed += EffectsChanged;
            _effects.SubtitleWanted = () => MixEngine.SubtitleModelReady(_appDir) || DownloadSpeechModel();
            Grid.SetRow(_effects, 1);
            sideGrid.Children.Add(_effects);
            _queuePanel = new QueuePanel { Visibility = Visibility.Collapsed, Describe = DescribeTask };
            _queuePanel.Remove += RemoveTask;
            _queuePanel.Open += OpenTaskFolder;
            _queuePanel.ClearFinished += ClearFinishedTasks;
            Grid.SetRow(_queuePanel, 1);
            sideGrid.Children.Add(_queuePanel);
            _logBody = new Chamfer { Cut = 12.0, Corners = Cuts.TopRight | Cuts.BottomLeft, BorderThickness = 1.0, Visibility = Visibility.Collapsed }.Bg(K.LogBg).Bd(K.Line);
            _logBox = new RichTextBox
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0.0),
                FontFamily = new FontFamily("Cascadia Mono, Consolas, Microsoft YaHei UI"),
                FontSize = 12.5,
                Margin = new Thickness(14.0, 12.0, 6.0, 12.0),
                IsReadOnly = true,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                // Long lines wrap.
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            }.Fg(K.LogText);
            // A document of its own (the default one starts with an empty paragraph, a blank first
            // line) with one line height for Latin and Chinese text alike.
            _logBox.Document = new System.Windows.Documents.FlowDocument
            {
                PagePadding = new Thickness(0.0, 0.0, 6.0, 0.0),
                LineHeight = 19.0,
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight
            };
            _logBox.Resources.Add(typeof(ScrollBar), FolderPicker.ScrollBarStyle(true));
            _logBody.Child = _logBox;
            Grid.SetRow(_logBody, 1);
            sideGrid.Children.Add(_logBody);
            side.Child = sideGrid;
            Grid.SetColumn(side, 1);
            body.Children.Add(side);

            var foot = new Grid { Margin = new Thickness(26.0, 6.0, 26.0, 12.0) };
            foot.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
            foot.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _progress = new ProgressBar { Minimum = 0.0, Maximum = 100.0, Value = 0.0, Style = (Style)Resources["Bar"], VerticalAlignment = VerticalAlignment.Center };
            _statusText = new TextBlock
            {
                FontSize = 12.0,
                Margin = new Thickness(16.0, 0.0, 0.0, 0.0),
                VerticalAlignment = VerticalAlignment.Center,
                MaxWidth = 520.0,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Text = "就绪"
            }.Fg(K.TextHint);
            _statusText.SetBinding(FrameworkElement.ToolTipProperty, new Binding("Text") { Source = _statusText });
            Grid.SetColumn(_progress, 0);
            Grid.SetColumn(_statusText, 1);
            foot.Children.Add(_progress);
            foot.Children.Add(_statusText);
            Grid.SetRow(foot, 2);
            shell.Children.Add(foot);
            ApplyGlow();
        }

        private void BuildTitleBar(Grid shell)
        {
            _titleBar = new Border { Margin = new Thickness(1.0, 1.0, 1.0, 0.0) }.Bg(K.Clear);
            var title = new Grid();
            var brand = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(20.0, 0.0, 0.0, 0.0), VerticalAlignment = VerticalAlignment.Center };
            brand.Children.Add(Logo(30.0));
            brand.Children.Add(new TextBlock
            {
                Text = "AXE",
                FontFamily = TechFont,
                FontSize = 21.0,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(11.0, 0.0, 0.0, 1.0),
                VerticalAlignment = VerticalAlignment.Center
            }.Fg(K.Text));
            var badge = new Chamfer { Cut = 5.0, Corners = Cuts.TopLeft, BorderThickness = 1.0, Padding = new Thickness(7.0, 1.0, 7.0, 2.0), Margin = new Thickness(10.0, 1.0, 0.0, 0.0), VerticalAlignment = VerticalAlignment.Center }.Bg(K.AccentSoft).Bd(K.AccentLine);
            badge.Child = new TextBlock { Text = AppInfo.Version, FontFamily = TechFont, FontSize = 11.5 }.Fg(K.Accent);
            brand.Children.Add(badge);
            title.Children.Add(brand);

            var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0.0, 0.0, 12.0, 0.0), VerticalAlignment = VerticalAlignment.Center };
            var status = new Chamfer { Cut = 6.0, BorderThickness = 1.0, Padding = new Thickness(10.0, 4.0, 12.0, 5.0), Margin = new Thickness(0.0, 0.0, 10.0, 0.0), VerticalAlignment = VerticalAlignment.Center }.Bg(K.Panel).Bd(K.Line);
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            _statusDot = new Rectangle
            {
                Width = 7.0,
                Height = 7.0,
                VerticalAlignment = VerticalAlignment.Center,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new RotateTransform(45.0),
                Effect = new DropShadowEffect { BlurRadius = 9.0, ShadowDepth = 0.0 }
            }.Fill(K.Keep);
            _headerStatus = new TextBlock { Text = "就绪", FontSize = 12.0, Margin = new Thickness(9.0, 0.0, 0.0, 0.0), VerticalAlignment = VerticalAlignment.Center }.Fg(K.TextSub);
            sp.Children.Add(_statusDot);
            sp.Children.Add(_headerStatus);
            status.Child = sp;
            right.Children.Add(status);

            _themeBtn = MakeWinButton(IconPalette, false);
            _themeBtn.ToolTip = "背景色";
            _themeBtn.Click += (s, e) => ShowThemes();
            right.Children.Add(_themeBtn);
            right.Children.Add(new Border { Width = 1.0, Height = 16.0, Margin = new Thickness(6.0, 0.0, 6.0, 0.0), VerticalAlignment = VerticalAlignment.Center }.Bg(K.Line));
            Button minBtn = MakeWinButton(IconMin, false);
            minBtn.Click += MinClick;
            _maxBtn = MakeWinButton(IconMax, false);
            _maxBtn.Click += MaxClick;
            Button closeBtn = MakeWinButton(IconClose, true);
            closeBtn.Click += CloseClick;
            right.Children.Add(minBtn);
            right.Children.Add(_maxBtn);
            right.Children.Add(closeBtn);
            title.Children.Add(right);

            // Hairline under the title bar with a lit segment at the left.
            var under = new Grid { Height = 1.0, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(18.0, 0.0, 18.0, 0.0) };
            under.Children.Add(new Border().Bg(K.Line));
            under.Children.Add(new Border { Width = 220.0, HorizontalAlignment = HorizontalAlignment.Left, Height = 1.0 }.Bg(K.Accent));
            title.Children.Add(under);

            _titleBar.Child = title;
            _titleBar.MouseLeftButtonDown += TitleBarDown;
            Grid.SetRow(_titleBar, 0);
            shell.Children.Add(_titleBar);
        }

        // ---------------------------------------------------------------- 背景色
        private DateTime _themeClosedAt;

        private void ShowThemes()
        {
            if (_themePopup != null && _themePopup.IsOpen)
            {
                _themePopup.IsOpen = false;
                return;
            }
            // A click on the button while the panel is open first closes it (StaysOpen = false), then
            // lands here: the button toggles instead of opening the panel again.
            if ((DateTime.UtcNow - _themeClosedAt).TotalMilliseconds < 300) return;
            var panel = new Chamfer { Cut = 12.0, BorderThickness = 1.0, Padding = new Thickness(14.0, 12.0, 14.0, 14.0), AccentSize = 10.0, Margin = new Thickness(0.0, 0.0, 12.0, 12.0) }.Bg(K.Panel).Bd(K.LineHi);
            panel.SetResourceReference(Chamfer.AccentProperty, K.Accent);
            panel.Effect = Shadow(Colors.Black, 18.0, 2.0, 0.45);
            var sp = new StackPanel();
            sp.Children.Add(SectionHead("背景色", 0.0));
            var grid = new UniformGrid { Columns = 3 };
            foreach (Palette p in Palettes) grid.Children.Add(Swatch(p));
            sp.Children.Add(grid);
            panel.Child = sp;
            _themePopup = new Popup
            {
                Child = panel,
                PlacementTarget = _themeBtn,
                Placement = PlacementMode.Bottom,
                HorizontalOffset = -250.0,
                VerticalOffset = 6.0,
                AllowsTransparency = true,
                StaysOpen = false,
                PopupAnimation = PopupAnimation.Fade
            };
            _themePopup.Closed += (s, e) => _themeClosedAt = DateTime.UtcNow;
            _themePopup.IsOpen = true;
        }

        // One scheme as a miniature: its page, a panel and its accent, named underneath.
        private Button Swatch(Palette p)
        {
            bool cur = Current != null && Current.Id == p.Id;
            var face = new Grid { Width = 92.0, Height = 52.0 };
            face.Children.Add(new Chamfer { Cut = 8.0, BorderThickness = cur ? 2.0 : 1.0, Background = SB(p.Page), BorderBrush = SB(cur ? p.Accent : p.LineHi) });
            face.Children.Add(new Chamfer { Cut = 5.0, Corners = Cuts.TopLeft, BorderThickness = 1.0, Background = SB(p.Panel), BorderBrush = SB(p.Line), Margin = new Thickness(9.0, 9.0, 34.0, 9.0) });
            face.Children.Add(new Border { Background = SB(p.Accent), Width = 18.0, Height = 5.0, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0.0, 11.0, 10.0, 0.0) });
            face.Children.Add(new Border { Background = SB(p.TextSub), Width = 18.0, Height = 2.0, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0.0, 21.0, 10.0, 0.0) });
            face.Children.Add(new Border { Background = SB(p.TextSub), Width = 12.0, Height = 2.0, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0.0, 26.0, 16.0, 0.0) });
            if (cur)
            {
                face.Children.Add(new Path { Data = Geo("M3.5,12.5 L9,18 L20.5,6"), Stroke = SB(p.Accent), StrokeThickness = 2.4, Width = 12.0, Height = 12.0, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0.0, 0.0, 9.0, 7.0) });
            }
            var sp = new StackPanel();
            sp.Children.Add(face);
            sp.Children.Add(new TextBlock { Text = p.Name, FontSize = 12.0, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0.0, 5.0, 0.0, 0.0) }.Fg(cur ? K.Accent : K.TextSub));
            var b = new Button { Content = sp, Margin = new Thickness(4.0), Padding = new Thickness(4.0), Style = (Style)Resources["GhostButton"], ToolTip = p.Name };
            b.Click += (s, e) =>
            {
                _themePopup.IsOpen = false;
                SetTheme(p.Id);
            };
            return b;
        }

        private void SetTheme(string id)
        {
            if (Current != null && Current.Id == id) return;
            Apply(id);
            _settings.Theme = Current.Id;
            SaveSettings();
        }

        // Colours held as Color (glows) follow the scheme by hand.
        private void ApplyGlow()
        {
            Palette p = Current ?? Palettes[0];
            if (_windowGlow != null)
            {
                _windowGlow.Color = p.Dark ? p.Accent : Colors.Black;
                _windowGlow.Opacity = 0.22;
            }
            if (_statusDot != null) ((DropShadowEffect)_statusDot.Effect).Color = ColorOf(_statusKey);
        }

        private static StackPanel TabContent(string icon, string text, UIElement extra)
        {
            var p = new StackPanel { Orientation = Orientation.Horizontal };
            p.Children.Add(MakeIcon(icon, K.Accent, 14.0));
            p.Children.Add(new TextBlock { Text = text, Margin = new Thickness(7.0, 0.0, 0.0, 0.0), VerticalAlignment = VerticalAlignment.Center });
            if (extra != null) p.Children.Add(extra);
            return p;
        }

        // Group heading of the left card: its number in the tech face, the title, a hairline to the edge.
        private Grid GroupTitle(string number, string text)
        {
            // (v2.7.0: a pixel less above and below, so the 组合 line fits at the smallest window size.)
            var g = new Grid { Margin = new Thickness(0.0, 1.0, 0.0, 3.0) };
            _denseTitles.Add(g);
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
            var num = new Chamfer { Cut = 4.0, Corners = Cuts.TopRight, BorderThickness = 0.0, Padding = new Thickness(5.0, 0.0, 6.0, 0.0), VerticalAlignment = VerticalAlignment.Center }.Bg(K.AccentSoft);
            num.Child = new TextBlock { Text = number, FontFamily = TechFont, FontSize = 11.0, FontWeight = FontWeights.SemiBold }.Fg(K.Accent);
            g.Children.Add(num);
            var t = new TextBlock
            {
                Text = text,
                FontSize = 12.5,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(8.0, 0.0, 10.0, 0.0),
                VerticalAlignment = VerticalAlignment.Center
            }.Fg(K.Text);
            Grid.SetColumn(t, 1);
            g.Children.Add(t);
            var line = new Border { Height = 1.0, VerticalAlignment = VerticalAlignment.Center }.Bg(K.Line);
            Grid.SetColumn(line, 2);
            g.Children.Add(line);
            return g;
        }

        private TextBox CompactTextBox()
        {
            var box = new TextBox
            {
                Height = 28.0,
                VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(9.0, 0.0, 9.0, 0.0),
                Style = (Style)Resources["InputBox"]
            };
            _denseInputs.Add(box);
            return box;
        }

        private ComboBox CompactCombo()
        {
            var combo = new ComboBox { Height = 28.0, Style = (Style)Resources["Combo"] };
            _denseInputs.Add(combo);
            return combo;
        }

        // A long path shows its end (the folder it is about) while the box is not being edited; the
        // whole path is in the tooltip (v2.7.1: the start "C:\Users\..." used to fill the box).
        private static void ShowPathEnd(TextBox box)
        {
            Action toEnd = () =>
            {
                if (box.IsKeyboardFocusWithin) return;
                box.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
                {
                    if (box.IsKeyboardFocusWithin) return;
                    box.CaretIndex = box.Text.Length;
                    box.ScrollToHorizontalOffset(Math.Max(0.0, box.ExtentWidth - box.ViewportWidth));
                }));
            };
            box.ToolTip = box.Text.Length > 0 ? box.Text : null;
            box.Loaded += (s, e) => toEnd();
            box.LostKeyboardFocus += (s, e) => toEnd();
            box.SizeChanged += (s, e) => toEnd();
            box.TextChanged += (s, e) =>
            {
                box.ToolTip = box.Text.Length > 0 ? box.Text : null;
                toEnd();
            };
        }

        // v2.7.1: the left card has two spacings. With height to spare (the 1220x780 window) the rows
        // get more air and the 组合 figures a block of their own; on a small screen (the 1000x640
        // minimum, a 1366x768 laptop) everything stays as compact as before.
        private readonly List<FrameworkElement> _denseInputs = new List<FrameworkElement>();
        private readonly List<FrameworkElement> _denseRows = new List<FrameworkElement>();
        private readonly List<FrameworkElement> _denseTitles = new List<FrameworkElement>();
        private readonly List<FrameworkElement> _denseHints = new List<FrameworkElement>();
        private readonly List<FrameworkElement> _denseActions = new List<FrameworkElement>();
        private ScrollViewer _leftScroll;
        private StackPanel _form;
        private bool _roomy;
        private bool _fitting;

        private void SetDensity(bool roomy)
        {
            _roomy = roomy;
            foreach (var c in _denseInputs) c.Height = roomy ? 30.0 : 28.0;
            foreach (var r in _denseRows) r.Margin = new Thickness(0.0, 0.0, 0.0, roomy ? 8.0 : 4.0);
            for (int i = 0; i < _denseTitles.Count; i++)
            {
                _denseTitles[i].Margin = roomy ? new Thickness(0.0, i == 0 ? 3.0 : 10.0, 0.0, 6.0) : new Thickness(0.0, 1.0, 0.0, 3.0);
            }
            // A hint stays close to the row it belongs to.
            foreach (var h in _denseHints) h.Margin = roomy ? new Thickness(61.0, -4.0, 4.0, 6.0) : new Thickness(61.0, -1.0, 4.0, 5.0);
            foreach (var b in _denseActions) b.Height = roomy ? 34.0 : 32.0;
            ShowComboView();
        }

        // Roomy when the whole card fits the window that way, else compact. Measured whenever the
        // window or the card's content changes height, so it holds for whatever lines are shown.
        private void FitDensity()
        {
            if (_fitting || _leftScroll == null || _form == null) return;
            double room = _leftScroll.ViewportHeight;
            // The card's right margin (8) and its 1 px border on both sides are outside the form.
            double width = _leftScroll.ViewportWidth - 8.0 - 2.0;
            if (!(room > 0.0) || !(width > 0.0)) return;
            _fitting = true;
            try
            {
                bool was = _roomy;
                if (!was) SetDensity(true);
                // A real measure: only the rows were changed above, so the form itself still counts
                // as measured and would hand back its old size for the same width (that made the
                // window flip between the two spacings forever at 1000x640).
                _form.InvalidateMeasure();
                _form.Measure(new Size(width, double.PositiveInfinity));
                bool roomy = _form.DesiredSize.Height + 2.0 <= room;
                if (roomy != was)
                {
                    // Should the layout ever not settle, stay compact rather than flip back and forth.
                    DateTime now = DateTime.UtcNow;
                    if (now - _flipStart > TimeSpan.FromSeconds(1.0))
                    {
                        _flipStart = now;
                        _flips = 0;
                    }
                    if (++_flips > 6) roomy = false;
                }
                if (!roomy) SetDensity(false);
            }
            finally
            {
                _fitting = false;
            }
        }

        private DateTime _flipStart;
        private int _flips;

        private Chamfer _comboBlock;
        private Grid _comboNumbers;
        private TextBlock[] _comboFigures;
        private TextBlock _comboMessage;
        private ComboView _comboView;

        // 素材 · 已用组合 · 剩余组合 · 总组合 as four figures (roomy spacing); a warning such as 总时长不够
        // takes the block's place in the same height.
        private Chamfer BuildComboBlock()
        {
            _comboBlock = new Chamfer
            {
                Cut = 8.0,
                Corners = Cuts.TopLeft | Cuts.BottomRight,
                BorderThickness = 1.0,
                Padding = new Thickness(4.0, 6.0, 4.0, 7.0),
                Margin = new Thickness(0.0, 0.0, 0.0, 4.0),
                Visibility = Visibility.Collapsed
            }.Bg(K.Panel2).Bd(K.Line);
            var host = new Grid();
            _comboNumbers = new Grid();
            string[] labels = { "素材", "已用组合", "剩余组合", "总组合" };
            _comboFigures = new TextBlock[labels.Length];
            for (int i = 0; i < labels.Length; i++)
            {
                _comboNumbers.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
                if (i > 0)
                {
                    var line = new Border { Width = 1.0, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0.0, 4.0, 0.0, 4.0) }.Bg(K.Line);
                    Grid.SetColumn(line, i);
                    _comboNumbers.Children.Add(line);
                }
                var cell = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
                cell.Children.Add(new TextBlock { Text = labels[i], FontSize = 11.0, HorizontalAlignment = HorizontalAlignment.Center }.Fg(K.TextHint));
                _comboFigures[i] = new TextBlock { FontSize = 16.0, FontFamily = TechFont, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0.0, 1.0, 0.0, 0.0) }.Fg(K.Text);
                cell.Children.Add(_comboFigures[i]);
                Grid.SetColumn(cell, i);
                _comboNumbers.Children.Add(cell);
            }
            host.Children.Add(_comboNumbers);
            _comboMessage = new TextBlock
            {
                FontSize = 12.5,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(8.0, 0.0, 8.0, 0.0),
                Visibility = Visibility.Collapsed
            };
            host.Children.Add(_comboMessage);
            _comboBlock.Child = host;
            return _comboBlock;
        }

        // The newest count (null: nothing to show, e.g. no such folder or a length being typed).
        private void SetComboView(ComboView v)
        {
            _comboView = v;
            bool warn = v != null && v.Warn;
            _comboLine.Text = v != null ? v.Text ?? "" : "";
            _comboLine.ToolTip = v != null ? v.Tip : null;
            _comboLine.Fg(warn ? K.Warn : K.TextHint);
            _comboBlock.ToolTip = v != null ? v.Tip : null;
            if (v != null && v.Figures != null)
            {
                for (int i = 0; i < _comboFigures.Length; i++) _comboFigures[i].Text = v.Figures[i];
                // 剩余组合 is the figure to watch: lit, the warning colour when fewer than the videos asked for.
                _comboFigures[2].Fg(warn ? K.Warn : K.Keep);
                _comboNumbers.Visibility = Visibility.Visible;
                _comboMessage.Visibility = Visibility.Collapsed;
            }
            else if (v != null)
            {
                _comboMessage.Text = v.Text;
                _comboMessage.Fg(warn ? K.Warn : K.TextSub);
                _comboNumbers.Visibility = Visibility.Hidden;
                _comboMessage.Visibility = Visibility.Visible;
            }
            ShowComboView();
        }

        private void ShowComboView()
        {
            if (_comboLine == null || _comboBlock == null) return;
            bool any = _comboView != null && !string.IsNullOrEmpty(_comboView.Text);
            _comboLine.Visibility = any && !_roomy ? Visibility.Visible : Visibility.Collapsed;
            _comboBlock.Visibility = any && _roomy ? Visibility.Visible : Visibility.Collapsed;
        }

        private Grid FormRow(string label, double labelWidth, UIElement control)
        {
            var g = new Grid { Margin = new Thickness(0.0, 0.0, 0.0, 4.0) };
            _denseRows.Add(g);
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(labelWidth) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
            var tb = new TextBlock
            {
                Text = label,
                FontSize = 12.5,
                TextAlignment = TextAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0.0, 0.0, 9.0, 0.0)
            }.Fg(K.TextSub);
            Grid.SetColumn(tb, 0);
            Grid.SetColumn(control, 1);
            g.Children.Add(tb);
            g.Children.Add(control);
            return g;
        }

        private Grid WithBrowse(TextBox box, bool isFile)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(box, 0);
            g.Children.Add(box);
            Button browse = MiniButton("浏览");
            if (isFile) browse.Click += (s, e) => PickFile(box);
            else browse.Click += (s, e) => PickFolder(box);
            Grid.SetColumn(browse, 1);
            g.Children.Add(browse);
            if (isFile)
            {
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                Button clear = MiniButton("清除");
                clear.Click += (s, e) => box.Text = "";
                Grid.SetColumn(clear, 2);
                g.Children.Add(clear);
            }
            return g;
        }

        // Small status/preview text under a form row, aligned with the input column.
        private TextBlock HintLine()
        {
            return new TextBlock
            {
                FontSize = 11.5,
                Margin = new Thickness(61.0, -1.0, 4.0, 5.0),
                TextTrimming = TextTrimming.CharacterEllipsis,
                Visibility = Visibility.Collapsed
            };
        }

        // Template box plus a 变量 button whose menu inserts a token at the caret.
        private Grid WithTokens(TextBox box)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(box, 0);
            g.Children.Add(box);
            Button tokens = MiniButton("变量");
            var menu = new ContextMenu { Style = (Style)Resources["Menu"], PlacementTarget = tokens, Placement = PlacementMode.Bottom };
            foreach (string[] t in OutputNaming.Tokens)
            {
                string token = t[0];
                var item = new MenuItem { Header = MenuRow(t[1], token, "例：" + OutputNaming.Example(t)), Style = (Style)Resources["MenuItem"] };
                item.Click += (s, e) => InsertToken(token);
                menu.Items.Add(item);
            }
            var reset = new MenuItem { Header = MenuRow("恢复默认", OutputNaming.DefaultFormat, ""), Style = (Style)Resources["MenuItem"] };
            reset.Click += (s, e) => box.Text = OutputNaming.DefaultFormat;
            menu.Items.Add(reset);
            tokens.Click += (s, e) => menu.IsOpen = true;
            Grid.SetColumn(tokens, 1);
            g.Children.Add(tokens);
            return g;
        }

        private static Grid MenuRow(string label, string token, string example)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(78.0) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96.0) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var a = new TextBlock { Text = label, FontSize = 12.5 }.Fg(K.Text);
            var b = new TextBlock { Text = token, FontSize = 12.5, FontFamily = TechFont }.Fg(K.Accent);
            var c = new TextBlock { Text = example, FontSize = 12.0 }.Fg(K.TextHint);
            Grid.SetColumn(b, 1);
            Grid.SetColumn(c, 2);
            g.Children.Add(a);
            g.Children.Add(b);
            g.Children.Add(c);
            return g;
        }

        private Button MiniButton(string text)
        {
            var b = new Button
            {
                Content = text,
                Width = 52.0,
                Height = 28.0,
                Margin = new Thickness(8.0, 0.0, 0.0, 0.0),
                FontSize = 12.5,
                Style = (Style)Resources["SecondaryButton"]
            };
            _denseInputs.Add(b);
            return b;
        }

        private Grid TwoField(string l1, TextBox b1, string l2, TextBox b2)
        {
            var outer = new Grid { Margin = new Thickness(0.0, 0.0, 0.0, 4.0) };
            _denseRows.Add(outer);
            outer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
            outer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
            var g1 = new Grid { Margin = new Thickness(0.0, 0.0, 4.0, 0.0) };
            // The left label takes the label column of the rows above, so the boxes line up (v2.7.1).
            g1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60.0) });
            g1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
            TextBlock t1 = MiniLabel(l1);
            t1.TextAlignment = TextAlignment.Right;
            t1.Margin = new Thickness(0.0, 0.0, 9.0, 0.0);
            b1.Margin = new Thickness(0.0);
            Grid.SetColumn(t1, 0);
            Grid.SetColumn(b1, 1);
            g1.Children.Add(t1);
            g1.Children.Add(b1);
            Grid.SetColumn(g1, 0);
            outer.Children.Add(g1);
            var g2 = new Grid { Margin = new Thickness(4.0, 0.0, 0.0, 0.0) };
            g2.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
            TextBlock t2 = MiniLabel(l2);
            t2.Margin = new Thickness(0.0, 0.0, 9.0, 0.0);
            b2.Margin = new Thickness(0.0);
            Grid.SetColumn(t2, 0);
            Grid.SetColumn(b2, 1);
            g2.Children.Add(t2);
            g2.Children.Add(b2);
            Grid.SetColumn(g2, 1);
            outer.Children.Add(g2);
            return outer;
        }

        private TextBlock MiniLabel(string text)
        {
            return new TextBlock { Text = text, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center }.Fg(K.TextSub);
        }

        private Grid TwoButtons(FrameworkElement a, FrameworkElement b)
        {
            var g = new Grid { Margin = new Thickness(0.0, 0.0, 0.0, 4.0) };
            _denseRows.Add(g);
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
            a.Margin = new Thickness(0.0, 0.0, 4.0, 0.0);
            b.Margin = new Thickness(4.0, 0.0, 0.0, 0.0);
            if (a is Button) _denseActions.Add(a);
            if (b is Button) _denseActions.Add(b);
            Grid.SetColumn(a, 0);
            Grid.SetColumn(b, 1);
            g.Children.Add(a);
            g.Children.Add(b);
            return g;
        }

        private Button CompactActionButton(string text, string iconData, bool force)
        {
            var btn = new Button
            {
                Height = 32.0,
                Margin = new Thickness(0.0, 0.0, 0.0, 4.0),
                FontSize = 12.5,
                Style = force ? MakeWarnButtonStyle() : (Style)Resources["SecondaryButton"]
            };
            var content = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            content.Children.Add(MakeIcon(iconData, force ? K.Warn : K.Accent, 15.0));
            content.Children.Add(new TextBlock { Text = text, Margin = new Thickness(7.0, 0.0, 0.0, 0.0), VerticalAlignment = VerticalAlignment.Center });
            btn.Content = content;
            return btn;
        }

        // Title-bar button; the icon geometry lives in Tag so it can be swapped (maximize/restore).
        internal static Button MakeWinButton(string iconData, bool isClose)
        {
            var wbd = new FrameworkElementFactory(typeof(Chamfer), "wbd");
            wbd.SetResourceReference(Chamfer.BackgroundProperty, K.Clear);
            wbd.SetValue(Chamfer.BorderThicknessProperty, 0.0);
            wbd.SetValue(Chamfer.CutProperty, 7.0);
            var icon = new FrameworkElementFactory(typeof(Path), "wicon");
            icon.SetValue(Path.DataProperty, new TemplateBindingExtension(FrameworkElement.TagProperty));
            icon.SetResourceReference(Shape.StrokeProperty, K.TextSub);
            icon.SetValue(Shape.StrokeThicknessProperty, 1.5);
            icon.SetValue(FrameworkElement.WidthProperty, 13.0);
            icon.SetValue(FrameworkElement.HeightProperty, 13.0);
            icon.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            icon.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            icon.SetValue(Shape.StretchProperty, Stretch.Uniform);
            wbd.AppendChild(icon);
            var tpl = new ControlTemplate(typeof(Button)) { VisualTree = wbd };
            var over = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            over.Setters.Add(DynSetter(Chamfer.BackgroundProperty, isClose ? K.Danger : K.Panel3, "wbd"));
            over.Setters.Add(DynSetter(Shape.StrokeProperty, isClose ? K.OnAccent : K.Accent, "wicon"));
            tpl.Triggers.Add(over);
            return new Button
            {
                Width = 42.0,
                Height = 30.0,
                Template = tpl,
                Focusable = false,
                Cursor = System.Windows.Input.Cursors.Arrow,
                Tag = Geo(iconData)
            };
        }
    }
}
