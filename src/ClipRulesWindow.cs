using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using static VideoMixApp.Theme;

namespace VideoMixApp
{
    // One video in the 素材规则 dialog.
    public sealed class RuleRow : INotifyPropertyChanged
    {
        public string FullPath { get; set; }
        public string Name { get; set; }
        public string Length { get; set; }
        internal ClipRule Saved;
        private bool _noOpen;
        private bool _noUse;

        public bool NoUse
        {
            get { return _noUse; }
            set
            {
                if (_noUse == value) return;
                _noUse = value;
                Raise("NoUse");
                Raise("OpenChecked");
                Raise("OpenEnabled");
                Raise("Dim");
            }
        }

        // 开头不用 as shown: also ticked (and greyed) while 成片不用 is, which includes it.
        public bool OpenChecked
        {
            get { return _noOpen || _noUse; }
            set
            {
                if (_noUse || _noOpen == value) return;
                _noOpen = value;
                Raise("OpenChecked");
            }
        }

        public bool OpenEnabled
        {
            get { return !_noUse; }
        }

        public double Dim
        {
            get { return _noUse ? 0.5 : 1.0; }
        }

        // The list style shared with the folder picker asks for it.
        public bool Selectable
        {
            get { return true; }
        }

        internal ClipRule Rule
        {
            get { return (_noOpen ? ClipRule.NoOpen : ClipRule.None) | (_noUse ? ClipRule.NoUse : ClipRule.None); }
        }

        internal bool HasRule
        {
            get { return _noOpen || _noUse; }
        }

        internal void Load(ClipRule r)
        {
            _noOpen = (r & ClipRule.NoOpen) != 0;
            _noUse = (r & ClipRule.NoUse) != 0;
            Saved = Rule;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void Raise(string name)
        {
            PropertyChangedEventHandler h = PropertyChanged;
            if (h != null) h(this, new PropertyChangedEventArgs(name));
        }
    }

    // 素材规则 (v2.7.0): every video of the chosen folder (and category) with 开头不用 and 成片不用.
    // Saved with 确定 into the folders (see ClipRules). The picture on the right is a frame of the
    // selected video; double-click a row (or 播放) opens it in the PC's player.
    internal sealed class ClipRulesWindow : Window
    {
        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
        private static extern int StrCmpLogicalW(string a, string b);

        private readonly string _appDir;
        private readonly string _root;
        private readonly string _category;
        private readonly int _frameW;
        private readonly int _frameH;
        private readonly PreviewRenderer _renderer;
        private ClipRules _rules;
        private readonly List<RuleRow> _all = new List<RuleRow>();
        private TextBox _search;
        private TextBlock _searchHint;
        private CheckBox _onlySet;
        private ListBox _list;
        private TextBlock _summary;
        private Image _preview;
        private TextBlock _previewName;
        private TextBlock _previewInfo;
        private Button _play;
        private Button _ok;
        private int _previewSeq;
        // Closed before the background reading finished: its results are dropped.
        private bool _closed;

        // 开头不用 / 成片不用 after saving, for the main window's log.
        public string SavedSummary;

        public ClipRulesWindow(string appDir, string root, string category, int frameW, int frameH)
        {
            _appDir = appDir;
            _root = root;
            _category = category;
            _frameW = Math.Max(1, frameW);
            _frameH = Math.Max(1, frameH);
            _renderer = new PreviewRenderer(appDir);
            Rect work = SystemParameters.WorkArea;
            Width = Math.Min(860.0, work.Width - 40.0);
            Height = Math.Min(660.0, work.Height - 40.0);
            MinWidth = Math.Min(620.0, Width);
            MinHeight = Math.Min(440.0, Height);
            DialogChrome.Apply(this, "素材规则", IconTag, BuildBody(), true);
            PreviewKeyDown += OnKey;
            Loaded += (s, e) => Load();
            Closed += (s, e) => _closed = true;
        }

