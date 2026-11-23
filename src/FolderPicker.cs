using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Shapes;
using Path = System.Windows.Shapes.Path;
using System.Windows.Threading;
using static VideoMixApp.Theme;

namespace VideoMixApp
{
    public enum PickerKind { Drive, Folder, File, Info }

    // One row of the picker list (a drive, folder, file or an informational line).
    public sealed class PickerEntry
    {
        public string FullPath;
        public string Name;
        public PickerKind Kind;
        public string Detail;
        public string Right;
        public bool Enabled = true;
        public int Score;
        public int Depth;
        public List<int> Highlight;
        // 规格统一 state of a video file (only when the picker chooses the source folder).
        public string Badge;
        public bool BadgeWarn;

        public bool Selectable
        {
            get { return Enabled && Kind != PickerKind.Info; }
        }
    }

    // Decides whether a folder/file name matches the search keyword, how high it ranks and which
    // characters to highlight. The helpers below do the character-level matching.
    internal sealed class NameMatcher
    {
        public readonly string Keyword;
        public readonly string[] Terms;
        public readonly bool Exact;

        private const string Separators = " 　_-.·・、，,()（）[]【】{}《》<>#&+~";
        // Separators as a lookup table: the search tests every character of every name it reads.
        private static readonly bool[] SeparatorTable = BuildSeparatorTable();

        // Terms without spaces and punctuation, prepared once instead of for every name.
        private readonly string[] _loose;

        public NameMatcher(string keyword, bool exact)
        {
            Keyword = (keyword ?? "").Trim();
            Exact = exact;
            Terms = Keyword.Split(new[] { ' ', '　', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            _loose = new string[Terms.Length];
            for (int i = 0; i < Terms.Length; i++) _loose[i] = StripSeparators(Terms[i]);
        }

        // Returns a score >= 0 when the name matches (higher is listed first) or -1 for no match.
        public int Match(string name, bool isFile, List<int> hl)
        {
            if (Keyword.Length == 0 || string.IsNullOrEmpty(name)) return -1;
            hl.Clear();
            int score = Score(name, isFile, hl);
            if (score < 0) hl.Clear();
            return score;
        }

        // name: full name as shown. Fill hl with the indices of matched characters in name.
        // 精确 = the whole name (or a file's name without extension) equals the keyword.
        // 模糊 = every space-separated term is found, trying contiguous, then ignoring
        //        spaces/punctuation, then characters in order with limited gaps; looser ranks lower.
        private int Score(string name, bool isFile, List<int> hl)
        {
            if (Exact)
            {
                string stem = isFile ? System.IO.Path.GetFileNameWithoutExtension(name) : name;
                bool whole = string.Equals(name.Trim(), Keyword, StringComparison.OrdinalIgnoreCase);
                if (!whole && !string.Equals(stem.Trim(), Keyword, StringComparison.OrdinalIgnoreCase)) return -1;
                if (whole) MarkAll(name, hl);
                else for (int i = 0; i < stem.Length; i++) hl.Add(i);
                return 1000;
            }
            int score = 0;
            for (int n = 0; n < Terms.Length; n++)
            {
                string term = Terms[n];
                int pos = FindContiguous(name, term, hl);
                if (pos >= 0)
                {
                    score += 100 - Math.Min(pos, 30) + (pos == 0 ? 20 : 0);
                    continue;
                }
                if (LooseAt(name, _loose[n], hl) >= 0)
                {
                    score += 60;
                    continue;
                }
                if (term.Length < 2) return -1;
                // Check first, record the positions only for an accepted match.
                int gaps = SubsequenceAt(name, _loose[n], null);
                if (gaps < 0 || gaps > Math.Max(3, term.Length * 2)) return -1;
                SubsequenceAt(name, _loose[n], hl);
                score += Math.Max(5, 30 - gaps);
            }
            // Only names that matched get here, so the extension is stripped for those alone.
            string bare = isFile ? System.IO.Path.GetFileNameWithoutExtension(name) : name;
            if (string.Equals(name.Trim(), Keyword, StringComparison.OrdinalIgnoreCase) || string.Equals(bare.Trim(), Keyword, StringComparison.OrdinalIgnoreCase)) score += 500;
            else if (name.StartsWith(Keyword, StringComparison.OrdinalIgnoreCase)) score += 50;
            return score;
        }

        // Case-insensitive substring search. Returns the start index or -1.
        public static int FindContiguous(string name, string term, List<int> hl)
        {
            int i = name.IndexOf(term, StringComparison.OrdinalIgnoreCase);
            if (i < 0) return -1;
            for (int k = i; k < i + term.Length; k++) hl.Add(k);
            return i;
        }

        // Like FindContiguous but ignores spaces and punctuation on both sides, so "密封 毛条" finds
        // "密封_毛条". Returns the start index in name or -1.
        public static int FindLoose(string name, string term, List<int> hl)
        {
            return LooseAt(name, StripSeparators(term), hl);
        }

        // FindLoose with t already stripped of separators. Positions go into hl only on success.
        private static int LooseAt(string name, string t, List<int> hl)
        {
            if (t.Length == 0) return -1;
            for (int s = 0; s < name.Length; s++)
            {
                if (!SameChar(name[s], t[0]) || IsSeparator(name[s])) continue;
                int k = 0;
                for (int i = s; i < name.Length && k < t.Length; i++)
                {
                    if (IsSeparator(name[i])) continue;
                    if (!SameChar(name[i], t[k])) break;
                    k++;
                }
                if (k < t.Length) continue;
                // The characters matched are exactly the non-separators from s on.
                for (int i = s, m = 0; m < t.Length; i++)
                {
                    if (IsSeparator(name[i])) continue;
                    hl.Add(i);
                    m++;
                }
                return s;
            }
            return -1;
        }

        // Characters of term appear in name in order, not necessarily adjacent ("密条" finds
        // "密封毛条"). Returns the number of skipped characters between matches, or -1.
        public static int FindSubsequence(string name, string term, List<int> hl)
        {
            return SubsequenceAt(name, StripSeparators(term), hl);
        }

        // FindSubsequence with t already stripped; hl may be null to only measure the gaps.
        private static int SubsequenceAt(string name, string t, List<int> hl)
        {
            if (t.Length == 0) return -1;
            int gaps = 0;
            int pos = 0;
            int mark = hl != null ? hl.Count : 0;
            for (int n = 0; n < t.Length; n++)
            {
                int i = pos;
                while (i < name.Length && !SameChar(name[i], t[n])) i++;
                if (i >= name.Length)
                {
                    if (hl != null) hl.RemoveRange(mark, hl.Count - mark);
                    return -1;
                }
                if (n > 0) gaps += i - pos;
                if (hl != null) hl.Add(i);
                pos = i + 1;
            }
            return gaps;
        }

        public static void MarkAll(string name, List<int> hl)
        {
            for (int i = 0; i < name.Length; i++) hl.Add(i);
        }

        private static bool[] BuildSeparatorTable()
        {
            var table = new bool[char.MaxValue + 1];
            foreach (char c in Separators) table[c] = true;
            return table;
        }

        private static bool IsSeparator(char c)
        {
            return SeparatorTable[c];
        }

        private static bool SameChar(char a, char b)
        {
            return a == b || char.ToUpperInvariant(a) == char.ToUpperInvariant(b);
        }

        private static string StripSeparators(string s)
        {
            var chars = new List<char>(s.Length);
            foreach (char c in s) if (!IsSeparator(c)) chars.Add(c);
            return new string(chars.ToArray());
        }
    }

    // Visual for one PickerEntry; created per realized (virtualized) list container.
    internal sealed class PickerRow : Grid
    {
        public static readonly DependencyProperty EntryProperty = DependencyProperty.Register(
            "Entry", typeof(PickerEntry), typeof(PickerRow), new PropertyMetadata(null, OnEntryChanged));

        private static readonly Geometry FolderGeo = Geo(IconFolder);
        private static readonly Geometry DriveGeo = Geo(IconDrive);
        private static readonly Geometry FileGeo = Geo(IconFile);
        private static readonly Geometry VideoGeo = Geo(IconVideo);
        private static readonly string[] VideoExt = { ".mp4", ".mov", ".m4v", ".mkv", ".avi", ".flv", ".wmv", ".ts", ".webm", ".mpg", ".mpeg" };

        private readonly Path _icon = new Path();
        private readonly TextBlock _name = new TextBlock();
        private readonly TextBlock _detail = new TextBlock();
        private readonly TextBlock _right = new TextBlock();
        private readonly Chamfer _badge = new Chamfer { Cut = 4.0, Corners = Cuts.TopLeft, BorderThickness = 1.0 };
        private readonly TextBlock _badgeText = new TextBlock();

        public PickerRow()
        {
            ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
            ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _badge.Padding = new Thickness(6.0, 1.0, 6.0, 2.0);
            _badge.Margin = new Thickness(10.0, 0.0, 0.0, 0.0);
            _badge.VerticalAlignment = VerticalAlignment.Center;
            _badgeText.FontSize = 11.0;
            _badge.Child = _badgeText;
            _badge.Visibility = Visibility.Collapsed;
            _icon.Width = 18.0;
            _icon.Height = 18.0;
            _icon.Stretch = Stretch.Uniform;
            _icon.VerticalAlignment = VerticalAlignment.Center;
            _icon.Margin = new Thickness(0.0, 0.0, 10.0, 0.0);
            var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            _name.FontSize = 13.0;
            _name.TextTrimming = TextTrimming.CharacterEllipsis;
            _detail.FontSize = 11.5;
            _detail.Fg(K.TextHint);
            _detail.TextTrimming = TextTrimming.CharacterEllipsis;
            _detail.Margin = new Thickness(0.0, 1.0, 0.0, 0.0);
            texts.Children.Add(_name);
            texts.Children.Add(_detail);
            _right.FontSize = 11.5;
            _right.Fg(K.TextHint);
            _right.VerticalAlignment = VerticalAlignment.Center;
            _right.Margin = new Thickness(12.0, 0.0, 0.0, 0.0);
            // Fixed minimum so the 已统一/未统一 badges line up in one column.
            _right.MinWidth = 58.0;
            _right.TextAlignment = TextAlignment.Right;
            SetColumn(_icon, 0);
            SetColumn(texts, 1);
            SetColumn(_badge, 2);
            SetColumn(_right, 3);
            Children.Add(_icon);
            Children.Add(texts);
            Children.Add(_badge);
            Children.Add(_right);
        }

        private static void OnEntryChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((PickerRow)d).Apply(e.NewValue as PickerEntry);
        }

        private void Apply(PickerEntry e)
        {
            if (e == null) return;
            switch (e.Kind)
            {
                case PickerKind.Drive:
                    _icon.Data = DriveGeo;
                    _icon.Fill(K.Accent);
                    break;
                case PickerKind.Folder:
                    _icon.Data = FolderGeo;
                    _icon.Fill(K.Folder);
                    break;
                case PickerKind.File:
                    bool video = Array.IndexOf(VideoExt, (System.IO.Path.GetExtension(e.Name) ?? "").ToLowerInvariant()) >= 0;
                    _icon.Data = video ? VideoGeo : FileGeo;
                    _icon.Fill(video ? K.Video : K.TextHint);
                    break;
                default:
                    _icon.Data = null;
                    break;
            }
            _icon.Visibility = e.Kind == PickerKind.Info ? Visibility.Collapsed : Visibility.Visible;
            _name.Fg(e.Kind == PickerKind.Info ? K.TextHint : (e.Kind == PickerKind.File ? K.TextSub : K.Text));
            _name.Inlines.Clear();
            if (e.Highlight == null || e.Highlight.Count == 0)
            {
                _name.Inlines.Add(new Run(e.Name ?? ""));
            }
            else
            {
                var mark = new bool[e.Name.Length];
                foreach (int i in e.Highlight) if (i >= 0 && i < mark.Length) mark[i] = true;
                int start = 0;
                while (start < mark.Length)
                {
                    int end = start;
                    while (end < mark.Length && mark[end] == mark[start]) end++;
                    var run = new Run(e.Name.Substring(start, end - start));
                    if (mark[start])
                    {
                        run.FontWeight = FontWeights.Bold;
                        run.SetResourceReference(TextElement.ForegroundProperty, K.Accent);
                    }
                    _name.Inlines.Add(run);
                    start = end;
                }
            }
            _detail.Text = e.Detail ?? "";
            _detail.Visibility = string.IsNullOrEmpty(e.Detail) ? Visibility.Collapsed : Visibility.Visible;
            _badge.Visibility = string.IsNullOrEmpty(e.Badge) ? Visibility.Collapsed : Visibility.Visible;
            _badgeText.Text = e.Badge ?? "";
            string tone = e.BadgeWarn ? K.Warn : K.Keep;
            _badgeText.Fg(tone);
            _badge.Bg(K.Soft(tone)).Bd(K.Edge(tone));
            _right.Text = e.Right ?? "";
            ToolTip = e.Kind == PickerKind.Info ? null : e.FullPath;
        }
    }

