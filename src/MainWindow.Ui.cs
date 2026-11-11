using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Path = System.Windows.Shapes.Path;
using static VideoMixApp.Theme;

namespace VideoMixApp
{
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
            Resources["Bar"] = MakeProgressBarStyle();
        }

        internal static ControlTemplate ButtonTpl(Brush normal, Brush hover, Brush pressed, Brush disabled, Brush borderNormal, Brush borderHover, CornerRadius r)
        {
            var bd = new FrameworkElementFactory(typeof(Border), "bd");
            bd.SetValue(Border.BackgroundProperty, normal);
            bd.SetValue(Border.CornerRadiusProperty, r);
            bd.SetValue(UIElement.SnapsToDevicePixelsProperty, true);
            if (borderNormal != null)
            {
                bd.SetValue(Border.BorderBrushProperty, borderNormal);
                bd.SetValue(Border.BorderThicknessProperty, new Thickness(1.0));
            }
            else
            {
                bd.SetValue(Border.BorderThicknessProperty, new Thickness(0.0));
            }
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            cp.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            cp.SetValue(FrameworkElement.MarginProperty, new TemplateBindingExtension(Control.PaddingProperty));
            bd.AppendChild(cp);
            var tpl = new ControlTemplate(typeof(Button));
            tpl.VisualTree = bd;
            var over = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            over.Setters.Add(new Setter(Border.BackgroundProperty, hover, "bd"));
            if (borderHover != null) over.Setters.Add(new Setter(Border.BorderBrushProperty, borderHover, "bd"));
            tpl.Triggers.Add(over);
            var press = new Trigger { Property = ButtonBase.IsPressedProperty, Value = true };
            press.Setters.Add(new Setter(Border.BackgroundProperty, pressed, "bd"));
            tpl.Triggers.Add(press);
            var off = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            off.Setters.Add(new Setter(Border.BackgroundProperty, disabled, "bd"));
            off.Setters.Add(new Setter(UIElement.OpacityProperty, 0.55));
            tpl.Triggers.Add(off);
            return tpl;
        }

        internal static Style MakePrimaryButtonStyle()
        {
            Brush normal = LG(CAccentLight, CAccent, new Point(0, 0), new Point(0, 1));
            Brush hover = LG(Color.FromRgb(255, 176, 96), Color.FromRgb(255, 148, 46), new Point(0, 0), new Point(0, 1));
            Brush pressed = SB(CAccentDeep);
            Brush disabled = SB(Color.FromRgb(214, 220, 228));
            var s = new Style(typeof(Button));
            s.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
            s.Setters.Add(new Setter(FrameworkElement.CursorProperty, System.Windows.Input.Cursors.Hand));
            s.Setters.Add(new Setter(Control.TemplateProperty, ButtonTpl(normal, hover, pressed, disabled, null, null, new CornerRadius(10.0))));
            return s;
        }

        // The big button while a task runs (停止).
        private static Style MakeStopButtonStyle()
        {
            Brush normal = LG(Color.FromRgb(240, 106, 98), Color.FromRgb(222, 70, 70), new Point(0, 0), new Point(0, 1));
            Brush hover = LG(Color.FromRgb(246, 124, 116), Color.FromRgb(232, 86, 86), new Point(0, 0), new Point(0, 1));
            Brush pressed = SB(Color.FromRgb(196, 54, 54));
            Brush disabled = SB(Color.FromRgb(214, 220, 228));
            var s = new Style(typeof(Button));
            s.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.White));
            s.Setters.Add(new Setter(FrameworkElement.CursorProperty, System.Windows.Input.Cursors.Hand));
            s.Setters.Add(new Setter(Control.TemplateProperty, ButtonTpl(normal, hover, pressed, disabled, null, null, new CornerRadius(10.0))));
            return s;
        }

        // Rounded drop-down used by the 变量 button.
        private static Style MakeMenuStyle()
        {
            const string xaml =
                "<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='ContextMenu'>" +
                "<Setter Property='OverridesDefaultStyle' Value='True'/><Setter Property='HasDropShadow' Value='False'/>" +
                "<Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ContextMenu'>" +
                "<Border Background='White' BorderBrush='#E5EBF3' BorderThickness='1' CornerRadius='10' Padding='5' Margin='0,4,10,10'>" +
                "<Border.Effect><DropShadowEffect Color='#96A5B9' BlurRadius='14' ShadowDepth='3' Opacity='0.5'/></Border.Effect>" +
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
                "<Border x:Name='bd' Background='Transparent' CornerRadius='6' Padding='10,6,12,6'>" +
                "<ContentPresenter ContentSource='Header' VerticalAlignment='Center'/></Border>" +
                "<ControlTemplate.Triggers><Trigger Property='IsHighlighted' Value='True'>" +
                "<Setter TargetName='bd' Property='Background' Value='#1C2E6CA8'/></Trigger></ControlTemplate.Triggers>" +
                "</ControlTemplate></Setter.Value></Setter></Style>";
            return (Style)System.Windows.Markup.XamlReader.Parse(xaml);
        }

        internal static Style MakeSecondaryButtonStyle()
        {
            var s = new Style(typeof(Button));
            s.Setters.Add(new Setter(Control.ForegroundProperty, SB(CPrimaryDeep)));
            s.Setters.Add(new Setter(FrameworkElement.CursorProperty, System.Windows.Input.Cursors.Hand));
            s.Setters.Add(new Setter(Control.TemplateProperty, ButtonTpl(
                SB(Color.FromRgb(238, 243, 250)), SB(Color.FromRgb(226, 236, 248)), SB(Color.FromRgb(212, 226, 242)), SB(Color.FromRgb(244, 246, 249)),
                SB(CBorder), SB(CPrimary), new CornerRadius(10.0))));
            return s;
        }

        private Style MakeGhostButtonStyle()
        {
            var s = new Style(typeof(Button));
            s.Setters.Add(new Setter(FrameworkElement.CursorProperty, System.Windows.Input.Cursors.Hand));
            s.Setters.Add(new Setter(Control.TemplateProperty, ButtonTpl(
                Brushes.Transparent, SB(22, 255, 255, 255), SB(38, 255, 255, 255), Brushes.Transparent, null, null, new CornerRadius(10.0))));
            return s;
        }

        internal static Style MakeTextBoxStyle()
        {
            var bd = new FrameworkElementFactory(typeof(Border), "bd");
            bd.SetValue(Border.BackgroundProperty, SB(CInputBg));
            bd.SetValue(Border.BorderBrushProperty, SB(CBorder));
            bd.SetValue(Border.BorderThicknessProperty, new Thickness(1.0));
            bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(10.0));
            bd.SetValue(UIElement.SnapsToDevicePixelsProperty, true);
            var host = new FrameworkElementFactory(typeof(ScrollViewer), "PART_ContentHost");
            host.SetValue(FrameworkElement.MarginProperty, new TemplateBindingExtension(Control.PaddingProperty));
            host.SetValue(Control.BackgroundProperty, Brushes.Transparent);
            host.SetValue(UIElement.FocusableProperty, false);
            host.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Hidden);
            host.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Hidden);
            bd.AppendChild(host);
            var tpl = new ControlTemplate(typeof(TextBox));
            tpl.VisualTree = bd;
            var focus = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true };
            focus.Setters.Add(new Setter(Border.BorderBrushProperty, SB(CPrimary), "bd"));
            focus.Setters.Add(new Setter(Border.BackgroundProperty, Brushes.White, "bd"));
            tpl.Triggers.Add(focus);
            var off = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            off.Setters.Add(new Setter(Border.BackgroundProperty, SB(Color.FromRgb(240, 242, 246)), "bd"));
            tpl.Triggers.Add(off);
            var s = new Style(typeof(TextBox));
            s.Setters.Add(new Setter(Control.ForegroundProperty, SB(CTextMain)));
            s.Setters.Add(new Setter(Control.FontSizeProperty, 13.0));
            s.Setters.Add(new Setter(Control.TemplateProperty, tpl));
            return s;
        }

        private Style MakeComboBoxStyle()
        {
            var bd = new FrameworkElementFactory(typeof(Border), "bd");
            bd.SetValue(Border.BackgroundProperty, SB(CInputBg));
            bd.SetValue(Border.BorderBrushProperty, SB(CBorder));
            bd.SetValue(Border.BorderThicknessProperty, new Thickness(1.0));
            bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(10.0));
            bd.SetValue(UIElement.SnapsToDevicePixelsProperty, true);
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
            var openBinding = new Binding("IsDropDownOpen");
            openBinding.RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent);
            openBinding.Mode = BindingMode.TwoWay;
            toggle.SetBinding(ToggleButton.IsCheckedProperty, openBinding);
            grid.AppendChild(toggle);

            var content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(FrameworkElement.MarginProperty, new Thickness(10.0, 0.0, 6.0, 0.0));
            content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            content.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
            content.SetValue(UIElement.IsHitTestVisibleProperty, false);
            var boxBinding = new Binding("SelectionBoxItem");
            boxBinding.RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent);
            content.SetBinding(ContentPresenter.ContentProperty, boxBinding);
            grid.AppendChild(content);

            // Drop-down arrow sits in the Auto column on the right edge.
            var arrowBox = new FrameworkElementFactory(typeof(Border));
            arrowBox.SetValue(Grid.ColumnProperty, 1);
            arrowBox.SetValue(FrameworkElement.WidthProperty, 26.0);
            arrowBox.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            arrowBox.SetValue(UIElement.IsHitTestVisibleProperty, false);
            var arrow = new FrameworkElementFactory(typeof(Path));
            arrow.SetValue(Path.DataProperty, Geo("M7,10 L12,15 L17,10 Z"));
            arrow.SetValue(Shape.FillProperty, SB(CTextHint));
            arrow.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            arrow.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            arrowBox.AppendChild(arrow);
            grid.AppendChild(arrowBox);
            bd.AppendChild(grid);

            var popup = new FrameworkElementFactory(typeof(Popup), "PART_Popup");
            popup.SetValue(Popup.PlacementProperty, PlacementMode.Bottom);
            popup.SetValue(Popup.AllowsTransparencyProperty, true);
            popup.SetValue(FrameworkElement.MarginProperty, new Thickness(0.0, 4.0, 0.0, 0.0));
            var popupBinding = new Binding("IsDropDownOpen");
            popupBinding.RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent);
            popupBinding.Mode = BindingMode.TwoWay;
            popup.SetBinding(Popup.IsOpenProperty, popupBinding);
            var minWidth = new Binding("ActualWidth");
            minWidth.RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent);
            var popupBd = new FrameworkElementFactory(typeof(Border));
            popupBd.SetValue(Border.BackgroundProperty, Brushes.White);
            popupBd.SetValue(Border.CornerRadiusProperty, new CornerRadius(10.0));
            popupBd.SetValue(Border.BorderBrushProperty, SB(CBorder));
            popupBd.SetValue(Border.BorderThicknessProperty, new Thickness(1.0));
            popupBd.SetValue(UIElement.EffectProperty, Shadow(Color.FromRgb(150, 165, 185), 14.0, 3.0, 0.5));
            popupBd.SetBinding(FrameworkElement.MinWidthProperty, minWidth);
            var scroller = new FrameworkElementFactory(typeof(ScrollViewer));
            scroller.SetValue(FrameworkElement.MaxHeightProperty, 320.0);
            scroller.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
            var items = new FrameworkElementFactory(typeof(ItemsPresenter));
            items.SetValue(FrameworkElement.MarginProperty, new Thickness(5.0));
            scroller.AppendChild(items);
            popupBd.AppendChild(scroller);
            popup.AppendChild(popupBd);

            var root = new FrameworkElementFactory(typeof(Grid));
            root.AppendChild(bd);
            root.AppendChild(popup);
            var tpl = new ControlTemplate(typeof(ComboBox));
            tpl.VisualTree = root;
            var focus = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true };
            focus.Setters.Add(new Setter(Border.BorderBrushProperty, SB(CPrimary), "bd"));
            tpl.Triggers.Add(focus);

            var ib = new FrameworkElementFactory(typeof(Border), "ib");
            ib.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            ib.SetValue(Border.CornerRadiusProperty, new CornerRadius(6.0));
            ib.SetValue(Border.PaddingProperty, new Thickness(9.0, 7.0, 9.0, 7.0));
            ib.SetValue(FrameworkElement.MarginProperty, new Thickness(2.0, 1.0, 2.0, 1.0));
            var icp = new FrameworkElementFactory(typeof(ContentPresenter));
            icp.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            ib.AppendChild(icp);
            var itemTpl = new ControlTemplate(typeof(ComboBoxItem));
            itemTpl.VisualTree = ib;
            var itemOver = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            itemOver.Setters.Add(new Setter(Border.BackgroundProperty, SB(28, 46, 108, 168), "ib"));
            itemTpl.Triggers.Add(itemOver);
            var itemSel = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
            itemSel.Setters.Add(new Setter(Border.BackgroundProperty, SB(40, 46, 108, 168), "ib"));
            itemTpl.Triggers.Add(itemSel);
            var itemStyle = new Style(typeof(ComboBoxItem));
            itemStyle.Setters.Add(new Setter(Control.ForegroundProperty, SB(CTextMain)));
            itemStyle.Setters.Add(new Setter(Control.FontSizeProperty, 13.0));
            itemStyle.Setters.Add(new Setter(Control.TemplateProperty, itemTpl));

            var s = new Style(typeof(ComboBox));
            s.Setters.Add(new Setter(Control.ForegroundProperty, SB(CTextMain)));
            s.Setters.Add(new Setter(FrameworkElement.HeightProperty, 34.0));
            s.Setters.Add(new Setter(Control.TemplateProperty, tpl));
            s.Setters.Add(new Setter(ItemsControl.ItemContainerStyleProperty, itemStyle));
            return s;
        }

        internal static Style MakeCheckBoxStyle()
        {
            var panel = new FrameworkElementFactory(typeof(StackPanel));
            panel.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
            panel.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            var box = new FrameworkElementFactory(typeof(Border), "box");
            box.SetValue(FrameworkElement.WidthProperty, 20.0);
            box.SetValue(FrameworkElement.HeightProperty, 20.0);
            box.SetValue(Border.BackgroundProperty, Brushes.White);
            box.SetValue(Border.BorderBrushProperty, SB(CBorder));
            box.SetValue(Border.BorderThicknessProperty, new Thickness(1.5));
            box.SetValue(Border.CornerRadiusProperty, new CornerRadius(5.0));
            box.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            var chk = new FrameworkElementFactory(typeof(Path), "chk");
            chk.SetValue(Path.DataProperty, Geo("M3.5,12.5 L8.5,17.5 L20.5,5.5"));
            chk.SetValue(Shape.StrokeProperty, Brushes.White);
            chk.SetValue(Shape.StrokeThicknessProperty, 2.4);
            chk.SetValue(Shape.StrokeStartLineCapProperty, PenLineCap.Round);
            chk.SetValue(Shape.StrokeEndLineCapProperty, PenLineCap.Round);
            chk.SetValue(Shape.StretchProperty, Stretch.None);
            chk.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            chk.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            chk.SetValue(UIElement.VisibilityProperty, Visibility.Collapsed);
            box.AppendChild(chk);
            panel.AppendChild(box);
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(FrameworkElement.MarginProperty, new Thickness(10.0, 0.0, 0.0, 0.0));
            cp.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            panel.AppendChild(cp);
            var tpl = new ControlTemplate(typeof(CheckBox));
            tpl.VisualTree = panel;
            var on = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true };
            on.Setters.Add(new Setter(Border.BackgroundProperty, SB(CPrimary), "box"));
            on.Setters.Add(new Setter(Border.BorderBrushProperty, SB(CPrimary), "box"));
            on.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible, "chk"));
            tpl.Triggers.Add(on);
            var over = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            over.Setters.Add(new Setter(Border.BorderBrushProperty, SB(CPrimary), "box"));
            tpl.Triggers.Add(over);
            var s = new Style(typeof(CheckBox));
            s.Setters.Add(new Setter(FrameworkElement.CursorProperty, System.Windows.Input.Cursors.Hand));
            s.Setters.Add(new Setter(Control.ForegroundProperty, SB(CTextMain)));
            s.Setters.Add(new Setter(Control.TemplateProperty, tpl));
            return s;
        }

        internal static Style MakeProgressBarStyle()
        {
            var bd = new FrameworkElementFactory(typeof(Border));
            bd.SetValue(Border.BackgroundProperty, SB(Color.FromRgb(232, 237, 244)));
            bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(6.0));
            bd.SetValue(Border.BorderThicknessProperty, new Thickness(0.0));
            var track = new FrameworkElementFactory(typeof(Grid), "PART_Track");
            var ind = new FrameworkElementFactory(typeof(Border), "PART_Indicator");
            ind.SetValue(Border.BackgroundProperty, LG(CPrimary, CPrimaryDeep, new Point(0, 0), new Point(1, 0)));
            ind.SetValue(Border.CornerRadiusProperty, new CornerRadius(6.0));
            ind.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
            track.AppendChild(ind);
            bd.AppendChild(track);
            var tpl = new ControlTemplate(typeof(ProgressBar));
            tpl.VisualTree = bd;
            var s = new Style(typeof(ProgressBar));
            s.Setters.Add(new Setter(FrameworkElement.HeightProperty, 12.0));
            s.Setters.Add(new Setter(Control.TemplateProperty, tpl));
            return s;
        }

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
            FontFamily = new FontFamily("Microsoft YaHei");
            UseLayoutRounding = true;
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
            var root = new Grid { Background = SB(1, 255, 255, 255) };
            _windowFrame = new Border
            {
                Background = SB(CPageBg),
                CornerRadius = new CornerRadius(18.0),
                Margin = new Thickness(10.0),
                BorderBrush = SB(CBorder),
                BorderThickness = new Thickness(1.0),
                Effect = Shadow(Color.FromRgb(110, 126, 150), 24.0, 0.0, 0.35)
            };
            var shell = new Grid();
            shell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(48.0) });
            shell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.0, GridUnitType.Star) });
            shell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _windowFrame.Child = shell;
            root.Children.Add(_windowFrame);
            Content = root;

            _titleBar = new Border { Background = Brushes.White, CornerRadius = new CornerRadius(18.0, 18.0, 0.0, 0.0), BorderThickness = new Thickness(0.0) };
            var title = new Grid();
            var brand = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(18.0, 0.0, 0.0, 0.0), VerticalAlignment = VerticalAlignment.Center };
            var logo = new Border { Width = 34.0, Height = 34.0, CornerRadius = new CornerRadius(9.0), Background = SB(CPrimary), VerticalAlignment = VerticalAlignment.Center };
            logo.Child = MakeIcon(IconPlay, Brushes.White, 18.0);
            brand.Children.Add(logo);
            brand.Children.Add(new TextBlock
            {
                Text = "Axe",
                Foreground = SB(CTextMain),
                FontSize = 17.0,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(11.0, 0.0, 0.0, 0.0),
                VerticalAlignment = VerticalAlignment.Center
            });
            var badge = new Border
            {
                Background = SB(28, 46, 108, 168),
                CornerRadius = new CornerRadius(6.0),
                Padding = new Thickness(7.0, 2.0, 7.0, 2.0),
                Margin = new Thickness(9.0, 0.0, 0.0, 0.0),
                VerticalAlignment = VerticalAlignment.Center
            };
            badge.Child = new TextBlock { Text = AppInfo.Version, Foreground = SB(CPrimary), FontSize = 11.0 };
            brand.Children.Add(badge);
            title.Children.Add(brand);

            var status = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0.0, 0.0, 156.0, 0.0),
                VerticalAlignment = VerticalAlignment.Center
            };
            _statusDot = new Ellipse
            {
                Width = 9.0,
                Height = 9.0,
                Fill = SB(CSuccess),
                VerticalAlignment = VerticalAlignment.Center,
                Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = CSuccess, BlurRadius = 8.0, ShadowDepth = 0.0 }
            };
            _headerStatus = new TextBlock
            {
                Text = "就绪",
                Foreground = SB(CTextSub),
                FontSize = 12.5,
                Margin = new Thickness(7.0, 0.0, 0.0, 0.0),
                VerticalAlignment = VerticalAlignment.Center
            };
            status.Children.Add(_statusDot);
            status.Children.Add(_headerStatus);
            title.Children.Add(status);

            var winButtons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0.0, 0.0, 10.0, 0.0),
                VerticalAlignment = VerticalAlignment.Center
            };
            Button minBtn = MakeWinButton(IconMin, false);
            minBtn.Click += MinClick;
            _maxBtn = MakeWinButton(IconMax, false);
            _maxBtn.Click += MaxClick;
            Button closeBtn = MakeWinButton(IconClose, true);
            closeBtn.Click += CloseClick;
            winButtons.Children.Add(minBtn);
            winButtons.Children.Add(_maxBtn);
            winButtons.Children.Add(closeBtn);
            title.Children.Add(winButtons);
            _titleBar.Child = title;
            _titleBar.MouseLeftButtonDown += TitleBarDown;
            Grid.SetRow(_titleBar, 0);
            shell.Children.Add(_titleBar);

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
            var card = new Border
            {
                Background = Brushes.White,
                CornerRadius = new CornerRadius(18.0),
                Margin = new Thickness(0.0, 0.0, 8.0, 0.0),
                Effect = Shadow(Color.FromRgb(185, 198, 214), 16.0, 3.0, 0.55)
            };
            var form = new StackPanel { Margin = new Thickness(13.0, 7.0, 13.0, 5.0) };
            card.Child = form;
            scroll.Content = card;
            Grid.SetRow(scroll, 0);
            left.Children.Add(scroll);

            form.Children.Add(GroupTitle("素材与输出", IconFolder));
            _srcBox = CompactTextBox();
            _srcBox.Text = _shownSource;
            _srcBox.LostFocus += (s, e) => RefreshCategories();
            form.Children.Add(FormRow("素材目录", 60.0, WithBrowse(_srcBox, false)));
            // 规格统一 status of the chosen folder, filled in by RunUnifyCheck.
            _unifyLine = HintLine();
            form.Children.Add(_unifyLine);
            _outBox = CompactTextBox();
            _outBox.Text = _shownOutput;
            form.Children.Add(FormRow("输出目录", 60.0, WithBrowse(_outBox, false)));
            _nameBox = CompactTextBox();
            _nameBox.Text = _settings.NameFormat;
            _nameBox.TextChanged += (s, e) => UpdateNamePreview();
            form.Children.Add(FormRow("成片命名", 60.0, WithTokens(_nameBox)));
            _namePreview = HintLine();
            _namePreview.Foreground = SB(CTextHint);
            _namePreview.Visibility = Visibility.Visible;
            form.Children.Add(_namePreview);
            _introBox = CompactTextBox();
            _introBox.Text = _shownIntro;
            form.Children.Add(FormRow("片头素材", 60.0, WithBrowse(_introBox, true)));

            form.Children.Add(GroupTitle("成片规格", IconAspect));
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
            _durBox = CompactTextBox();
            _durBox.Text = _settings.DurationSec.ToString();
            _durBox.TextChanged += (s, e) => UpdateNamePreview();
            UpdateNamePreview();
            form.Children.Add(TwoField("成片数量", _countBox, "目标时长", _durBox));
            _tolBox = CompactTextBox();
            _tolBox.Text = _settings.ToleranceSec.ToString();
            _parallelBox = CompactTextBox();
            _parallelBox.Text = _settings.ParallelJobs.ToString();
            _parallelBox.ToolTip = "这台电脑建议 " + DeviceCheck.Suggest();
            form.Children.Add(TwoField("浮动秒数", _tolBox, "并行进程", _parallelBox));

            form.Children.Add(GroupTitle("性能与操作", IconBolt));
            _nvencCheck = new CheckBox { IsChecked = _settings.UseNvenc, Style = (Style)Resources["Check"], VerticalAlignment = VerticalAlignment.Center };
            _nvencCheck.Content = new TextBlock { Text = "硬件加速", FontSize = 12.5, Foreground = SB(CTextMain), VerticalAlignment = VerticalAlignment.Center };
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

            _mixBtn = new Button { Height = 46.0, Margin = new Thickness(0.0, 12.0, 8.0, 0.0), Style = (Style)Resources["PrimaryButton"] };
            var mixContent = new StackPanel { Orientation = Orientation.Horizontal };
            _mixIcon = MakeIcon(IconPlay, Brushes.White, 18.0);
            mixContent.Children.Add(_mixIcon);
            _mixText = new TextBlock
            {
                Text = "开始快拼",
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                FontSize = 15.0,
                Margin = new Thickness(9.0, 0.0, 0.0, 0.0),
                VerticalAlignment = VerticalAlignment.Center
            };
            mixContent.Children.Add(_mixText);
            _mixBtn.Content = mixContent;
            _mixBtn.Click += MixClick;
            Grid.SetRow(_mixBtn, 1);
            left.Children.Add(_mixBtn);

            // Right: 成片效果 and 运行日志 as two tabs of one card. A task switches to the log; a new log
            // line while the effects are shown puts a dot on the 运行日志 tab.
            var side = new Border
            {
                Background = Brushes.White,
                CornerRadius = new CornerRadius(18.0),
                Margin = new Thickness(8.0, 0.0, 0.0, 0.0),
                Effect = Shadow(Color.FromRgb(185, 198, 214), 16.0, 3.0, 0.55)
            };
            var sideGrid = new Grid { Margin = new Thickness(16.0, 12.0, 16.0, 16.0) };
            sideGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            sideGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.0, GridUnitType.Star) });
            var track = new Border
            {
                Background = SB(Color.FromRgb(236, 241, 247)),
                CornerRadius = new CornerRadius(10.0),
                Padding = new Thickness(3.0),
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0.0, 0.0, 0.0, 12.0)
            };
            var tabRow = new StackPanel { Orientation = Orientation.Horizontal };
            Style seg = EffectsPanel.SegmentStyle();
            _logDot = new System.Windows.Shapes.Ellipse { Width = 7.0, Height = 7.0, Fill = SB(CAccent), Visibility = Visibility.Collapsed, Margin = new Thickness(7.0, 0.0, 0.0, 0.0), VerticalAlignment = VerticalAlignment.Center };
            _tabFx = new ToggleButton { Style = seg, IsChecked = true, Content = TabContent(IconSparkle, "成片效果", null) };
            _tabLog = new ToggleButton { Style = seg, Content = TabContent(IconTerminal, "运行日志", _logDot) };
            _tabFx.Click += (s, e) => ShowTab(false);
            _tabLog.Click += (s, e) => ShowTab(true);
            tabRow.Children.Add(_tabFx);
            tabRow.Children.Add(_tabLog);
            track.Child = tabRow;
            sideGrid.Children.Add(track);
            _effects = new EffectsPanel(_appDir, CurrentEffects());
            _effects.Changed += EffectsChanged;
            _effects.SubtitleWanted = () => MixEngine.SubtitleModelReady(_appDir) || DownloadSpeechModel();
            Grid.SetRow(_effects, 1);
            sideGrid.Children.Add(_effects);
            _logBody = new Border { Background = new SolidColorBrush(Color.FromRgb(28, 32, 40)), CornerRadius = new CornerRadius(12.0), Visibility = Visibility.Collapsed };
            _logBox = new RichTextBox
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0.0),
                Foreground = new SolidColorBrush(Color.FromRgb(220, 226, 234)),
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12.5,
                Margin = new Thickness(14.0, 12.0, 10.0, 12.0),
                IsReadOnly = true,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            _logBody.Child = _logBox;
            Grid.SetRow(_logBody, 1);
            sideGrid.Children.Add(_logBody);
            side.Child = sideGrid;
            Grid.SetColumn(side, 1);
            body.Children.Add(side);

            var foot = new Grid { Margin = new Thickness(26.0, 6.0, 26.0, 12.0) };
            foot.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
            foot.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _progress = new ProgressBar { Minimum = 0.0, Maximum = 100.0, Value = 0.0, Style = (Style)Resources["Bar"] };
            _statusText = new TextBlock
            {
                FontSize = 12.0,
                Foreground = SB(CTextHint),
                Margin = new Thickness(16.0, 0.0, 0.0, 0.0),
                VerticalAlignment = VerticalAlignment.Center,
                MaxWidth = 520.0,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Text = "就绪"
            };
            _statusText.SetBinding(FrameworkElement.ToolTipProperty, new Binding("Text") { Source = _statusText });
            Grid.SetColumn(_progress, 0);
            Grid.SetColumn(_statusText, 1);
            foot.Children.Add(_progress);
            foot.Children.Add(_statusText);
            Grid.SetRow(foot, 2);
            shell.Children.Add(foot);
        }

        private static StackPanel TabContent(string icon, string text, UIElement extra)
        {
            var p = new StackPanel { Orientation = Orientation.Horizontal };
            p.Children.Add(MakeIcon(icon, SB(CPrimary), 14.0));
            p.Children.Add(new TextBlock { Text = text, Margin = new Thickness(7.0, 0.0, 0.0, 0.0), VerticalAlignment = VerticalAlignment.Center });
            if (extra != null) p.Children.Add(extra);
            return p;
        }

        private StackPanel GroupTitle(string text, string iconData)
        {
            var p = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0.0, 2.0, 0.0, 4.0) };
            p.Children.Add(MakeIcon(iconData, SB(CPrimary), 14.0));
            p.Children.Add(new TextBlock
            {
                Text = text,
                FontSize = 12.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = SB(CPrimaryDeep),
                Margin = new Thickness(7.0, 0.0, 0.0, 0.0),
                VerticalAlignment = VerticalAlignment.Center
            });
            return p;
        }

        private TextBox CompactTextBox()
        {
            return new TextBox
            {
                Height = 28.0,
                VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(9.0, 0.0, 9.0, 0.0),
                Style = (Style)Resources["InputBox"]
            };
        }

        private ComboBox CompactCombo()
        {
            return new ComboBox { Height = 28.0, Style = (Style)Resources["Combo"] };
        }

        private Grid FormRow(string label, double labelWidth, UIElement control)
        {
            var g = new Grid { Margin = new Thickness(0.0, 0.0, 0.0, 4.0) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(labelWidth) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
            var tb = new TextBlock
            {
                Text = label,
                FontSize = 12.5,
                Foreground = SB(CTextSub),
                TextAlignment = TextAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0.0, 0.0, 9.0, 0.0)
            };
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
            var a = new TextBlock { Text = label, FontSize = 12.5, Foreground = SB(CTextMain) };
            var b = new TextBlock { Text = token, FontSize = 12.5, Foreground = SB(CPrimary) };
            var c = new TextBlock { Text = example, FontSize = 12.0, Foreground = SB(CTextHint) };
            Grid.SetColumn(b, 1);
            Grid.SetColumn(c, 2);
            g.Children.Add(a);
            g.Children.Add(b);
            g.Children.Add(c);
            return g;
        }

        private Button MiniButton(string text)
        {
            return new Button
            {
                Content = text,
                Width = 52.0,
                Height = 28.0,
                Margin = new Thickness(8.0, 0.0, 0.0, 0.0),
                FontSize = 12.5,
                Style = (Style)Resources["SecondaryButton"]
            };
        }

        private Grid TwoField(string l1, TextBox b1, string l2, TextBox b2)
        {
            var outer = new Grid { Margin = new Thickness(0.0, 0.0, 0.0, 4.0) };
            outer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
            outer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
            var g1 = new Grid { Margin = new Thickness(0.0, 0.0, 4.0, 0.0) };
            g1.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
            TextBlock t1 = MiniLabel(l1);
            t1.Margin = new Thickness(0.0, 0.0, 7.0, 0.0);
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
            t2.Margin = new Thickness(0.0, 0.0, 7.0, 0.0);
            b2.Margin = new Thickness(0.0);
            Grid.SetColumn(t2, 0);
            Grid.SetColumn(b2, 1);
            g2.Children.Add(t2);
            g2.Children.Add(b2);
            Grid.SetColumn(g2, 1);
            outer.Children.Add(g2);
            return outer;
        }

        // Two labelled controls side by side; a null label leaves the right control without one.
        private Grid TwoControls(string l1, FrameworkElement c1, string l2, FrameworkElement c2)
        {
            var outer = new Grid { Margin = new Thickness(0.0, 0.0, 0.0, 4.0) };
            outer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
            outer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
            for (int i = 0; i < 2; i++)
            {
                string label = i == 0 ? l1 : l2;
                FrameworkElement c = i == 0 ? c1 : c2;
                var g = new Grid { Margin = i == 0 ? new Thickness(0.0, 0.0, 4.0, 0.0) : new Thickness(4.0, 0.0, 0.0, 0.0) };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
                if (label != null)
                {
                    TextBlock t = MiniLabel(label);
                    t.Margin = new Thickness(0.0, 0.0, 7.0, 0.0);
                    Grid.SetColumn(t, 0);
                    g.Children.Add(t);
                }
                c.Margin = new Thickness(0.0);
                Grid.SetColumn(c, 1);
                g.Children.Add(c);
                Grid.SetColumn(g, i);
                outer.Children.Add(g);
            }
            return outer;
        }

        private TextBlock MiniLabel(string text)
        {
            return new TextBlock { Text = text, FontSize = 12.5, Foreground = SB(CTextSub), VerticalAlignment = VerticalAlignment.Center };
        }

        private Grid TwoButtons(FrameworkElement a, FrameworkElement b)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
            a.Margin = new Thickness(0.0, 0.0, 4.0, 4.0);
            b.Margin = new Thickness(4.0, 0.0, 0.0, 4.0);
            Grid.SetColumn(a, 0);
            Grid.SetColumn(b, 1);
            g.Children.Add(a);
            g.Children.Add(b);
            return g;
        }

        private Button CompactActionButton(string text, string iconData, bool force)
        {
            var btn = new Button { Height = 32.0, Margin = new Thickness(0.0, 0.0, 0.0, 4.0), FontSize = 12.5 };
            Brush iconBrush;
            Brush textBrush;
            if (force)
            {
                var s = new Style(typeof(Button));
                s.Setters.Add(new Setter(FrameworkElement.CursorProperty, System.Windows.Input.Cursors.Hand));
                s.Setters.Add(new Setter(Control.TemplateProperty, ButtonTpl(
                    Brushes.Transparent, SB(28, 255, 138, 31), SB(46, 255, 138, 31), Brushes.Transparent,
                    SB(150, 255, 160, 70), SB(CAccent), new CornerRadius(10.0))));
                btn.Style = s;
                iconBrush = SB(CAccentDeep);
                textBrush = SB(CAccentDeep);
            }
            else
            {
                btn.Style = (Style)Resources["SecondaryButton"];
                iconBrush = SB(CPrimary);
                textBrush = SB(CPrimaryDeep);
            }
            var content = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            content.Children.Add(MakeIcon(iconData, iconBrush, 15.0));
            content.Children.Add(new TextBlock { Text = text, Margin = new Thickness(7.0, 0.0, 0.0, 0.0), Foreground = textBrush, VerticalAlignment = VerticalAlignment.Center });
            btn.Content = content;
            return btn;
        }

        // Title-bar button; the icon geometry lives in Tag so it can be swapped (maximize/restore).
        internal static Button MakeWinButton(string iconData, bool isClose)
        {
            var wbd = new FrameworkElementFactory(typeof(Border), "wbd");
            wbd.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            wbd.SetValue(Border.CornerRadiusProperty, new CornerRadius(8.0));
            var icon = new FrameworkElementFactory(typeof(Path), "wicon");
            icon.SetValue(Path.DataProperty, new TemplateBindingExtension(FrameworkElement.TagProperty));
            icon.SetValue(Shape.StrokeProperty, SB(CTextSub));
            icon.SetValue(Shape.StrokeThicknessProperty, 1.6);
            icon.SetValue(Shape.StrokeStartLineCapProperty, PenLineCap.Round);
            icon.SetValue(Shape.StrokeEndLineCapProperty, PenLineCap.Round);
            icon.SetValue(FrameworkElement.WidthProperty, 15.0);
            icon.SetValue(FrameworkElement.HeightProperty, 15.0);
            icon.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            icon.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            icon.SetValue(Shape.StretchProperty, Stretch.Uniform);
            wbd.AppendChild(icon);
            var tpl = new ControlTemplate(typeof(Button));
            tpl.VisualTree = wbd;
            var over = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            if (isClose)
            {
                over.Setters.Add(new Setter(Border.BackgroundProperty, SB(CCloseHover), "wbd"));
                over.Setters.Add(new Setter(Shape.StrokeProperty, Brushes.White, "wicon"));
            }
            else
            {
                over.Setters.Add(new Setter(Border.BackgroundProperty, SB(CWinHover), "wbd"));
            }
            tpl.Triggers.Add(over);
            return new Button
            {
                Width = 46.0,
                Height = 32.0,
                Template = tpl,
                Focusable = false,
                Cursor = System.Windows.Input.Cursors.Arrow,
                Tag = Geo(iconData)
            };
        }
    }
}