        private UIElement BuildBody()
        {
            var body = new Grid { Margin = new Thickness(18.0, 6.0, 18.0, 16.0) };
            body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.0, GridUnitType.Star) });
            body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Search and the 只看已设置 switch.
            var tools = new Grid { Margin = new Thickness(0.0, 0.0, 0.0, 10.0) };
            tools.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
            tools.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var host = new Grid();
            // This text box template applies the padding twice, so the values are halved.
            _search = new TextBox { Height = 32.0, VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(15.0, 0.0, 8.0, 0.0), Style = MainWindow.MakeTextBoxStyle(), FontSize = 13.0 };
            _search.TextChanged += (s, e) =>
            {
                _searchHint.Visibility = _search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
                ApplyFilter();
            };
            var icon = MakeIcon(IconSearch, K.TextHint, 15.0);
            icon.HorizontalAlignment = HorizontalAlignment.Left;
            icon.Margin = new Thickness(11.0, 0.0, 0.0, 0.0);
            icon.IsHitTestVisible = false;
            _searchHint = new TextBlock { Text = "搜索视频", FontSize = 13.0, Margin = new Thickness(34.0, 0.0, 0.0, 0.0), VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false }.Fg(K.TextHint);
            host.Children.Add(_search);
            host.Children.Add(icon);
            host.Children.Add(_searchHint);
            tools.Children.Add(host);
            _onlySet = new CheckBox
            {
                Style = MainWindow.MakeCheckBoxStyle(),
                Margin = new Thickness(16.0, 0.0, 2.0, 0.0),
                VerticalAlignment = VerticalAlignment.Center,
                Content = new TextBlock { Text = "只看已设置", FontSize = 12.5 }.Fg(K.Text)
            };
            _onlySet.Click += (s, e) => ApplyFilter();
            Grid.SetColumn(_onlySet, 1);
            tools.Children.Add(_onlySet);
            body.Children.Add(tools);

            // The list (with its column titles) and the preview beside it.
            var middle = new Grid();
            middle.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
            middle.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetRow(middle, 1);
            body.Children.Add(middle);