    // Folder chooser used by every "浏览" button for folders.
    internal sealed class FolderPicker : Window
    {
        private const string ThisPC = "ThisPC";
        private const int MaxHits = 3000;

        private static bool _lastExact;
        private static bool _lastRecursive = true;
        private static bool _lastShowFiles = true;

        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
        private static extern int StrCmpLogicalW(string a, string b);

        private sealed class SearchRun
        {
            public string Keyword;
            public string Scope;
            public NameMatcher Matcher;
            public bool Recursive;
            public bool IncludeFiles;
            public Task<UnifyRecord> RecordTask;
            public UnifyRecord Record;
            public CancellationToken Token;
            public readonly ConcurrentQueue<PickerEntry> Hits = new ConcurrentQueue<PickerEntry>();
            // Highlight positions of the name being tested; only the search thread uses it.
            public readonly List<int> Scratch = new List<int>();
            public readonly Stopwatch Clock = Stopwatch.StartNew();
            public int ScannedDirs;
            public int Found;
            public volatile bool Finished;
            public volatile bool Truncated;
            public volatile bool Stopped;
        }

        private sealed class Listing
        {
            public readonly List<PickerEntry> Entries = new List<PickerEntry>();
            public int Folders;
            public int Files;
            public string Error;
        }

        private string _currentDir;
        private string _selectedPath;
        private string _selectAfterLoad;
        private bool _suppressSearch;
        private TextBox _addr;
        private TextBox _search;
        private TextBlock _hint;
        private Button _clearBtn;
        private ToggleButton _fuzzyBtn;
        private ToggleButton _exactBtn;
        private CheckBox _subCheck;
        private CheckBox _filesCheck;
        private TextBlock _status;
        private Button _stopBtn;
        private ListBox _list;
        private TextBlock _selText;
        private TextBlock _unifyText;
        private ObservableCollection<PickerEntry> _items = new ObservableCollection<PickerEntry>();
        private readonly DispatcherTimer _debounce;
        private readonly DispatcherTimer _pump;
        private readonly DispatcherTimer _checkTimer;
        private CancellationTokenSource _cts;
        private SearchRun _run;
        // 规格统一 record of the chosen ratio, loaded in the background; null when the picker is
        // used for another folder (output).
        private readonly Task<UnifyRecord> _record;
        private int _checkSeq;

