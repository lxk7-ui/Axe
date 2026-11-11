using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using static VideoMixApp.Theme;
using Path = System.Windows.Shapes.Path;

namespace VideoMixApp
{
    // Text with an outline, drawn like the burned-in subtitles (white, black edge).
    internal sealed class OutlinedText : FrameworkElement
    {
        private string _text = "";
        private double _size = 16;
        private Geometry _geo;

        public string Text
        {
            get { return _text; }
            set { _text = value ?? ""; Rebuild(); }
        }

        public double FontPixels
        {
            get { return _size; }
            set
            {
                value = Math.Max(4, value);
                // Dragging lays the preview out on every mouse move: same size, same outline.
                if (_geo != null && Math.Abs(value - _size) < 0.01) return;
                _size = value;
                Rebuild();
            }
        }

        private void Rebuild()
        {
            // The constructor without pixelsPerDip: the one with it needs .NET Framework 4.6.2, which
            // the first Windows 10 releases (1507 / 1511, LTSB 2015) do not have. For an outline
            // geometry the two give the same result.
#pragma warning disable 618
            var ft = new FormattedText(_text, CultureInfo.GetCultureInfo("zh-CN"), FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Microsoft YaHei"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal), _size, Brushes.White);
#pragma warning restore 618
            _geo = ft.BuildGeometry(new Point(0, 0));
            InvalidateMeasure();
            InvalidateVisual();
        }

        protected override Size MeasureOverride(Size available)
        {
            if (_geo == null || _geo.Bounds.IsEmpty) return new Size(0, 0);
            double pad = _size * 0.1;
            return new Size(_geo.Bounds.Right + pad * 2, _geo.Bounds.Bottom + pad * 2);
        }

        protected override void OnRender(DrawingContext dc)
        {
            if (_geo == null) return;
            // Clickable between the letters too (it is dragged).
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
            double pad = _size * 0.1;
            dc.PushTransform(new TranslateTransform(pad, pad));
            // Half of the pen lies outside the letters: 0.19 of this size matches libass's border of
            // 0.07 of its (larger) font size.
            var pen = new Pen(Brushes.Black, Math.Max(1.5, _size * 0.19)) { LineJoin = PenLineJoin.Round };
            dc.DrawGeometry(null, pen, _geo);
            dc.DrawGeometry(Brushes.White, null, _geo);
            dc.Pop();
        }
    }

    // The 成片效果 tab: a preview of one frame of the chosen footage with the filter applied, the
    // sticker moving and a sample subtitle (both dragged into place right there; the mouse wheel
    // changes their size), the filter and sticker galleries with 滤镜强度, 变速 and 识别语音配字幕.
    internal sealed class EffectsPanel : Grid
    {
        private const double MaxW = 236.0;
        private const double MaxH = 420.0;
        // The preview's frame line, the two slider rows and 位置恢复默认 under it.
        private const double BelowPreview = 92.0;
        private const int FramePx = 640;
        private const string SampleText = "字幕示例 现在下单只要9.9块";

        // A choice, a position or a size changed (the window saves the settings).
        public event Action Changed;
        // The subtitle switch was turned on: return false when it has to go off again (no speech model).
        public Func<bool> SubtitleWanted;

        private readonly string _appDir;
        private readonly PreviewRenderer _renderer;
        private readonly EffectOptions _fx;
        private int _w = 720;
        private int _h = 1280;
        private string _root = "";
        private string _cat = "";
        private double _pw;
        private double _ph;
        private double _fitH = MaxH;
        private double _stickerAspect = 1.0;

        private readonly Border _phone;
        private readonly Canvas _canvas;
        private readonly Image _bg;
        private readonly Border _sampleTag;
        private readonly Image _sticker;
        private readonly Border _stickerBox;
        private readonly OutlinedText _sub;
        private readonly Slider _stickerSlider;
        private readonly Slider _subSlider;
        private readonly Grid _strengthRow;
        private readonly Slider _strengthSlider;
        private readonly TextBlock _strengthText;
        private readonly ToggleButton _tabFilter;
        private readonly ToggleButton _tabSticker;
        private readonly ScrollViewer _scroll;
        private readonly WrapPanel _filterWrap = new WrapPanel { Margin = new Thickness(2.0, 2.0, 0.0, 4.0) };
        private readonly WrapPanel _stickerWrap = new WrapPanel { Margin = new Thickness(2.0, 2.0, 0.0, 4.0) };
        private readonly List<ToggleButton> _speedButtons = new List<ToggleButton>();
        private readonly CheckBox _subCheck;
        private readonly DispatcherTimer _timer;

        private List<EffectAsset> _filters = new List<EffectAsset>();
        private List<EffectAsset> _stickers = new List<EffectAsset>();
        private readonly Dictionary<string, Border> _filterTiles = new Dictionary<string, Border>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Border> _stickerTiles = new Dictionary<string, Border>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Image> _filterPics = new Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);
        private readonly List<Image> _randomFilterPics = new List<Image>();
        private readonly List<Image> _randomStickerPics = new List<Image>();
        private BitmapSource _frame;
        // The gallery pictures of the filters (by LUT path), made from a frame a third of the size.
        private readonly Dictionary<string, BitmapSource> _filtered = new Dictionary<string, BitmapSource>(StringComparer.OrdinalIgnoreCase);
        // The frame as Bgr32 pixels, and the filter shown in the preview applied to all of it (made when
        // it is first shown, a few milliseconds): a strength below 100 % is mixed from the two right
        // away while the slider moves, into one reused bitmap.
        private byte[] _framePx;
        private int _fw;
        private int _fh;
        private string _shownLut;
        private byte[] _shownPx;
        private WriteableBitmap _mixed;
        private byte[] _mixBuf;
        private readonly Dictionary<string, BitmapSource> _stickerStills = new Dictionary<string, BitmapSource>(StringComparer.OrdinalIgnoreCase);
        private List<BitmapSource> _anim = new List<BitmapSource>();
        private int _animIndex;
        private int _tick;
        private int _cycle;
        private int _frameSeq;
        private int _animSeq;
        private int _listSeq;
        private FrameworkElement _drag;
        private Point _grab;
        private bool _suppress;