            var listArea = new Grid();
            listArea.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            listArea.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.0, GridUnitType.Star) });
            // Lined up with the row template (item padding and margin, list padding, scroll bar).
            var head = RowGrid();
            head.Margin = new Thickness(17.0, 0.0, 35.0, 6.0);
            head.Children.Add(HeadText("视频", 0, HorizontalAlignment.Left));
            head.Children.Add(HeadText("时长", 1, HorizontalAlignment.Center));
            head.Children.Add(HeadText("开头不用", 2, HorizontalAlignment.Center));
            head.Children.Add(HeadText("成片不用", 3, HorizontalAlignment.Center));
            listArea.Children.Add(head);
            var listBorder = new Chamfer
            {
                BorderThickness = 1.0,
                Cut = 10.0,
                Corners = Cuts.TopLeft | Cuts.BottomRight,
                Padding = new Thickness(0.0, 4.0, 2.0, 4.0)
            }.Bg(K.Panel2).Bd(K.Line);
            _list = new ListBox
            {
                BorderThickness = new Thickness(0.0),
                Background = Brushes.Transparent,
                ItemContainerStyle = FolderPicker.ItemStyle(),
                ItemTemplate = RowTemplate(),
                HorizontalContentAlignment = HorizontalAlignment.Stretch
            };
            ScrollViewer.SetHorizontalScrollBarVisibility(_list, ScrollBarVisibility.Disabled);
            ScrollViewer.SetVerticalScrollBarVisibility(_list, ScrollBarVisibility.Visible);
            ScrollViewer.SetCanContentScroll(_list, true);
            VirtualizingPanel.SetIsVirtualizing(_list, true);
            VirtualizingPanel.SetVirtualizationMode(_list, VirtualizationMode.Recycling);
            VirtualizingPanel.SetScrollUnit(_list, ScrollUnit.Pixel);
            _list.Resources.Add(typeof(ScrollBar), FolderPicker.ScrollBarStyle());
            _list.SelectionChanged += (s, e) => ShowPreview();
            _list.MouseDoubleClick += (s, e) =>
            {
                // Only on a row's name or length: not on a tick box, the scroll bar or empty space.
                var d = e.OriginalSource as DependencyObject;
                if (d != null && FindParent<ListBoxItem>(d) != null && FindParent<CheckBox>(d) == null) Play();
            };
            listBorder.Child = _list;
            Grid.SetRow(listBorder, 1);
            listArea.Children.Add(listBorder);
            middle.Children.Add(listArea);

            double pw = 200.0;
            double ph = Math.Min(356.0, Math.Round(pw * _frameH / _frameW));
            var side = new StackPanel { Width = pw, Margin = new Thickness(14.0, 24.0, 0.0, 0.0) };
            var frame = new Border { Width = pw, Height = ph, BorderThickness = new Thickness(1.0), ClipToBounds = true }.Bg(K.Frame).Bd(K.LineHi);
            _preview = new Image { Stretch = Stretch.Uniform };
            frame.Child = _preview;
            side.Children.Add(frame);
            _previewName = new TextBlock { FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0.0, 10.0, 0.0, 0.0) }.Fg(K.Text);
            _previewInfo = new TextBlock { FontSize = 12.0, FontFamily = TechFont, Margin = new Thickness(0.0, 3.0, 0.0, 0.0) }.Fg(K.TextHint);
            side.Children.Add(_previewName);
            side.Children.Add(_previewInfo);
            _play = new Button { Content = "播放", Width = 72.0, Height = 28.0, FontSize = 12.5, Margin = new Thickness(0.0, 10.0, 0.0, 0.0), HorizontalAlignment = HorizontalAlignment.Left, Style = MainWindow.MakeSecondaryButtonStyle(), IsEnabled = false };
            _play.Click += (s, e) => Play();
            side.Children.Add(_play);
            Grid.SetColumn(side, 1);
            middle.Children.Add(side);

            // Counts and the buttons.
            var foot = new Grid { Margin = new Thickness(0.0, 14.0, 0.0, 0.0) };
            foot.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
            foot.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            foot.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _summary = new TextBlock { FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Text = "正在读取…" }.Fg(K.TextSub);
            var cancel = new Button { Content = "取消", Width = 84.0, Height = 36.0, FontSize = 13.0, Margin = new Thickness(0.0, 0.0, 10.0, 0.0), Style = MainWindow.MakeSecondaryButtonStyle() };
            cancel.Click += (s, e) => DialogResult = false;
            _ok = new Button { Width = 96.0, Height = 36.0, Style = ConfirmStyle(), IsEnabled = false };
            _ok.Content = new TextBlock { Text = "确定", FontSize = 13.5, FontWeight = FontWeights.SemiBold };
            _ok.Click += (s, e) => Save();
            Grid.SetColumn(cancel, 1);
            Grid.SetColumn(_ok, 2);
            foot.Children.Add(_summary);
            foot.Children.Add(cancel);
            foot.Children.Add(_ok);
            Grid.SetRow(foot, 2);
            body.Children.Add(foot);
            return body;
        }

        private static Grid RowGrid()
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56.0) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72.0) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72.0) });
            return g;
        }

        private static TextBlock HeadText(string text, int column, HorizontalAlignment align)
        {
            var t = new TextBlock { Text = text, FontSize = 12.0, HorizontalAlignment = align }.Fg(K.TextSub);
            Grid.SetColumn(t, column);
            return t;
        }

        private static DataTemplate RowTemplate()
        {
            var g = new FrameworkElementFactory(typeof(Grid));
            g.SetValue(FrameworkElement.MinHeightProperty, 30.0);
            foreach (double width in new[] { -1.0, 56.0, 72.0, 72.0 })
            {
                var col = new FrameworkElementFactory(typeof(ColumnDefinition));
                col.SetValue(ColumnDefinition.WidthProperty, width < 0 ? new GridLength(1.0, GridUnitType.Star) : new GridLength(width));
                g.AppendChild(col);
            }
            var name = new FrameworkElementFactory(typeof(TextBlock));
            name.SetBinding(TextBlock.TextProperty, new Binding("Name"));
            name.SetBinding(FrameworkElement.ToolTipProperty, new Binding("FullPath"));
            name.SetBinding(UIElement.OpacityProperty, new Binding("Dim"));
            name.SetValue(TextBlock.FontSizeProperty, 13.0);
            name.SetResourceReference(TextBlock.ForegroundProperty, K.Text);
            name.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            name.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            g.AppendChild(name);
            var len = new FrameworkElementFactory(typeof(TextBlock));
            len.SetValue(Grid.ColumnProperty, 1);
            len.SetBinding(TextBlock.TextProperty, new Binding("Length"));
            len.SetValue(TextBlock.FontSizeProperty, 12.0);
            len.SetResourceReference(TextBlock.ForegroundProperty, K.TextHint);
            len.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            len.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            g.AppendChild(len);
            Style check = MainWindow.MakeCheckBoxStyle();
            for (int i = 0; i < 2; i++)
            {
                var cb = new FrameworkElementFactory(typeof(CheckBox));
                cb.SetValue(Grid.ColumnProperty, 2 + i);
                cb.SetValue(FrameworkElement.StyleProperty, check);
                // The style leaves room for a label; the margin centres the box itself.
                cb.SetValue(FrameworkElement.MarginProperty, new Thickness(10.0, 0.0, 0.0, 0.0));
                cb.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
                cb.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
                cb.SetBinding(ToggleButton.IsCheckedProperty, new Binding(i == 0 ? "OpenChecked" : "NoUse") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
                if (i == 0)
                {
                    cb.SetBinding(UIElement.IsEnabledProperty, new Binding("OpenEnabled"));
                    cb.SetBinding(UIElement.OpacityProperty, new Binding("Dim"));
                }
                g.AppendChild(cb);
            }
            return new DataTemplate { VisualTree = g };
        }

        private static Style ConfirmStyle()
        {
            var s = new Style(typeof(Button));
            s.Setters.Add(new Setter(FrameworkElement.CursorProperty, Cursors.Hand));
            s.Setters.Add(DynSetter(Control.ForegroundProperty, K.OnAccent));
            s.Setters.Add(new Setter(Control.TemplateProperty, MainWindow.ButtonTpl(K.StartBg, K.StartHover, K.StartPress, null, null, 10.0)));
            return s;
        }

        private static T FindParent<T>(DependencyObject d) where T : DependencyObject
        {
            while (d != null && !(d is T)) d = d is Visual || d is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
            return d as T;
        }

        // The videos, their lengths and rules, read off the UI thread.
        private void Load()
        {
            string root = _root;
            string category = _category;
            string appDir = _appDir;
            Task.Factory.StartNew(() =>
            {
                var rules = new ClipRules(appDir);
                List<FileInfo> files = MixEngine.ScanSources(root, category);
                Parallel.ForEach(files, new ParallelOptions { MaxDegreeOfParallelism = 8 }, f => Unified.Read(f));
                Unified.SaveStore();
                // Names from the chosen category's folder: its name in front of every row said nothing (v2.7.1).
                string top = (string.IsNullOrEmpty(category) ? root : System.IO.Path.Combine(root, category)).TrimEnd('\\');
                var rows = new List<RuleRow>();
                foreach (var f in files)
                {
                    string dir = f.DirectoryName.TrimEnd('\\');
                    var row = new RuleRow
                    {
                        FullPath = f.FullName,
                        Name = string.Equals(dir, top, StringComparison.OrdinalIgnoreCase) ? f.Name : System.IO.Path.GetFileName(dir) + "\\" + f.Name,
                        Length = LengthText(Unified.Read(f).Duration)
                    };
                    row.Load(rules.Of(f.FullName));
                    rows.Add(row);
                }
                rows.Sort((a, b) => StrCmpLogicalW(a.Name, b.Name));
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (!_closed) Fill(rules, rows);
                }));
            }).ContinueWith(t =>
            {
                Exception ex = t.Exception.GetBaseException();
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    // Setting DialogResult on a closed window would throw.
                    if (_closed) return;
                    var re = ex as ClipRulesException;
                    MessageDialog.Show(this, re != null ? "素材规则文件读不出来：" + re.File + "\n" + re.Message : "读取素材目录失败：" + ex.Message);
                    if (!_closed) DialogResult = false;
                }));
            }, TaskContinuationOptions.OnlyOnFaulted);
        }

        private void Fill(ClipRules rules, List<RuleRow> rows)
        {
            _rules = rules;
            _all.AddRange(rows);
            foreach (var r in _all) r.PropertyChanged += (s, e) => UpdateSummary();
            _ok.IsEnabled = true;
            ApplyFilter();
            UpdateSummary();
            if (_list.Items.Count > 0) _list.SelectedIndex = 0;
            _search.Focus();
        }

        private static string LengthText(double seconds)
        {
            if (!(seconds > 0)) return "—";
            int s = (int)Math.Round(seconds);
            return (s / 60) + ":" + (s % 60).ToString("00");
        }

        private void ApplyFilter()
        {
            if (_list == null) return;
            string kw = _search.Text.Trim();
            bool only = _onlySet.IsChecked == true;
            NameMatcher m = kw.Length > 0 ? new NameMatcher(kw, false) : null;
            var hl = new List<int>();
            var shown = new ObservableCollection<RuleRow>();
            foreach (var r in _all)
            {
                if (only && !r.HasRule) continue;
                if (m != null && m.Match(r.Name, true, hl) < 0) continue;
                shown.Add(r);
            }
            object keep = _list.SelectedItem;
            _list.ItemsSource = shown;
            if (keep != null && shown.Contains((RuleRow)keep)) _list.SelectedItem = keep;
        }

        private void UpdateSummary()
        {
            int open = 0;
            int unused = 0;
            foreach (var r in _all)
            {
                if (r.NoUse) unused++;
                else if (r.OpenChecked) open++;
            }
            string rules = ClipRules.Summary(open, unused);
            _summary.Text = "共 " + _all.Count + " 个视频" + (rules.Length > 0 ? " · " + rules : "");
        }

        private void ShowPreview()
        {
            var r = _list.SelectedItem as RuleRow;
            int seq = ++_previewSeq;
            _preview.Source = null;
            _play.IsEnabled = r != null;
            if (r == null)
            {
                _previewName.Text = "";
                _previewInfo.Text = "";
                return;
            }
            _previewName.Text = System.IO.Path.GetFileName(r.FullPath);
            _previewInfo.Text = r.Length == "—" ? "" : "时长 " + r.Length;
            string path = r.FullPath;
            int w = 360;
            int h = Math.Max(2, (int)Math.Round(360.0 * _frameH / _frameW / 2) * 2);
            Task.Factory.StartNew(() =>
            {
                BitmapImage pic = null;
                try
                {
                    byte[] png = _renderer.Frame(new FileInfo(path), w, h);
                    if (png != null)
                    {
                        pic = new BitmapImage();
                        pic.BeginInit();
                        pic.CacheOption = BitmapCacheOption.OnLoad;
                        pic.StreamSource = new MemoryStream(png);
                        pic.EndInit();
                        pic.Freeze();
                    }
                }
                catch
                {
                    pic = null;
                }
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (seq == _previewSeq) _preview.Source = pic;
                }));
            });
        }

        private void Play()
        {
            var r = _list.SelectedItem as RuleRow;
            if (r == null) return;
            try
            {
                Process.Start(new ProcessStartInfo(r.FullPath) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageDialog.Show(this, "无法打开：" + ex.Message);
            }
        }

        private void Save()
        {
            if (_rules == null) return;
            foreach (var r in _all) if (r.Rule != r.Saved) _rules.Set(r.FullPath, r.Rule);
            string error = _rules.Save();
            if (error != null)
            {
                MessageDialog.Show(this, "素材规则没有保存：" + error);
                return;
            }
            int open = 0;
            int unused = 0;
            foreach (var r in _all)
            {
                if (r.NoUse) unused++;
                else if (r.OpenChecked) open++;
            }
            SavedSummary = ClipRules.Summary(open, unused);
            DialogResult = true;
        }

        private void OnKey(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                if (_search.Text.Length > 0) _search.Text = "";
                else DialogResult = false;
                e.Handled = true;
            }
            else if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
            {
                _search.Focus();
                _search.SelectAll();
                e.Handled = true;
            }
        }
    }
}