        public string SelectedPath
        {
            get { return _selectedPath; }
        }

        public FolderPicker(string initialPath) : this(initialPath, null, null, 0)
        {
        }

        public FolderPicker(string initialPath, string unifyCacheDir, string unifyMark) : this(initialPath, unifyCacheDir, unifyMark, 0)
        {
        }

        // unifyCacheDir / unifyMark: the 规格统一 record and mark of the chosen ratio (see UnifyRecord);
        // floorKbps: the bitrate floor clips are raised to (v2.7.0), 0 = none.
        public FolderPicker(string initialPath, string unifyCacheDir, string unifyMark, int floorKbps)
        {
            if (unifyCacheDir != null) _record = Task.Factory.StartNew(() => UnifyRecord.Load(unifyCacheDir, unifyMark, floorKbps));
            _checkTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
            _checkTimer.Tick += (s, e) => CheckTarget();
            Rect work = SystemParameters.WorkArea;
            Width = Math.Min(700.0, work.Width - 40.0);
            Height = Math.Min(740.0, work.Height - 40.0);
            MinWidth = Math.Min(540.0, Width);
            MinHeight = Math.Min(480.0, Height);
            Title = "选择文件夹";
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.CanResize;
            ShowInTaskbar = false;
            FontFamily = new FontFamily("Microsoft YaHei");
            UseLayoutRounding = true;
            _currentDir = ResolveInitial(initialPath);
            _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(280) };
            _debounce.Tick += (s, e) =>
            {
                _debounce.Stop();
                StartSearch();
            };
            _pump = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _pump.Tick += (s, e) => PumpResults();
            BuildUI();
            WindowResizer.Enable(this, 10.0);
            PreviewKeyDown += OnPreviewKey;
            Loaded += (s, e) => _search.Focus();
            Closed += (s, e) =>
            {
                _checkTimer.Stop();
                CancelWork();
            };
            UpdateHint();
            LoadDir();
        }

        // Starts from the given folder; if it no longer exists, from its nearest existing parent.
        private static string ResolveInitial(string p)
        {
            try
            {
                if (!string.IsNullOrEmpty(p))
                {
                    string dir = System.IO.Path.GetFullPath(p.Trim().Trim('"'));
                    while (!string.IsNullOrEmpty(dir))
                    {
                        if (Directory.Exists(dir)) return dir;
                        dir = System.IO.Path.GetDirectoryName(dir);
                    }
                }
            }
            catch
            {
            }
            return ThisPC;
        }

        private void BuildUI()
        {
            Ensure(this);
            var root = new Grid { Background = SB(1, 0, 0, 0) };
            Chamfer frame = DialogFrame(root);
            var g = new Grid();
            for (int i = 0; i < 6; i++) g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.RowDefinitions[4].Height = new GridLength(1.0, GridUnitType.Star);
            frame.Child = g;
            Content = root;

            Style secondary = MainWindow.MakeSecondaryButtonStyle();
            Style input = MainWindow.MakeTextBoxStyle();
            Style check = MainWindow.MakeCheckBoxStyle();

            // Row 0: title bar (drag to move).
            var titleBar = new Border { Padding = new Thickness(18.0, 10.0, 10.0, 8.0), Margin = new Thickness(0.0, 0.0, 0.0, 8.0) }.Bg(K.Clear);
            var tg = new Grid();
            tg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
            tg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var titleStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            titleStack.Children.Add(MakeIcon(IconFolder, K.Folder, 18.0));
            titleStack.Children.Add(new TextBlock
            {
                Text = "选择文件夹",
                FontSize = 14.5,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(9.0, 0.0, 0.0, 0.0),
                VerticalAlignment = VerticalAlignment.Center
            }.Fg(K.Text));
            // Hairline under the title with a lit segment, as in the main window.
            var under = new Grid { Height = 1.0, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(-2.0, 0.0, 6.0, -8.0) };
            under.Children.Add(new Border().Bg(K.Line));
            under.Children.Add(new Border { Width = 90.0, HorizontalAlignment = HorizontalAlignment.Left }.Bg(K.Accent));
            Grid.SetColumnSpan(under, 2);
            tg.Children.Add(under);
            Button close = MainWindow.MakeWinButton(IconClose, true);
            close.Click += (s, e) => DialogResult = false;
            Grid.SetColumn(close, 1);
            tg.Children.Add(titleStack);
            tg.Children.Add(close);
            titleBar.Child = tg;
            titleBar.MouseLeftButtonDown += (s, e) =>
            {
                try { DragMove(); } catch { }
            };
            Grid.SetRow(titleBar, 0);
            g.Children.Add(titleBar);

            // Row 1: navigation (up, this PC, editable address).
            var nav = new Grid { Margin = new Thickness(16.0, 4.0, 16.0, 10.0) };
            nav.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            nav.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            nav.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
            Button up = ToolButton(IconArrowUp, "上一级", secondary);
            up.ToolTip = "返回上一级（Backspace）";
            up.Click += (s, e) => GoUp();
            Button pc = ToolButton(IconComputer, "此电脑", secondary);
            pc.Click += (s, e) => Navigate(ThisPC, null);
            // TextBox padding is applied twice by this template (host margin + text view), so values are halved.
            _addr = new TextBox
            {
                Height = 32.0,
                VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(5.0, 0.0, 5.0, 0.0),
                Style = input,
                FontSize = 12.5,
                ToolTip = "可直接输入或粘贴路径，按回车跳转"
            };
            _addr.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    GoToTyped();
                    e.Handled = true;
                }
            };
            Grid.SetColumn(up, 0);
            Grid.SetColumn(pc, 1);
            Grid.SetColumn(_addr, 2);
            nav.Children.Add(up);
            nav.Children.Add(pc);
            nav.Children.Add(_addr);
            Grid.SetRow(nav, 1);
            g.Children.Add(nav);