        public EffectsPanel(string appDir, EffectOptions fx)
        {
            _appDir = appDir;
            _fx = fx;
            _renderer = new PreviewRenderer(appDir);
            Style sliderStyle = SliderStyle();
            ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });

            // Left: preview and the two sizes.
            var left = new StackPanel { Margin = new Thickness(0.0, 0.0, 16.0, 0.0) };
            _canvas = new Canvas { ClipToBounds = true };
            _bg = new Image { Stretch = Stretch.Fill };
            RenderOptions.SetBitmapScalingMode(_bg, BitmapScalingMode.HighQuality);
            _canvas.Children.Add(_bg);
            _sampleTag = new Border
            {
                Background = SB(130, 0, 0, 0),
                CornerRadius = new CornerRadius(4.0),
                Padding = new Thickness(6.0, 2.0, 6.0, 2.0),
                Child = new TextBlock { Text = "示例画面", FontSize = 11.0, Foreground = Brushes.White },
                IsHitTestVisible = false,
                Visibility = Visibility.Collapsed
            };
            Canvas.SetLeft(_sampleTag, 6.0);
            Canvas.SetTop(_sampleTag, 6.0);
            _canvas.Children.Add(_sampleTag);
            _sticker = new Image { Stretch = Stretch.Uniform };
            RenderOptions.SetBitmapScalingMode(_sticker, BitmapScalingMode.HighQuality);
            _stickerBox = new Border { Child = _sticker, Background = SB(1, 0, 0, 0), Cursor = Cursors.SizeAll, ToolTip = "拖动调整位置，滚轮调整大小" };
            _canvas.Children.Add(_stickerBox);
            _sub = new OutlinedText { Text = SampleText, Cursor = Cursors.SizeAll, ToolTip = "拖动调整位置，滚轮调整大小" };
            _canvas.Children.Add(_sub);
            _phone = new Border
            {
                Child = _canvas,
                CornerRadius = new CornerRadius(12.0),
                Background = new LinearGradientBrush(Color.FromRgb(74, 84, 100), Color.FromRgb(32, 36, 44), 90),
                BorderBrush = SB(CBorder),
                BorderThickness = new Thickness(1.0),
                HorizontalAlignment = HorizontalAlignment.Center,
                Effect = Shadow(Color.FromRgb(150, 165, 185), 12.0, 2.0, 0.45)
            };
            left.Children.Add(_phone);
            _stickerSlider = new Slider { Minimum = 0.08, Maximum = 0.7, Style = sliderStyle };
            _subSlider = new Slider { Minimum = 0.035, Maximum = 0.11, Style = sliderStyle };
            left.Children.Add(SliderRow("贴纸", _stickerSlider));
            left.Children.Add(SliderRow("字幕", _subSlider));
            var reset = new Button { Content = "位置恢复默认", Height = 26.0, FontSize = 12.0, Margin = new Thickness(0.0, 6.0, 0.0, 0.0), Style = MainWindow.MakeSecondaryButtonStyle() };
            reset.Click += (s, e) => ResetPositions();
            left.Children.Add(reset);
            Grid.SetColumn(left, 0);
            Children.Add(left);