            // Row 2: search box + fuzzy/exact switch.
            var sr = new Grid { Margin = new Thickness(16.0, 0.0, 16.0, 8.0) };
            sr.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
            sr.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var host = new Grid();
            _search = new TextBox
            {
                Height = 34.0,
                VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(17.0, 0.0, 15.0, 0.0),
                Style = input,
                FontSize = 13.0
            };
            _search.TextChanged += OnSearchTextChanged;
            _search.PreviewKeyDown += OnSearchKey;
            Path searchIcon = MakeIcon(IconSearch, K.TextHint, 16.0);
            searchIcon.HorizontalAlignment = HorizontalAlignment.Left;
            searchIcon.Margin = new Thickness(12.0, 0.0, 0.0, 0.0);
            searchIcon.IsHitTestVisible = false;
            _hint = new TextBlock
            {
                FontSize = 13.0,
                IsHitTestVisible = false,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(36.0, 0.0, 30.0, 0.0),
                TextTrimming = TextTrimming.CharacterEllipsis
            }.Fg(K.TextHint);
            _clearBtn = new Button
            {
                Width = 24.0,
                Height = 24.0,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0.0, 0.0, 5.0, 0.0),
                Focusable = false,
                Visibility = Visibility.Collapsed,
                ToolTip = "清除搜索（Esc）",
                Cursor = Cursors.Hand,
                Template = MainWindow.ButtonTpl(K.Clear, K.Panel3, K.AccentSoft, null, null, 5.0),
                Content = new Path { Data = Geo(IconClose), StrokeThickness = 1.6, Width = 10.0, Height = 10.0, Stretch = Stretch.Uniform }.Stroke(K.TextSub)
            };
            _clearBtn.Click += (s, e) =>
            {
                _search.Text = "";
                _search.Focus();
            };
            host.Children.Add(_search);
            host.Children.Add(searchIcon);
            host.Children.Add(_hint);
            host.Children.Add(_clearBtn);
            Grid.SetColumn(host, 0);
            sr.Children.Add(host);

            var seg = new Chamfer
            {
                Cut = 7.0,
                Corners = Cuts.TopRight,
                BorderThickness = 1.0,
                Margin = new Thickness(8.0, 0.0, 0.0, 0.0),
                Padding = new Thickness(3.0),
                Height = 34.0
            }.Bg(K.Panel2).Bd(K.Line);
            var segPanel = new StackPanel { Orientation = Orientation.Horizontal };
            Style segStyle = SegmentStyle();
            _fuzzyBtn = new ToggleButton
            {
                Content = "模糊",
                Style = segStyle,
                ToolTip = "模糊搜索：名称中含有关键词即可；可用空格分隔多个关键词，忽略空格与符号，字符不连续也能找到"
            };
            _exactBtn = new ToggleButton
            {
                Content = "精确",
                Style = segStyle,
                ToolTip = "精确搜索：名称与关键词完全一致（不区分大小写，文件可省略扩展名）"
            };
            _fuzzyBtn.IsChecked = !_lastExact;
            _exactBtn.IsChecked = _lastExact;
            _fuzzyBtn.Click += (s, e) => SetMode(false);
            _exactBtn.Click += (s, e) => SetMode(true);
            segPanel.Children.Add(_fuzzyBtn);
            segPanel.Children.Add(_exactBtn);
            seg.Child = segPanel;
            Grid.SetColumn(seg, 1);
            sr.Children.Add(seg);
            Grid.SetRow(sr, 2);
            g.Children.Add(sr);

            // Row 3: options and live status.
            var opt = new Grid { Margin = new Thickness(18.0, 0.0, 16.0, 8.0) };
            opt.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            opt.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            opt.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
            opt.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _subCheck = new CheckBox
            {
                Style = check,
                IsChecked = _lastRecursive,
                Margin = new Thickness(0.0, 0.0, 18.0, 0.0),
                Content = new TextBlock { Text = "包含子文件夹", FontSize = 12.5 }.Fg(K.Text),
                ToolTip = "勾选：搜索当前位置下的所有层级；取消：只筛选当前这一层"
            };
            _filesCheck = new CheckBox
            {
                Style = check,
                IsChecked = _lastShowFiles,
                Content = new TextBlock { Text = "显示文件", FontSize = 12.5 }.Fg(K.Text),
                ToolTip = "在列表和搜索结果中同时显示文件（只用于查看，选择的仍是文件夹）"
            };
            _subCheck.Click += (s, e) => OptionsChanged();
            _filesCheck.Click += (s, e) => OptionsChanged();
            _status = new TextBlock
            {
                FontSize = 12.0,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(12.0, 0.0, 6.0, 0.0)
            }.Fg(K.TextHint);
            _stopBtn = new Button
            {
                Content = "停止",
                Height = 26.0,
                Width = 50.0,
                FontSize = 12.0,
                Style = secondary,
                Visibility = Visibility.Collapsed
            };
            _stopBtn.Click += (s, e) => StopSearch();
            Grid.SetColumn(_subCheck, 0);
            Grid.SetColumn(_filesCheck, 1);
            Grid.SetColumn(_status, 2);
            Grid.SetColumn(_stopBtn, 3);
            opt.Children.Add(_subCheck);
            opt.Children.Add(_filesCheck);
            opt.Children.Add(_status);
            opt.Children.Add(_stopBtn);
            Grid.SetRow(opt, 3);
            g.Children.Add(opt);

            // Row 4: the list. It sits in a star row, so it gets a bounded height and scrolls.
            var listBorder = new Chamfer
            {
                BorderThickness = 1.0,
                Cut = 10.0,
                Corners = Cuts.TopLeft | Cuts.BottomRight,
                Margin = new Thickness(16.0, 0.0, 16.0, 0.0),
                Padding = new Thickness(0.0, 4.0, 2.0, 4.0)
            }.Bg(K.Panel2).Bd(K.Line);
            _list = new ListBox
            {
                BorderThickness = new Thickness(0.0),
                Background = Brushes.Transparent,
                ItemsSource = _items,
                ItemContainerStyle = ItemStyle(),
                ItemTemplate = RowTemplate(),
                HorizontalContentAlignment = HorizontalAlignment.Stretch
            };
            ScrollViewer.SetHorizontalScrollBarVisibility(_list, ScrollBarVisibility.Disabled);
            ScrollViewer.SetVerticalScrollBarVisibility(_list, ScrollBarVisibility.Auto);
            ScrollViewer.SetCanContentScroll(_list, true);
            VirtualizingPanel.SetIsVirtualizing(_list, true);
            VirtualizingPanel.SetVirtualizationMode(_list, VirtualizationMode.Recycling);
            VirtualizingPanel.SetScrollUnit(_list, ScrollUnit.Pixel);
            _list.Resources.Add(typeof(ScrollBar), ScrollBarStyle());
            _list.MouseDoubleClick += OnListDoubleClick;
            _list.SelectionChanged += (s, e) => UpdateSelText();
            _list.PreviewKeyDown += OnListKey;
            listBorder.Child = _list;
            Grid.SetRow(listBorder, 4);
            g.Children.Add(listBorder);

            // Row 5: footer with the chosen path and the buttons.
            var foot = new Border
            {
                BorderThickness = new Thickness(0.0, 1.0, 0.0, 0.0),
                Padding = new Thickness(18.0, 12.0, 16.0, 14.0),
                Margin = new Thickness(0.0, 12.0, 0.0, 0.0)
            }.Bd(K.Line);
            var fg = new Grid();
            fg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
            fg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            fg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var selPanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0.0, 0.0, 12.0, 0.0) };
            _selText = new TextBlock
            {
                FontSize = 12.5,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            _unifyText = new TextBlock
            {
                FontSize = 12.0,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0.0, 3.0, 0.0, 0.0),
                Visibility = Visibility.Collapsed
            };
            selPanel.Children.Add(_selText);
            selPanel.Children.Add(_unifyText);
            var cancel = new Button { Content = "取消", Width = 84.0, Height = 36.0, FontSize = 13.0, Margin = new Thickness(0.0, 0.0, 10.0, 0.0), Style = secondary };
            cancel.Click += (s, e) => DialogResult = false;
            var ok = new Button { Width = 96.0, Height = 36.0, Style = ConfirmStyle() };
            ok.Content = new TextBlock { Text = "确定", FontSize = 13.5, FontWeight = FontWeights.SemiBold };
            ok.ToolTip = "选择当前选中的文件夹（Ctrl+Enter）";
            ok.Click += (s, e) => Confirm();
            Grid.SetColumn(selPanel, 0);
            Grid.SetColumn(cancel, 1);
            Grid.SetColumn(ok, 2);
            fg.Children.Add(selPanel);
            fg.Children.Add(cancel);
            fg.Children.Add(ok);
            foot.Child = fg;
            Grid.SetRow(foot, 5);
            g.Children.Add(foot);
        }

        private static Button ToolButton(string icon, string text, Style style)
        {
            var b = new Button { Height = 32.0, Padding = new Thickness(10.0, 0.0, 12.0, 0.0), Margin = new Thickness(0.0, 0.0, 8.0, 0.0), Style = style, FontSize = 12.5 };
            var p = new StackPanel { Orientation = Orientation.Horizontal };
            p.Children.Add(MakeIcon(icon, K.Accent, 14.0));
            p.Children.Add(new TextBlock { Text = text, Margin = new Thickness(6.0, 0.0, 0.0, 0.0), VerticalAlignment = VerticalAlignment.Center });
            b.Content = p;
            return b;
        }

        private static Style ConfirmStyle()
        {
            return MainWindow.MakePrimaryButtonStyle();
        }

        // 模糊 / 精确: cut-corner keys inside one frame, the chosen one filled.
        private static Style SegmentStyle()
        {
            var bd = new FrameworkElementFactory(typeof(Chamfer), "sb");
            bd.SetResourceReference(Chamfer.BackgroundProperty, K.Clear);
            bd.SetValue(Chamfer.BorderThicknessProperty, 0.0);
            bd.SetValue(Chamfer.CutProperty, 5.0);
            bd.SetValue(Chamfer.CornersProperty, Cuts.TopRight);
            bd.SetValue(Chamfer.PaddingProperty, new Thickness(14.0, 0.0, 14.0, 0.0));
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            cp.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            bd.AppendChild(cp);
            var tpl = new ControlTemplate(typeof(ToggleButton)) { VisualTree = bd };
            var over = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            over.Setters.Add(DynSetter(Chamfer.BackgroundProperty, K.Panel3, "sb"));
            tpl.Triggers.Add(over);
            var on = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true };
            on.Setters.Add(DynSetter(Chamfer.BackgroundProperty, K.Accent, "sb"));
            on.Setters.Add(DynSetter(Control.ForegroundProperty, K.OnAccent));
            tpl.Triggers.Add(on);
            var s = new Style(typeof(ToggleButton));
            s.Setters.Add(DynSetter(Control.ForegroundProperty, K.TextSub));
            s.Setters.Add(new Setter(Control.FontSizeProperty, 12.5));
            s.Setters.Add(new Setter(FrameworkElement.CursorProperty, Cursors.Hand));
            s.Setters.Add(new Setter(UIElement.FocusableProperty, false));
            s.Setters.Add(new Setter(Control.TemplateProperty, tpl));
            return s;
        }

        // List rows: a bar on the left lights up for the selected row.
        internal static Style ItemStyle()
        {
            var root = new FrameworkElementFactory(typeof(Grid));
            root.SetValue(FrameworkElement.MarginProperty, new Thickness(6.0, 0.5, 4.0, 0.5));
            var ib = new FrameworkElementFactory(typeof(Chamfer), "ib");
            ib.SetResourceReference(Chamfer.BackgroundProperty, K.Clear);
            ib.SetResourceReference(Chamfer.BorderBrushProperty, K.Clear);
            ib.SetValue(Chamfer.BorderThicknessProperty, 1.0);
            ib.SetValue(Chamfer.CutProperty, 7.0);
            ib.SetValue(Chamfer.CornersProperty, Cuts.BottomRight);
            ib.SetValue(Chamfer.PaddingProperty, new Thickness(10.0, 3.0, 12.0, 3.0));
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            ib.AppendChild(cp);
            root.AppendChild(ib);
            var bar = new FrameworkElementFactory(typeof(Border), "bar");
            bar.SetValue(FrameworkElement.WidthProperty, 3.0);
            bar.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
            bar.SetValue(FrameworkElement.MarginProperty, new Thickness(0.0, 5.0, 0.0, 5.0));
            bar.SetResourceReference(Border.BackgroundProperty, K.Accent);
            bar.SetValue(UIElement.VisibilityProperty, Visibility.Hidden);
            root.AppendChild(bar);
            var tpl = new ControlTemplate(typeof(ListBoxItem)) { VisualTree = root };
            var over = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            over.Setters.Add(DynSetter(Chamfer.BackgroundProperty, K.Panel3, "ib"));
            tpl.Triggers.Add(over);
            var sel = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
            sel.Setters.Add(DynSetter(Chamfer.BackgroundProperty, K.AccentSoft, "ib"));
            sel.Setters.Add(DynSetter(Chamfer.BorderBrushProperty, K.AccentLine, "ib"));
            sel.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible, "bar"));
            tpl.Triggers.Add(sel);
            var s = new Style(typeof(ListBoxItem));
            s.Setters.Add(new Setter(Control.TemplateProperty, tpl));
            s.Setters.Add(new Setter(FrameworkElement.FocusVisualStyleProperty, null));
            s.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
            s.Setters.Add(new Setter(UIElement.SnapsToDevicePixelsProperty, true));
            var disabled = new DataTrigger { Binding = new Binding("Selectable"), Value = false };
            disabled.Setters.Add(new Setter(UIElement.IsEnabledProperty, false));
            s.Triggers.Add(disabled);
            return s;
        }

        private static DataTemplate RowTemplate()
        {
            var row = new FrameworkElementFactory(typeof(PickerRow));
            row.SetBinding(PickerRow.EntryProperty, new Binding());
            return new DataTemplate { VisualTree = row };
        }

        // A slim, square scrollbar that is still easy to grab; the thumb lights up while dragging. Only
        // vertical bars take it (a horizontal one keeps the system look), and a bar with nothing to
        // scroll shows nothing (v2.7.1: lists that always reserve the bar's room showed a full-length
        // thumb). dark: on the 运行日志 background (always dark, whatever the 背景色).
        internal static Style ScrollBarStyle(bool dark = false)
        {
            string track = dark ? "{DynamicResource ax.logBg}" : "{DynamicResource ax.panel2}";
            string thumb = dark ? "{DynamicResource ax.logDim}" : "{DynamicResource ax.lineHi}";
            string hover = "{DynamicResource ax.textHint}";
            string xaml =
                "<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='ScrollBar'>" +
                "<Style.Triggers><Trigger Property='Orientation' Value='Vertical'>" +
                "<Setter Property='Width' Value='12'/><Setter Property='MinWidth' Value='12'/>" +
                "<Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ScrollBar'>" +
                "<Grid Background='Transparent'>" +
                "<Border x:Name='bg' Background='" + track + "' Margin='3,2'/>" +
                "<Track x:Name='PART_Track' IsDirectionReversed='True' Margin='3,2'>" +
                "<Track.DecreaseRepeatButton><RepeatButton Command='ScrollBar.PageUpCommand' Focusable='False' IsTabStop='False'>" +
                "<RepeatButton.Template><ControlTemplate TargetType='RepeatButton'><Border Background='Transparent'/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.DecreaseRepeatButton>" +
                "<Track.Thumb><Thumb MinHeight='36'><Thumb.Template><ControlTemplate TargetType='Thumb'>" +
                "<Border x:Name='tb' Background='" + thumb + "'/>" +
                "<ControlTemplate.Triggers>" +
                "<Trigger Property='IsMouseOver' Value='True'><Setter TargetName='tb' Property='Background' Value='" + hover + "'/></Trigger>" +
                "<Trigger Property='IsDragging' Value='True'><Setter TargetName='tb' Property='Background' Value='{DynamicResource ax.accent}'/></Trigger>" +
                "</ControlTemplate.Triggers></ControlTemplate></Thumb.Template></Thumb></Track.Thumb>" +
                "<Track.IncreaseRepeatButton><RepeatButton Command='ScrollBar.PageDownCommand' Focusable='False' IsTabStop='False'>" +
                "<RepeatButton.Template><ControlTemplate TargetType='RepeatButton'><Border Background='Transparent'/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.IncreaseRepeatButton>" +
                "</Track></Grid>" +
                "<ControlTemplate.Triggers><Trigger Property='Maximum' Value='0'>" +
                "<Setter TargetName='bg' Property='Visibility' Value='Hidden'/><Setter TargetName='PART_Track' Property='Visibility' Value='Hidden'/>" +
                "</Trigger></ControlTemplate.Triggers>" +
                "</ControlTemplate></Setter.Value></Setter>" +
                "</Trigger></Style.Triggers></Style>";
            return (Style)XamlReader.Parse(xaml);
        }

        private string ScopeName()
        {
            if (_currentDir == ThisPC) return "此电脑（所有磁盘）";
            string trimmed = _currentDir.TrimEnd('\\');
            string name = System.IO.Path.GetFileName(trimmed);
            return string.IsNullOrEmpty(name) ? trimmed : name;
        }

        private void UpdateHint()
        {
            bool empty = _search.Text.Length == 0;
            _hint.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
            _clearBtn.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
            _hint.Text = "在「" + ScopeName() + "」中搜索" + (_filesCheck.IsChecked == true ? "文件夹或文件" : "文件夹") + "…";
            _search.ToolTip = "搜索范围：" + (_currentDir == ThisPC ? "此电脑（所有磁盘）" : _currentDir);
        }

        private void SetStatus(string text, bool warn)
        {
            _status.Text = text ?? "";
            _status.Fg(warn ? K.Warn : K.TextHint);
            _status.ToolTip = string.IsNullOrEmpty(text) ? null : text;
        }

        private void SetMode(bool exact)
        {
            _lastExact = exact;
            _fuzzyBtn.IsChecked = !exact;
            _exactBtn.IsChecked = exact;
            if (_search.Text.Trim().Length > 0) StartSearch();
        }

        private void OptionsChanged()
        {
            _lastRecursive = _subCheck.IsChecked == true;
            _lastShowFiles = _filesCheck.IsChecked == true;
            UpdateHint();
            if (_search.Text.Trim().Length > 0) StartSearch();
            else LoadDir();
        }

        private void CancelWork()
        {
            if (_cts != null)
            {
                _cts.Cancel();
                _cts = null;
            }
            _run = null;
            _pump.Stop();
            _stopBtn.Visibility = Visibility.Collapsed;
        }

        private void Navigate(string dir, string selectAfter)
        {
            _debounce.Stop();
            CancelWork();
            _currentDir = dir;
            _selectAfterLoad = selectAfter;
            if (_search.Text.Length > 0)
            {
                _suppressSearch = true;
                _search.Text = "";
                _suppressSearch = false;
            }
            UpdateHint();
            LoadDir();
        }

        private void GoUp()
        {
            if (_currentDir == ThisPC) return;
            string trimmed = _currentDir.TrimEnd('\\');
            string parent = System.IO.Path.GetDirectoryName(trimmed);
            Navigate(string.IsNullOrEmpty(parent) ? ThisPC : parent, _currentDir);
        }

        private void GoToTyped()
        {
            string text = _addr.Text.Trim().Trim('"');
            if (text.Length == 0 || text == "此电脑")
            {
                Navigate(ThisPC, null);
                return;
            }
            try
            {
                if (text.Length == 2 && text[1] == ':') text += "\\";
                string full = System.IO.Path.GetFullPath(text);
                if (Directory.Exists(full))
                {
                    Navigate(full, null);
                    return;
                }
                if (File.Exists(full))
                {
                    Navigate(System.IO.Path.GetDirectoryName(full), full);
                    return;
                }
            }
            catch
            {
            }
            SetStatus("路径不存在：" + text, true);
        }

        private void LoadDir()
        {
            CancelWork();
            var cts = new CancellationTokenSource();
            _cts = cts;
            string dir = _currentDir;
            bool files = _filesCheck.IsChecked == true;
            Task<UnifyRecord> record = _record;
            _addr.Text = dir == ThisPC ? "此电脑" : dir;
            SetStatus("正在读取…", false);
            Task.Factory.StartNew(() =>
            {
                Listing r = ReadDir(dir, files, files ? Resolve(record) : null);
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (cts.IsCancellationRequested) return;
                    _items = new ObservableCollection<PickerEntry>(r.Entries);
                    _list.ItemsSource = _items;
                    if (r.Error != null) SetStatus("无法读取：" + r.Error, true);
                    else if (dir == ThisPC) SetStatus(r.Folders + " 个驱动器", false);
                    else SetStatus(r.Folders + " 个文件夹" + (files ? "，" + r.Files + " 个文件" : ""), false);
                    SelectPath(_selectAfterLoad);
                    _selectAfterLoad = null;
                    UpdateSelText();
                }));
            }, cts.Token, TaskCreationOptions.None, TaskScheduler.Default);
        }

        private void SelectPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            string want = path.TrimEnd('\\');
            foreach (var e in _items)
            {
                if (e.FullPath != null && string.Equals(e.FullPath.TrimEnd('\\'), want, StringComparison.OrdinalIgnoreCase))
                {
                    _list.SelectedItem = e;
                    _list.ScrollIntoView(e);
                    return;
                }
            }
        }

        private static UnifyRecord Resolve(Task<UnifyRecord> record)
        {
            try
            {
                return record != null ? record.Result : null;
            }
            catch
            {
                return null;
            }
        }

        // Marks a source video with its 规格统一 state (only files the engine reads as sources).
        private static void SetBadge(PickerEntry e, UnifyRecord rec, FileInfo f)
        {
            if (rec == null || f == null || !MixEngine.IsSourceVideo(f.Name)) return;
            UnifyRecord.State s;
            try { s = rec.Check(f); }
            catch { return; }
            switch (s)
            {
                case UnifyRecord.State.Done:
                case UnifyRecord.State.Cached:
                    e.Badge = "已统一";
                    break;
                case UnifyRecord.State.Changed:
                    e.Badge = "已改动";
                    e.BadgeWarn = true;
                    break;
                case UnifyRecord.State.Failed:
                    e.Badge = "上次失败";
                    e.BadgeWarn = true;
                    break;
                case UnifyRecord.State.LowRate:
                    e.Badge = "码率低";
                    e.BadgeWarn = true;
                    break;
                default:
                    e.Badge = "未统一";
                    e.BadgeWarn = true;
                    break;
            }
        }

        private static Listing ReadDir(string dir, bool includeFiles, UnifyRecord rec)
        {
            var r = new Listing();
            if (dir == ThisPC)
            {
                DriveInfo[] drives;
                try { drives = DriveInfo.GetDrives(); }
                catch (Exception ex) { r.Error = ex.Message; return r; }
                Array.Sort(drives, (a, b) => string.CompareOrdinal(a.Name, b.Name));
                foreach (var d in drives)
                {
                    bool ready;
                    try { ready = d.IsReady; } catch { ready = false; }
                    string letter = d.Name.TrimEnd('\\');
                    var e = new PickerEntry { Kind = PickerKind.Drive, FullPath = d.Name, Depth = 0 };
                    if (!ready)
                    {
                        e.Name = DriveKindName(d.DriveType) + " (" + letter + ")";
                        e.Right = "未就绪";
                        e.Enabled = false;
                    }
                    else
                    {
                        string label = null;
                        try { label = d.VolumeLabel; } catch { }
                        e.Name = (string.IsNullOrEmpty(label) ? DriveKindName(d.DriveType) : label) + " (" + letter + ")";
                        try { e.Right = "可用 " + FormatSize(d.AvailableFreeSpace) + " / 共 " + FormatSize(d.TotalSize); } catch { }
                    }
                    r.Entries.Add(e);
                    r.Folders++;
                }
                return r;
            }
            try
            {
                var di = new DirectoryInfo(dir);
                var dirs = new List<DirectoryInfo>();
                foreach (var d in di.EnumerateDirectories())
                {
                    FileAttributes a;
                    try { a = d.Attributes; } catch { continue; }
                    if ((a & FileAttributes.Hidden) != 0 && (a & FileAttributes.System) != 0) continue;
                    dirs.Add(d);
                }
                dirs.Sort((a, b) => StrCmpLogicalW(a.Name, b.Name));
                foreach (var d in dirs) r.Entries.Add(new PickerEntry { Kind = PickerKind.Folder, FullPath = d.FullName, Name = d.Name, Depth = 0 });
                r.Folders = dirs.Count;
                if (includeFiles)
                {
                    var files = new List<FileInfo>();
                    foreach (var f in di.EnumerateFiles())
                    {
                        FileAttributes a;
                        try { a = f.Attributes; } catch { continue; }
                        if ((a & FileAttributes.Hidden) != 0) continue;
                        files.Add(f);
                    }
                    files.Sort((a, b) => StrCmpLogicalW(a.Name, b.Name));
                    if (rec != null)
                    {
                        // Videos never seen before are opened once for their mark: several at a time.
                        var videos = files.FindAll(f => MixEngine.IsSourceVideo(f.Name));
                        Parallel.ForEach(videos, new ParallelOptions { MaxDegreeOfParallelism = 8 }, f => Unified.Read(f));
                    }
                    foreach (var f in files)
                    {
                        long len = 0;
                        try { len = f.Length; } catch { }
                        var e = new PickerEntry { Kind = PickerKind.File, FullPath = f.FullName, Name = f.Name, Right = FormatSize(len) };
                        SetBadge(e, rec, f);
                        r.Entries.Add(e);
                    }
                    r.Files = files.Count;
                }
                if (r.Entries.Count == 0) r.Entries.Add(new PickerEntry { Kind = PickerKind.Info, Name = includeFiles ? "（此文件夹是空的）" : "（此文件夹下没有子文件夹）" });
            }
            catch (Exception ex)
            {
                r.Error = ex.Message;
                r.Entries.Add(new PickerEntry { Kind = PickerKind.Info, Name = "无法读取目录：" + ex.Message });
            }
            return r;
        }

        private static string DriveKindName(DriveType t)
        {
            switch (t)
            {
                case DriveType.Fixed: return "本地磁盘";
                case DriveType.Removable: return "可移动磁盘";
                case DriveType.Network: return "网络驱动器";
                case DriveType.CDRom: return "DVD 驱动器";
                case DriveType.Ram: return "RAM 磁盘";
                default: return "磁盘";
            }
        }

        internal static string FormatSize(long bytes)
        {
            if (bytes >= 1L << 30) return (bytes / (double)(1L << 30)).ToString("0.#") + " GB";
            if (bytes >= 1L << 20) return (bytes / (double)(1L << 20)).ToString("0.#") + " MB";
            if (bytes >= 1L << 10) return (bytes / 1024.0).ToString("0") + " KB";
            return bytes + " B";
        }

        private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
        {
            if (_suppressSearch) return;
            UpdateHint();
            _debounce.Stop();
            if (_search.Text.Trim().Length == 0)
            {
                LoadDir();
                return;
            }
            // Drop the previous search at once so its results stop arriving while the user types.
            CancelWork();
            SetStatus("准备搜索…", false);
            _debounce.Start();
        }

        private void StartSearch()
        {
            _debounce.Stop();
            string kw = _search.Text.Trim();
            if (kw.Length == 0) return;
            CancelWork();
            var cts = new CancellationTokenSource();
            _cts = cts;
            var run = new SearchRun
            {
                Keyword = kw,
                Scope = _currentDir,
                Matcher = new NameMatcher(kw, _exactBtn.IsChecked == true),
                Recursive = _subCheck.IsChecked == true,
                IncludeFiles = _filesCheck.IsChecked == true,
                RecordTask = _record,
                Token = cts.Token
            };
            _run = run;
            _items = new ObservableCollection<PickerEntry>();
            _list.ItemsSource = _items;
            _stopBtn.Visibility = Visibility.Visible;
            SetStatus("正在搜索…", false);
            UpdateSelText();
            Task.Factory.StartNew(() => SearchWorker(run), cts.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            _pump.Start();
        }

        private void StopSearch()
        {
            var run = _run;
            if (run == null) return;
            run.Stopped = true;
            if (_cts != null) _cts.Cancel();
        }

        // Breadth-first walk of the scope: shallow matches show up first and results stream in
        // while deeper folders are still being read.
        private static void SearchWorker(SearchRun run)
        {
            try
            {
                if (run.IncludeFiles) run.Record = Resolve(run.RecordTask);
                var queue = new Queue<KeyValuePair<string, int>>();
                if (run.Scope == ThisPC)
                {
                    foreach (var d in DriveInfo.GetDrives())
                    {
                        try
                        {
                            // Type first: IsReady on a disconnected network drive can take many seconds.
                            if ((d.DriveType == DriveType.Fixed || d.DriveType == DriveType.Removable) && d.IsReady)
                                queue.Enqueue(new KeyValuePair<string, int>(d.RootDirectory.FullName, 0));
                        }
                        catch
                        {
                        }
                    }
                }
                else
                {
                    queue.Enqueue(new KeyValuePair<string, int>(run.Scope, 0));
                }
                while (queue.Count > 0)
                {
                    if (run.Token.IsCancellationRequested) return;
                    var cur = queue.Dequeue();
                    try
                    {
                        foreach (var fsi in new DirectoryInfo(cur.Key).EnumerateFileSystemInfos())
                        {
                            if (run.Token.IsCancellationRequested) return;
                            FileAttributes a;
                            try { a = fsi.Attributes; } catch { continue; }
                            if ((a & FileAttributes.Directory) != 0)
                            {
                                if ((a & FileAttributes.Hidden) != 0 && (a & FileAttributes.System) != 0) continue;
                                Interlocked.Increment(ref run.ScannedDirs);
                                TryHit(run, fsi.Name, fsi.FullName, PickerKind.Folder, cur.Value + 1, 0, null);
                                // Junctions/symlinks are skipped so loops like "Application Data" cannot recurse forever.
                                if (run.Recursive && (a & FileAttributes.ReparsePoint) == 0)
                                    queue.Enqueue(new KeyValuePair<string, int>(fsi.FullName, cur.Value + 1));
                            }
                            else if (run.IncludeFiles)
                            {
                                if ((a & FileAttributes.Hidden) != 0) continue;
                                long size = 0;
                                var fi = fsi as FileInfo;
                                if (fi != null) { try { size = fi.Length; } catch { } }
                                TryHit(run, fsi.Name, fsi.FullName, PickerKind.File, cur.Value + 1, size, fi);
                            }
                            if (run.Found >= MaxHits)
                            {
                                run.Truncated = true;
                                return;
                            }
                        }
                    }
                    catch
                    {
                        // Unreadable folder (access denied, vanished, path too long): skip it.
                    }
                }
            }
            finally
            {
                run.Finished = true;
            }
        }

        private static void TryHit(SearchRun run, string name, string fullPath, PickerKind kind, int depth, long size, FileInfo file)
        {
            int score = run.Matcher.Match(name, kind == PickerKind.File, run.Scratch);
            if (score < 0) return;
            var hl = new List<int>(run.Scratch);
            Interlocked.Increment(ref run.Found);
            string parent = System.IO.Path.GetDirectoryName(fullPath) ?? "";
            string detail = parent;
            if (run.Scope != ThisPC)
            {
                string scope = run.Scope.TrimEnd('\\');
                if (string.Equals(parent.TrimEnd('\\'), scope, StringComparison.OrdinalIgnoreCase)) detail = "位于：当前文件夹";
                else if (parent.StartsWith(scope + "\\", StringComparison.OrdinalIgnoreCase)) detail = "位于：…\\" + parent.Substring(scope.Length + 1);
            }
            else
            {
                detail = "位于：" + parent;
            }
            var hit = new PickerEntry
            {
                Kind = kind,
                FullPath = fullPath,
                Name = name,
                Detail = detail,
                Right = kind == PickerKind.File ? FormatSize(size) : null,
                Score = score,
                Depth = depth,
                Highlight = hl
            };
            SetBadge(hit, run.Record, file);
            run.Hits.Enqueue(hit);
        }

        // Moves streamed hits into the list in ranked order and keeps the status line current.
        private void PumpResults()
        {
            SearchRun run = _run;
            if (run == null)
            {
                _pump.Stop();
                return;
            }
            PickerEntry e;
            int n = 0;
            while (n < 600 && run.Hits.TryDequeue(out e))
            {
                InsertRanked(e);
                n++;
            }
            bool done = run.Finished && run.Hits.IsEmpty;
            string scope = run.Scope == ThisPC ? "此电脑" : ScopeName();
            if (!done)
            {
                SetStatus(string.Format("正在「{0}」中搜索… 已扫描 {1:N0} 个文件夹，找到 {2:N0} 个", scope, run.ScannedDirs, _items.Count), false);
                return;
            }
            _pump.Stop();
            _stopBtn.Visibility = Visibility.Collapsed;
            string secs = run.Clock.Elapsed.TotalSeconds.ToString("0.0");
            if (_items.Count == 0)
            {
                _items.Add(new PickerEntry { Kind = PickerKind.Info, Name = "没有找到与「" + run.Keyword + "」匹配的" + (run.IncludeFiles ? "文件夹或文件" : "文件夹") });
                SetStatus(string.Format("{0}未找到（扫描 {1:N0} 个文件夹，用时 {2} 秒）", run.Stopped ? "已停止，" : "", run.ScannedDirs, secs), true);
            }
            else if (run.Truncated)
            {
                SetStatus(string.Format("结果过多，仅显示前 {0:N0} 个，请输入更精确的关键词", MaxHits), true);
            }
            else
            {
                SetStatus(string.Format("{0}找到 {1:N0} 个（扫描 {2:N0} 个文件夹，用时 {3} 秒）", run.Stopped ? "已停止，" : "", _items.Count, run.ScannedDirs, secs), false);
            }
            _run = null;
        }

        private void InsertRanked(PickerEntry e)
        {
            int lo = 0;
            int hi = _items.Count;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (Compare(_items[mid], e) <= 0) lo = mid + 1;
                else hi = mid;
            }
            _items.Insert(lo, e);
        }

        private static int Compare(PickerEntry a, PickerEntry b)
        {
            int c = b.Score.CompareTo(a.Score);
            if (c != 0) return c;
            c = a.Depth.CompareTo(b.Depth);
            if (c != 0) return c;
            c = (a.Kind == PickerKind.File ? 1 : 0).CompareTo(b.Kind == PickerKind.File ? 1 : 0);
            if (c != 0) return c;
            return StrCmpLogicalW(a.Name, b.Name);
        }

        private PickerEntry SelectedEntry()
        {
            var e = _list.SelectedItem as PickerEntry;
            return e != null && e.Selectable ? e : null;
        }

        private static string TargetOf(PickerEntry e)
        {
            return e.Kind == PickerKind.File ? System.IO.Path.GetDirectoryName(e.FullPath) : e.FullPath;
        }

        private void UpdateSelText()
        {
            PickerEntry e = SelectedEntry();
            if (e != null)
            {
                _selText.Text = (e.Kind == PickerKind.File ? "将选择该文件所在文件夹：" : "将选择：") + TargetOf(e);
                _selText.Fg(K.Text);
            }
            else if (_currentDir != ThisPC)
            {
                _selText.Text = "未选中（确定将使用当前位置：" + _currentDir + "）";
                _selText.Fg(K.TextHint);
            }
            else
            {
                _selText.Text = "请先选择一个驱动器或文件夹";
                _selText.Fg(K.TextHint);
            }
            _selText.ToolTip = _selText.Text;
            if (_record != null)
            {
                _checkTimer.Stop();
                _checkTimer.Start();
            }
        }

        // Shows under the chosen path whether its videos are unified, and which ones are not.
        private void CheckTarget()
        {
            _checkTimer.Stop();
            PickerEntry e = SelectedEntry();
            string target = e != null ? (e.Kind == PickerKind.Drive ? null : TargetOf(e)) : (_currentDir != ThisPC ? _currentDir : null);
            int seq = ++_checkSeq;
            if (string.IsNullOrEmpty(target))
            {
                MainWindow.ShowUnify(_unifyText, null);
                return;
            }
            Task<UnifyRecord> record = _record;
            Task.Factory.StartNew(() =>
            {
                FolderStatus st;
                try
                {
                    st = record.Result.CheckFolder(target, "");
                }
                catch (Exception ex)
                {
                    st = new FolderStatus { Error = ex.Message };
                }
                Unified.SaveStore();
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (seq == _checkSeq) MainWindow.ShowUnify(_unifyText, st);
                }));
            });
        }

        private void Confirm()
        {
            PickerEntry e = SelectedEntry();
            string result = e != null ? TargetOf(e) : (_currentDir != ThisPC ? _currentDir : null);
            if (string.IsNullOrEmpty(result) || !Directory.Exists(result))
            {
                _selText.Text = "请先选择一个文件夹或驱动器";
                _selText.Fg(K.Warn);
                return;
            }
            _selectedPath = result.Length > 3 ? result.TrimEnd('\\') : result;
            DialogResult = true;
        }

        private void Open(PickerEntry e)
        {
            if (e == null) return;
            if (e.Kind == PickerKind.File) Navigate(System.IO.Path.GetDirectoryName(e.FullPath), e.FullPath);
            else if (Directory.Exists(e.FullPath)) Navigate(e.FullPath, null);
        }

        private void OnListDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var dep = e.OriginalSource as DependencyObject;
            while (dep != null && !(dep is ListBoxItem))
            {
                dep = dep is Visual || dep is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(dep) : LogicalTreeHelper.GetParent(dep);
            }
            if (dep == null) return;
            Open(SelectedEntry());
        }

        private void OnListKey(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) Confirm();
                else Open(SelectedEntry());
                e.Handled = true;
            }
            else if (e.Key == Key.Back)
            {
                GoUp();
                e.Handled = true;
            }
        }

        private void OnSearchKey(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) Confirm();
                else if (_search.Text.Trim().Length > 0) StartSearch();
                e.Handled = true;
            }
            else if (e.Key == Key.Down && _items.Count > 0)
            {
                foreach (var it in _items)
                {
                    if (!it.Selectable) continue;
                    _list.SelectedItem = it;
                    _list.UpdateLayout();
                    var c = _list.ItemContainerGenerator.ContainerFromItem(it) as ListBoxItem;
                    if (c != null) c.Focus();
                    break;
                }
                e.Handled = true;
            }
        }

        private void OnPreviewKey(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                if (_search.Text.Length > 0)
                {
                    _search.Text = "";
                    _search.Focus();
                }
                else
                {
                    DialogResult = false;
                }
                e.Handled = true;
            }
            else if (e.Key == Key.F && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
            {
                _search.Focus();
                _search.SelectAll();
                e.Handled = true;
            }
        }
    }
}