            // Right: galleries, 滤镜强度, 变速, subtitles.
            var right = new Grid();
            right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.0, GridUnitType.Star) });
            right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Style seg = SegmentStyle();
            var tabs = new Border { Background = SB(Color.FromRgb(236, 241, 247)), CornerRadius = new CornerRadius(10.0), Padding = new Thickness(3.0), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0.0, 0.0, 0.0, 8.0) };
            var tabRow = new StackPanel { Orientation = Orientation.Horizontal };
            _tabFilter = new ToggleButton { Content = "滤镜", Style = seg, IsChecked = true };
            _tabSticker = new ToggleButton { Content = "贴纸", Style = seg };
            _tabFilter.Click += (s, e) => ShowGallery(true);
            _tabSticker.Click += (s, e) => ShowGallery(false);
            tabRow.Children.Add(_tabFilter);
            tabRow.Children.Add(_tabSticker);
            tabs.Child = tabRow;
            right.Children.Add(tabs);
            _scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = _filterWrap };
            Grid.SetRow(_scroll, 1);
            right.Children.Add(_scroll);

            // 滤镜强度 right under the filters (shown with them), for all filters and 随机 alike.
            _strengthRow = new Grid { Margin = new Thickness(0.0, 8.0, 6.0, 0.0), Background = Brushes.Transparent };
            _strengthRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _strengthRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
            _strengthRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44.0) });
            _strengthRow.Children.Add(new TextBlock { Text = "强度", FontSize = 12.5, Foreground = SB(CTextSub), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0.0, 0.0, 8.0, 0.0) });
            _strengthSlider = new Slider { Minimum = 0.1, Maximum = 1.0, SmallChange = 0.05, LargeChange = 0.1, TickFrequency = 0.05, IsSnapToTickEnabled = true, Value = 1.0, Style = sliderStyle, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(_strengthSlider, 1);
            _strengthRow.Children.Add(_strengthSlider);
            _strengthText = new TextBlock { Text = "100%", FontSize = 12.5, Foreground = SB(CTextMain), TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(_strengthText, 2);
            _strengthRow.Children.Add(_strengthText);
            _strengthRow.MouseWheel += (s, e) =>
            {
                _strengthSlider.Value = SubtitleWriter.Clamp(Math.Round((_strengthSlider.Value + (e.Delta > 0 ? 0.05 : -0.05)) * 20) / 20, _strengthSlider.Minimum, _strengthSlider.Maximum);
                e.Handled = true;
            };
            Grid.SetRow(_strengthRow, 2);
            right.Children.Add(_strengthRow);

            var speedRow = new DockPanel { Margin = new Thickness(0.0, 10.0, 0.0, 0.0) };
            var speedLabel = new TextBlock { Text = "变速", FontSize = 12.5, Foreground = SB(CTextSub), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0.0, 0.0, 8.0, 0.0) };
            DockPanel.SetDock(speedLabel, Dock.Left);
            speedRow.Children.Add(speedLabel);
            var pills = new UniformGrid { Rows = 1 };
            Style pill = PillStyle();
            foreach (double v in EffectOptions.Speeds)
            {
                var b = new ToggleButton { Content = EffectOptions.SpeedText(v), Style = pill, Tag = v, Margin = new Thickness(0.0, 0.0, 3.0, 0.0) };
                b.Click += (s, e) => PickSpeed((double)((ToggleButton)s).Tag);
                _speedButtons.Add(b);
                pills.Children.Add(b);
            }
            speedRow.Children.Add(pills);
            Grid.SetRow(speedRow, 3);
            right.Children.Add(speedRow);

            _subCheck = new CheckBox { Style = MainWindow.MakeCheckBoxStyle(), Margin = new Thickness(0.0, 10.0, 0.0, 2.0) };
            _subCheck.Content = new TextBlock { Text = "识别语音配字幕", FontSize = 12.5, Foreground = SB(CTextMain), VerticalAlignment = VerticalAlignment.Center };
            _subCheck.Checked += (s, e) => SubtitleToggled(true);
            _subCheck.Unchecked += (s, e) => SubtitleToggled(false);
            Grid.SetRow(_subCheck, 4);
            right.Children.Add(_subCheck);
            Grid.SetColumn(right, 1);
            Children.Add(right);

            _strengthSlider.ValueChanged += (s, e) =>
            {
                _strengthText.Text = EffectOptions.PercentText(_strengthSlider.Value);
                if (_suppress) return;
                _fx.FilterStrength = EffectOptions.ValidStrength(_strengthSlider.Value);
                ShowBackground();
                Changed?.Invoke();
            };

            _stickerSlider.ValueChanged += (s, e) =>
            {
                if (_suppress) return;
                _fx.StickerSize = _stickerSlider.Value;
                Layout();
                Changed?.Invoke();
            };
            _subSlider.ValueChanged += (s, e) =>
            {
                if (_suppress) return;
                _fx.SubSize = _subSlider.Value;
                Layout();
                Changed?.Invoke();
            };
            HookDrag(_stickerBox);
            HookDrag(_sub);
            _sub.SizeChanged += (s, e) => Layout();
            _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(83) };
            _timer.Tick += (s, e) => Tick();
            IsVisibleChanged += (s, e) =>
            {
                if (IsVisible) _timer.Start();
                else _timer.Stop();
            };
            SizeChanged += (s, e) => FitPreview(e.NewSize.Height);
            SyncControls();
            SetRatio(720, 1280);
        }

        public EffectOptions Options
        {
            get { return _fx; }
        }

        // The settings were loaded or changed outside the panel.
        public void SyncControls()
        {
            _suppress = true;
            _stickerSlider.Value = SubtitleWriter.Clamp(_fx.StickerSize, _stickerSlider.Minimum, _stickerSlider.Maximum);
            _subSlider.Value = SubtitleWriter.Clamp(_fx.SubSize, _subSlider.Minimum, _subSlider.Maximum);
            _strengthSlider.Value = EffectOptions.ValidStrength(_fx.FilterStrength);
            _strengthText.Text = EffectOptions.PercentText(_fx.FilterStrength);
            foreach (var b in _speedButtons) b.IsChecked = Math.Abs((double)b.Tag - EffectOptions.ValidSpeed(_fx.Speed)) < 0.001;
            _subCheck.IsChecked = _fx.Subtitles;
            _suppress = false;
            _sub.Visibility = _fx.Subtitles ? Visibility.Visible : Visibility.Collapsed;
            MarkSelection();
        }

        public void SetRatio(int w, int h)
        {
            if (w <= 0 || h <= 0) return;
            bool same = w == _w && h == _h && _pw > 0;
            _w = w;
            _h = h;
            ApplySize();
            if (!same) LoadFrame();
        }

        // On a small screen (e.g. 1366x768 at 125 %) the panel is lower than the full preview: the
        // preview gets smaller so the two sliders and 位置恢复默认 below it stay in view.
        private void FitPreview(double panelHeight)
        {
            double room = Math.Max(140.0, Math.Min(MaxH, panelHeight - BelowPreview));
            if (Math.Abs(room - _fitH) < 1.0) return;
            _fitH = room;
            ApplySize();
        }

        private void ApplySize()
        {
            double k = Math.Min(MaxW / _w, _fitH / _h);
            _pw = Math.Round(_w * k);
            _ph = Math.Round(_h * k);
            _canvas.Width = _pw;
            _canvas.Height = _ph;
            _bg.Width = _pw;
            _bg.Height = _ph;
            _phone.Width = _pw + 2;
            _phone.Height = _ph + 2;
            Layout();
        }

        public void SetSource(string root, string category)
        {
            root = root ?? "";
            category = category ?? "";
            if (root == _root && category == _cat) return;
            _root = root;
            _cat = category;
            LoadFrame();
        }

        // The effects folders may have changed (files added in Explorer): rebuild the galleries.
        public void RefreshAssets()
        {
            List<EffectAsset> f = EffectAssets.Filters(_appDir);
            List<EffectAsset> s = EffectAssets.Stickers(_appDir);
            if (SameList(f, _filters) && SameList(s, _stickers) && _filterWrap.Children.Count > 0) return;
            _filters = f;
            _stickers = s;
            // A remembered choice whose file was removed (or 随机 with nothing left to choose from).
            bool reset = false;
            if (!string.IsNullOrEmpty(_fx.Filter) && (_fx.Filter == EffectOptions.Random ? f.Count == 0 : EffectAssets.Find(f, _fx.Filter) == null))
            {
                _fx.Filter = "";
                reset = true;
            }
            if (!string.IsNullOrEmpty(_fx.Sticker) && (_fx.Sticker == EffectOptions.Random ? s.Count == 0 : EffectAssets.Find(s, _fx.Sticker) == null))
            {
                _fx.Sticker = "";
                reset = true;
            }
            if (reset) Changed?.Invoke();
            _tabFilter.Content = "滤镜 " + f.Count;
            _tabSticker.Content = "贴纸 " + s.Count;
            BuildFilterTiles();
            BuildStickerTiles();
            MarkSelection();
            LoadStickerPictures();
            LoadFiltered();
            LoadAnimation();
        }

        // Same files, none replaced since (a file put over one with the same name gets new pictures).
        private static bool SameList(List<EffectAsset> a, List<EffectAsset> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
            {
                if (a[i].Path != b[i].Path || a[i].Stamp != b[i].Stamp) return false;
            }
            return true;
        }

        private void ShowGallery(bool filters)
        {
            _tabFilter.IsChecked = filters;
            _tabSticker.IsChecked = !filters;
            _scroll.Content = filters ? _filterWrap : _stickerWrap;
            _strengthRow.Visibility = filters ? Visibility.Visible : Visibility.Collapsed;
            _scroll.ScrollToTop();
        }

        // ---- galleries ----

        private void BuildFilterTiles()
        {
            _filterWrap.Children.Clear();
            _filterTiles.Clear();
            _filterPics.Clear();
            _randomFilterPics.Clear();
            double tw = 66;
            double th = Math.Round(tw * _h / _w);
            if (th > 118)
            {
                th = 118;
                tw = Math.Round(th * _w / _h);
            }
            var none = new Image { Width = tw, Height = th, Stretch = Stretch.UniformToFill };
            _filterPics[""] = none;
            AddTile(_filterWrap, _filterTiles, "", "无", none, tw, () => PickFilter(""));
            if (_filters.Count > 0)
            {
                var mosaic = new UniformGrid { Rows = 2, Columns = 2, Width = tw, Height = th };
                for (int i = 0; i < 4; i++)
                {
                    var im = new Image { Stretch = Stretch.UniformToFill };
                    _randomFilterPics.Add(im);
                    mosaic.Children.Add(im);
                }
                AddTile(_filterWrap, _filterTiles, EffectOptions.Random, "随机", mosaic, tw, () => PickFilter(EffectOptions.Random));
            }
            foreach (var f in _filters)
            {
                string name = f.Name;
                var im = new Image { Width = tw, Height = th, Stretch = Stretch.UniformToFill };
                _filterPics[name] = im;
                AddTile(_filterWrap, _filterTiles, name, name, im, tw, () => PickFilter(name));
            }
            ShowFilterPictures();
        }

        private void BuildStickerTiles()
        {
            _stickerWrap.Children.Clear();
            _stickerTiles.Clear();
            _randomStickerPics.Clear();
            const double size = 58;
            var none = new Grid { Width = size, Height = size };
            var slash = new Path
            {
                Data = Geo("M12,3 A9,9 0 1 0 12,21 A9,9 0 1 0 12,3 Z M5.6,5.6 L18.4,18.4"),
                Stroke = SB(CTextHint),
                StrokeThickness = 1.6,
                Width = 26,
                Height = 26,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            none.Children.Add(slash);
            AddTile(_stickerWrap, _stickerTiles, "", "无", none, size, () => PickSticker(""));
            if (_stickers.Count > 0)
            {
                var mosaic = new UniformGrid { Rows = 2, Columns = 2, Width = size, Height = size };
                for (int i = 0; i < 4; i++)
                {
                    var im = new Image { Stretch = Stretch.Uniform, Margin = new Thickness(1.0) };
                    _randomStickerPics.Add(im);
                    mosaic.Children.Add(im);
                }
                AddTile(_stickerWrap, _stickerTiles, EffectOptions.Random, "随机", mosaic, size, () => PickSticker(EffectOptions.Random));
            }
            foreach (var s in _stickers)
            {
                string name = s.Name;
                var im = new Image { Width = size, Height = size, Stretch = Stretch.Uniform };
                RenderOptions.SetBitmapScalingMode(im, BitmapScalingMode.HighQuality);
                im.Tag = name;
                AddTile(_stickerWrap, _stickerTiles, name, name, im, size, () => PickSticker(name));
            }
        }

        private void AddTile(WrapPanel wrap, Dictionary<string, Border> map, string key, string label, UIElement picture, double width, Action click)
        {
            var stack = new StackPanel();
            var frame = new Border { CornerRadius = new CornerRadius(7.0), ClipToBounds = true, Background = SB(Color.FromRgb(240, 243, 248)), Child = picture };
            stack.Children.Add(frame);
            stack.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 11.5,
                Foreground = SB(CTextSub),
                TextAlignment = TextAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Width = width,
                Margin = new Thickness(0.0, 3.0, 0.0, 0.0)
            });
            var tile = new Border
            {
                Child = stack,
                Padding = new Thickness(3.0),
                Margin = new Thickness(0.0, 0.0, 6.0, 6.0),
                CornerRadius = new CornerRadius(9.0),
                BorderThickness = new Thickness(2.0),
                BorderBrush = Brushes.Transparent,
                Background = Brushes.Transparent,
                Cursor = Cursors.Hand,
                ToolTip = label
            };
            tile.MouseEnter += (s, e) => { if (!IsSelected(map, tile)) tile.BorderBrush = SB(90, 46, 108, 168); };
            tile.MouseLeave += (s, e) => { if (!IsSelected(map, tile)) tile.BorderBrush = Brushes.Transparent; };
            tile.MouseLeftButtonUp += (s, e) => click();
            map[key] = tile;
            wrap.Children.Add(tile);
        }

        private bool IsSelected(Dictionary<string, Border> map, Border tile)
        {
            string key = map == _filterTiles ? (_fx.Filter ?? "") : (_fx.Sticker ?? "");
            Border sel;
            return map.TryGetValue(key, out sel) && sel == tile;
        }

        private void MarkSelection()
        {
            Mark(_filterTiles, _fx.Filter ?? "");
            Mark(_stickerTiles, _fx.Sticker ?? "");
            // Without a filter there is no strength to set.
            bool on = !string.IsNullOrEmpty(_fx.Filter);
            _strengthRow.IsEnabled = on;
            _strengthRow.Opacity = on ? 1.0 : 0.45;
        }

        private static void Mark(Dictionary<string, Border> map, string key)
        {
            foreach (var kv in map)
            {
                bool on = string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase);
                kv.Value.BorderBrush = on ? SB(CPrimary) : Brushes.Transparent;
                kv.Value.Background = on ? SB(22, 46, 108, 168) : Brushes.Transparent;
            }
        }

        private void PickFilter(string name)
        {
            _fx.Filter = name;
            _cycle = 0;
            MarkSelection();
            ShowBackground();
            Changed?.Invoke();
        }

        private void PickSticker(string name)
        {
            _fx.Sticker = name;
            _cycle = 0;
            MarkSelection();
            LoadAnimation();
            Changed?.Invoke();
        }

        private void PickSpeed(double v)
        {
            _fx.Speed = v;
            foreach (var b in _speedButtons) b.IsChecked = Math.Abs((double)b.Tag - v) < 0.001;
            Changed?.Invoke();
        }

        private void SubtitleToggled(bool on)
        {
            if (_suppress) return;
            if (on && SubtitleWanted != null && !SubtitleWanted())
            {
                _suppress = true;
                _subCheck.IsChecked = false;
                _suppress = false;
                on = false;
            }
            _fx.Subtitles = on;
            _sub.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            Layout();
            Changed?.Invoke();
        }

        public void SetSubtitles(bool on)
        {
            _suppress = true;
            _subCheck.IsChecked = on;
            _suppress = false;
            _fx.Subtitles = on;
            _sub.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ResetPositions()
        {
            var d = new EffectOptions();
            _fx.SubX = d.SubX;
            _fx.SubY = d.SubY;
            _fx.StickerX = d.StickerX;
            _fx.StickerY = d.StickerY;
            _fx.StickerSize = d.StickerSize;
            _fx.SubSize = d.SubSize;
            SyncControls();
            Layout();
            Changed?.Invoke();
        }

        // ---- preview ----

        // Same placement as 快拼: sticker width relative to the frame's shorter side and kept inside
        // the frame; the subtitle line centred on its point.
        private void Layout()
        {
            if (_pw <= 0) return;
            double shortSide = Math.Min(_pw, _ph);
            double sw = shortSide * SubtitleWriter.Clamp(_fx.StickerSize, 0.05, 0.9);
            double sh = sw / _stickerAspect;
            _stickerBox.Width = sw;
            _stickerBox.Height = sh;
            Canvas.SetLeft(_stickerBox, Math.Max(0, Math.Min(_pw - sw, _pw * _fx.StickerX - sw / 2)));
            Canvas.SetTop(_stickerBox, Math.Max(0, Math.Min(_ph - sh, _ph * _fx.StickerY - sh / 2)));
            _stickerBox.Visibility = string.IsNullOrEmpty(_fx.Sticker) || _sticker.Source == null ? Visibility.Collapsed : Visibility.Visible;
            // libass sizes a font by its full line height, WPF by the em square: measured on 微软雅黑 Bold,
            // the same text is 0.75 as wide in libass at the same number.
            _sub.FontPixels = shortSide * SubtitleWriter.Clamp(_fx.SubSize, 0.03, 0.12) * 0.75;
            _sub.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Size ds = _sub.DesiredSize;
            double margin = shortSide * SubtitleWriter.EdgeMargin;
            Canvas.SetLeft(_sub, SubtitleWriter.Inside(_pw * _fx.SubX, ds.Width / 2, _pw, margin) - ds.Width / 2);
            Canvas.SetTop(_sub, SubtitleWriter.Inside(_ph * _fx.SubY, ds.Height / 2, _ph, margin) - ds.Height / 2);
        }

        private void HookDrag(FrameworkElement e)
        {
            e.MouseLeftButtonDown += (s, a) =>
            {
                _drag = e;
                _grab = a.GetPosition(e);
                e.CaptureMouse();
                a.Handled = true;
            };
            e.MouseMove += (s, a) =>
            {
                if (_drag != e || !e.IsMouseCaptured) return;
                Point p = a.GetPosition(_canvas);
                double cx = Math.Max(0, Math.Min(_pw, p.X - _grab.X + e.ActualWidth / 2));
                double cy = Math.Max(0, Math.Min(_ph, p.Y - _grab.Y + e.ActualHeight / 2));
                if (e == _sub)
                {
                    // Snap to the middle: subtitles are usually centred.
                    if (Math.Abs(cx - _pw / 2) < 6) cx = _pw / 2;
                    _fx.SubX = cx / _pw;
                    _fx.SubY = cy / _ph;
                }
                else
                {
                    _fx.StickerX = cx / _pw;
                    _fx.StickerY = cy / _ph;
                }
                Layout();
            };
            e.MouseLeftButtonUp += (s, a) =>
            {
                if (_drag == e) e.ReleaseMouseCapture();
            };
            // Also when the drag is cut short (Alt+Tab, a dialog): the position it reached is kept.
            e.LostMouseCapture += (s, a) =>
            {
                if (_drag != e) return;
                _drag = null;
                Changed?.Invoke();
            };
            e.MouseWheel += (s, a) =>
            {
                Slider sl = e == _sub ? _subSlider : _stickerSlider;
                sl.Value = SubtitleWriter.Clamp(sl.Value + (a.Delta > 0 ? 1 : -1) * (sl.Maximum - sl.Minimum) / 30, sl.Minimum, sl.Maximum);
                a.Handled = true;
            };
        }

        // The background of the preview: the frame with the chosen filter (随机: one after another) at
        // the chosen strength.
        private void ShowBackground()
        {
            string lut = null;
            if (_fx.Filter == EffectOptions.Random)
            {
                if (_filters.Count > 0) lut = _filters[_cycle % _filters.Count].Path;
            }
            else if (!string.IsNullOrEmpty(_fx.Filter))
            {
                EffectAsset a = EffectAssets.Find(_filters, _fx.Filter);
                if (a != null) lut = a.Path;
            }
            if (lut == null || _framePx == null)
            {
                _bg.Source = _frame;
                return;
            }
            if (_shownLut != lut || _shownPx == null || _shownPx.Length != _framePx.Length)
            {
                CubeLut l = CubeLut.Get(lut);
                if (l == null)
                {
                    _bg.Source = _frame;
                    return;
                }
                if (_shownPx == null || _shownPx.Length != _framePx.Length) _shownPx = new byte[_framePx.Length];
                l.Apply(_framePx, _shownPx);
                _shownLut = lut;
            }
            if (_mixed == null || _mixed.PixelWidth != _fw || _mixed.PixelHeight != _fh)
            {
                _mixed = new WriteableBitmap(_fw, _fh, 96, 96, PixelFormats.Bgr32, null);
                _mixBuf = new byte[_framePx.Length];
            }
            double s = EffectOptions.ValidStrength(_fx.FilterStrength);
            byte[] shown = _shownPx;
            if (s < 0.995)
            {
                // About a millisecond, so dragging the slider stays smooth.
                CubeLut.Mix(_framePx, _shownPx, s, _mixBuf);
                shown = _mixBuf;
            }
            _mixed.WritePixels(new Int32Rect(0, 0, _fw, _fh), shown, _fw * 4, 0);
            _bg.Source = _mixed;
        }

        private void ShowFilterPictures()
        {
            Image none;
            if (_filterPics.TryGetValue("", out none)) none.Source = _frame;
            foreach (var f in _filters)
            {
                Image im;
                BitmapSource b;
                if (_filterPics.TryGetValue(f.Name, out im)) im.Source = _filtered.TryGetValue(f.Path, out b) ? b : null;
            }
            for (int i = 0; i < _randomFilterPics.Count; i++)
            {
                BitmapSource b = null;
                if (_filters.Count > 0) _filtered.TryGetValue(_filters[(i * Math.Max(1, _filters.Count / 4)) % _filters.Count].Path, out b);
                _randomFilterPics[i].Source = b;
            }
        }

        private void Tick()
        {
            _tick++;
            if (_anim.Count > 1 && _fx.Sticker != EffectOptions.Random)
            {
                _animIndex = (_animIndex + 1) % _anim.Count;
                _sticker.Source = _anim[_animIndex];
            }
            // 随机: show the choices one after another, a new one every 1.5 s.
            if (_tick % 18 == 0)
            {
                _cycle++;
                if (_fx.Filter == EffectOptions.Random) ShowBackground();
                if (_fx.Sticker == EffectOptions.Random && _stickers.Count > 0)
                {
                    BitmapSource b;
                    if (_stickerStills.TryGetValue(_stickers[_cycle % _stickers.Count].Path, out b)) SetSticker(b);
                }
            }
        }

        private void SetSticker(BitmapSource b)
        {
            _sticker.Source = b;
            if (b != null && b.PixelHeight > 0) _stickerAspect = b.PixelWidth / (double)b.PixelHeight;
            Layout();
        }

        // ---- loading (ffmpeg runs in the background; only the newest request is shown) ----

        // Folder, category, ratio and the filter list often change together (start-up, picking a
        // folder): one render a moment later instead of one per change.
        private void LoadFrame()
        {
            if (_frameTimer == null)
            {
                _frameTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
                _frameTimer.Tick += (s, e) =>
                {
                    _frameTimer.Stop();
                    LoadFrameNow();
                };
            }
            _frameTimer.Stop();
            _frameTimer.Start();
        }

        private DispatcherTimer _frameTimer;

        private void LoadFrameNow()
        {
            int seq = ++_frameSeq;
            string root = _root;
            string cat = _cat;
            double k = (double)FramePx / Math.Max(_w, _h);
            int fw = (int)Math.Round(_w * k / 2) * 2;
            int fh = (int)Math.Round(_h * k / 2) * 2;
            List<EffectAsset> filters = _filters;
            Task.Factory.StartNew(() =>
            {
                FileInfo v = PreviewRenderer.SampleVideo(root, cat);
                byte[] png = v != null ? _renderer.Frame(v, fw, fh) : null;
                bool sample = png == null;
                if (sample) png = _renderer.SampleFrame(fw, fh);
                BitmapSource frame = Decode(png);
                int pw;
                int ph;
                byte[] px = Pixels(frame, out pw, out ph);
                // The picture right away, the filtered ones a moment later.
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (seq != _frameSeq) return;
                    _frame = frame;
                    _framePx = px;
                    _fw = pw;
                    _fh = ph;
                    _sampleTag.Visibility = sample && frame != null ? Visibility.Visible : Visibility.Collapsed;
                    _filtered.Clear();
                    _shownLut = null;
                    BuildFilterTiles();
                    MarkSelection();
                    ShowBackground();
                }));
                if (px == null || seq != _frameSeq) return;
                // Every filter applied here, the way lut3d does it, to a third-size frame (the tiles are
                // small): about a millisecond each.
                int tw;
                int th;
                byte[] small = Third(px, pw, ph, out tw, out th);
                var pics = new BitmapSource[filters.Count];
                Parallel.For(0, filters.Count, i =>
                {
                    if (seq != _frameSeq) return;
                    CubeLut lut = CubeLut.Get(filters[i].Path);
                    if (lut != null) pics[i] = Bitmap(lut.Apply(small), tw, th);
                });
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (seq != _frameSeq) return;
                    for (int i = 0; i < filters.Count; i++)
                    {
                        if (pics[i] != null) _filtered[filters[i].Path] = pics[i];
                    }
                    ShowFilterPictures();
                    ShowBackground();
                }));
            });
        }

        // The picture as Bgr32 bytes (blue, green, red, unused); null when there is none.
        private static byte[] Pixels(BitmapSource b, out int w, out int h)
        {
            w = 0;
            h = 0;
            if (b == null) return null;
            try
            {
                var src = new FormatConvertedBitmap(b, PixelFormats.Bgr32, null, 0);
                w = src.PixelWidth;
                h = src.PixelHeight;
                var px = new byte[w * h * 4];
                src.CopyPixels(px, w * 4, 0);
                return px;
            }
            catch
            {
                return null;
            }
        }

        // A third of the size, each pixel the average of a 3 x 3 block (Bgr32).
        private static byte[] Third(byte[] px, int w, int h, out int tw, out int th)
        {
            tw = Math.Max(1, w / 3);
            th = Math.Max(1, h / 3);
            var dst = new byte[tw * th * 4];
            for (int y = 0; y < th; y++)
            {
                for (int x = 0; x < tw; x++)
                {
                    int sb = 0, sg = 0, sr = 0, n = 0;
                    for (int yy = y * 3; yy < Math.Min(h, y * 3 + 3); yy++)
                    {
                        for (int xx = x * 3; xx < Math.Min(w, x * 3 + 3); xx++)
                        {
                            int o = (yy * w + xx) * 4;
                            sb += px[o];
                            sg += px[o + 1];
                            sr += px[o + 2];
                            n++;
                        }
                    }
                    int d = (y * tw + x) * 4;
                    dst[d] = (byte)((sb + n / 2) / n);
                    dst[d + 1] = (byte)((sg + n / 2) / n);
                    dst[d + 2] = (byte)((sr + n / 2) / n);
                    dst[d + 3] = 255;
                }
            }
            return dst;
        }

        private static BitmapSource Bitmap(byte[] px, int w, int h)
        {
            BitmapSource b = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgr32, null, px, w * 4);
            b.Freeze();
            return b;
        }

        private void LoadFiltered()
        {
            // The frame's filters come with LoadFrame; a new filter list needs the whole set again.
            LoadFrame();
        }

        private void LoadStickerPictures()
        {
            int seq = ++_listSeq;
            List<EffectAsset> stickers = _stickers;
            Task.Factory.StartNew(() =>
            {
                var pics = new BitmapSource[stickers.Count];
                var tiles = new BitmapSource[stickers.Count];
                Parallel.For(0, stickers.Count, new ParallelOptions { MaxDegreeOfParallelism = 4 }, i =>
                {
                    pics[i] = Decode(_renderer.StickerPicture(stickers[i], 128));
                    tiles[i] = Trim(pics[i]);
                });
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (seq != _listSeq) return;
                    _stickerStills.Clear();
                    var trimmed = new Dictionary<string, BitmapSource>(StringComparer.OrdinalIgnoreCase);
                    for (int i = 0; i < stickers.Count; i++)
                    {
                        if (pics[i] != null) _stickerStills[stickers[i].Path] = pics[i];
                        if (tiles[i] != null) trimmed[stickers[i].Path] = tiles[i];
                    }
                    foreach (UIElement child in _stickerWrap.Children)
                    {
                        var tile = child as Border;
                        var stack = tile != null ? tile.Child as StackPanel : null;
                        var frame = stack != null && stack.Children.Count > 0 ? stack.Children[0] as Border : null;
                        var im = frame != null ? frame.Child as Image : null;
                        if (im == null || !(im.Tag is string)) continue;
                        EffectAsset a = EffectAssets.Find(stickers, (string)im.Tag);
                        BitmapSource b;
                        if (a != null && trimmed.TryGetValue(a.Path, out b)) im.Source = b;
                    }
                    for (int i = 0; i < _randomStickerPics.Count && stickers.Count > 0; i++)
                    {
                        BitmapSource b;
                        trimmed.TryGetValue(stickers[(i * Math.Max(1, stickers.Count / 4) + 1) % stickers.Count].Path, out b);
                        _randomStickerPics[i].Source = b;
                    }
                    // 随机 shows the current one right away (not only at the next change).
                    if (_fx.Sticker == EffectOptions.Random && stickers.Count > 0)
                    {
                        BitmapSource b;
                        if (_stickerStills.TryGetValue(stickers[_cycle % stickers.Count].Path, out b)) SetSticker(b);
                    }
                    Layout();
                }));
            });
        }

        private void LoadAnimation()
        {
            int seq = ++_animSeq;
            _anim = new List<BitmapSource>();
            _animIndex = 0;
            if (string.IsNullOrEmpty(_fx.Sticker))
            {
                SetSticker(null);
                return;
            }
            if (_fx.Sticker == EffectOptions.Random)
            {
                BitmapSource first = null;
                if (_stickers.Count > 0) _stickerStills.TryGetValue(_stickers[0].Path, out first);
                SetSticker(first);
                return;
            }
            EffectAsset a = EffectAssets.Find(_stickers, _fx.Sticker);
            if (a == null)
            {
                SetSticker(null);
                return;
            }
            BitmapSource still;
            if (_stickerStills.TryGetValue(a.Path, out still)) SetSticker(still);
            Task.Factory.StartNew(() =>
            {
                List<byte[]> raw = _renderer.StickerAnimation(a, 240);
                var frames = new List<BitmapSource>();
                foreach (byte[] r in raw)
                {
                    BitmapSource b = Decode(r);
                    if (b != null) frames.Add(b);
                }
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (seq != _animSeq) return;
                    _anim = frames;
                    if (frames.Count > 0) SetSticker(frames[0]);
                }));
            });
        }

        // The picture without its transparent margin (stickers keep room for their animation), for
        // the gallery tiles only; the preview shows the whole picture, as the video does.
        private static BitmapSource Trim(BitmapSource b)
        {
            if (b == null) return null;
            try
            {
                var src = new FormatConvertedBitmap(b, PixelFormats.Bgra32, null, 0);
                int w = src.PixelWidth;
                int h = src.PixelHeight;
                var px = new byte[w * h * 4];
                src.CopyPixels(px, w * 4, 0);
                int x0 = w, y0 = h, x1 = -1, y1 = -1;
                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        if (px[(y * w + x) * 4 + 3] < 24) continue;
                        if (x < x0) x0 = x;
                        if (x > x1) x1 = x;
                        if (y < y0) y0 = y;
                        if (y > y1) y1 = y;
                    }
                }
                if (x1 < x0 || y1 < y0) return b;
                int m = 2;
                x0 = Math.Max(0, x0 - m);
                y0 = Math.Max(0, y0 - m);
                x1 = Math.Min(w - 1, x1 + m);
                y1 = Math.Min(h - 1, y1 + m);
                var c = new CroppedBitmap(src, new Int32Rect(x0, y0, x1 - x0 + 1, y1 - y0 + 1));
                c.Freeze();
                return c;
            }
            catch
            {
                return b;
            }
        }

        private static BitmapSource Decode(byte[] png)
        {
            if (png == null || png.Length < 8) return null;
            try
            {
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.StreamSource = new MemoryStream(png);
                bi.EndInit();
                bi.Freeze();
                return bi;
            }
            catch
            {
                return null;
            }
        }

        // ---- styles ----

        private static Grid SliderRow(string label, Slider slider)
        {
            var g = new Grid { Margin = new Thickness(0.0, 8.0, 0.0, 0.0), Width = MaxW };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34.0) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
            var t = new TextBlock { Text = label, FontSize = 12.0, Foreground = SB(CTextSub), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(slider, 1);
            g.Children.Add(t);
            g.Children.Add(slider);
            return g;
        }

        private static Style SliderStyle()
        {
            const string xaml =
                "<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='Slider'>" +
                "<Setter Property='Focusable' Value='False'/><Setter Property='Cursor' Value='Hand'/>" +
                "<Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Slider'>" +
                "<Grid Height='20' Background='Transparent'><Border Height='4' CornerRadius='2' Background='#E3E9F1' VerticalAlignment='Center'/>" +
                "<Track x:Name='PART_Track'>" +
                "<Track.DecreaseRepeatButton><RepeatButton Command='{x:Static Slider.DecreaseLarge}' Focusable='False'><RepeatButton.Template><ControlTemplate TargetType='RepeatButton'>" +
                "<Border Height='4' CornerRadius='2' Background='#2E6CA8' VerticalAlignment='Center'/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.DecreaseRepeatButton>" +
                "<Track.IncreaseRepeatButton><RepeatButton Command='{x:Static Slider.IncreaseLarge}' Focusable='False'><RepeatButton.Template><ControlTemplate TargetType='RepeatButton'>" +
                "<Border Background='Transparent'/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.IncreaseRepeatButton>" +
                "<Track.Thumb><Thumb><Thumb.Template><ControlTemplate TargetType='Thumb'>" +
                "<Ellipse Width='16' Height='16' Fill='White' Stroke='#2E6CA8' StrokeThickness='2'/></ControlTemplate></Thumb.Template></Thumb></Track.Thumb>" +
                "</Track></Grid></ControlTemplate></Setter.Value></Setter></Style>";
            return (Style)System.Windows.Markup.XamlReader.Parse(xaml);
        }

        // Segmented switch (滤镜 / 贴纸, and the tabs of the right card).
        internal static Style SegmentStyle()
        {
            const string xaml =
                "<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='ToggleButton'>" +
                "<Setter Property='Cursor' Value='Hand'/><Setter Property='Foreground' Value='#606E80'/><Setter Property='FontSize' Value='13'/><Setter Property='Focusable' Value='False'/>" +
                "<Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ToggleButton'>" +
                "<Border x:Name='bd' Background='Transparent' CornerRadius='8' Padding='14,5,14,5'>" +
                "<ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/></Border>" +
                "<ControlTemplate.Triggers>" +
                "<Trigger Property='IsMouseOver' Value='True'><Setter Property='Foreground' Value='#1F4E79'/></Trigger>" +
                "<Trigger Property='IsChecked' Value='True'><Setter TargetName='bd' Property='Background' Value='White'/><Setter Property='Foreground' Value='#1F4E79'/>" +
                "<Setter Property='FontWeight' Value='SemiBold'/>" +
                "<Setter TargetName='bd' Property='Effect'><Setter.Value><DropShadowEffect Color='#96A5B9' BlurRadius='8' ShadowDepth='1' Opacity='0.35'/></Setter.Value></Setter></Trigger>" +
                "</ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>";
            return (Style)System.Windows.Markup.XamlReader.Parse(xaml);
        }

        private static Style PillStyle()
        {
            const string xaml =
                "<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='ToggleButton'>" +
                "<Setter Property='Cursor' Value='Hand'/><Setter Property='Foreground' Value='#22303C'/><Setter Property='FontSize' Value='12'/><Setter Property='Height' Value='28'/><Setter Property='Focusable' Value='False'/>" +
                "<Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ToggleButton'>" +
                "<Border x:Name='bd' Background='#F4F7FB' BorderBrush='#E5EBF3' BorderThickness='1' CornerRadius='8'>" +
                "<ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/></Border>" +
                "<ControlTemplate.Triggers>" +
                "<Trigger Property='IsMouseOver' Value='True'><Setter TargetName='bd' Property='BorderBrush' Value='#2E6CA8'/></Trigger>" +
                "<Trigger Property='IsChecked' Value='True'><Setter TargetName='bd' Property='Background' Value='#2E6CA8'/><Setter TargetName='bd' Property='BorderBrush' Value='#2E6CA8'/>" +
                "<Setter Property='Foreground' Value='White'/><Setter Property='FontWeight' Value='SemiBold'/></Trigger>" +
                "</ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>";
            return (Style)System.Windows.Markup.XamlReader.Parse(xaml);
        }
    }
}
