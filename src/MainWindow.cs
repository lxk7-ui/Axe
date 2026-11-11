using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using Path = System.Windows.Shapes.Path;
using System.Windows.Threading;

namespace VideoMixApp
{
    public partial class MainWindow : Window
    {
        private static readonly string[] RatioLabels =
        {
            "9:16 竖屏（720x1280）", "16:9 横屏（1280x720）", "1:1 方形（720x720）", "3:4 竖屏（720x960）", "4:3 横屏（960x720）"
        };
        private static readonly int[] RatioW = { 720, 1280, 720, 720, 960 };
        private static readonly int[] RatioH = { 1280, 720, 720, 960, 720 };
        private const int MaxLogParagraphs = 2000;
        private const string KindMix = "快拼";
        private const string KindUnify = "规格统一";

        private TextBox _srcBox;
        private TextBox _outBox;
        private TextBox _introBox;
        private TextBox _nameBox;
        private TextBlock _namePreview;
        private TextBlock _unifyLine;
        private ComboBox _catCombo;
        private ComboBox _ratioCombo;
        private TextBox _countBox;
        private TextBox _durBox;
        private TextBox _tolBox;
        private TextBox _parallelBox;
        private RichTextBox _logBox;
        private ProgressBar _progress;
        private TextBlock _statusText;
        private Button _mixBtn;
        private Path _mixIcon;
        private TextBlock _mixText;
        private Button _preBtn;
        private Button _forceBtn;
        private CheckBox _nvencCheck;
        private bool _suppressNvencEvent;
        private EffectsPanel _effects;
        private System.Windows.Controls.Primitives.ToggleButton _tabFx;
        private System.Windows.Controls.Primitives.ToggleButton _tabLog;
        private Ellipse _logDot;
        private DispatcherTimer _saveTimer;
        private bool _started;
        private AppSettings _settings;
        private string _shownSource;
        private string _shownOutput;
        private string _shownIntro;
        private string _appDir;
        private readonly object _logFileLock = new object();
        private bool _busy;
        private bool _stopping;
        private bool _closeAfterStop;
        private bool _forceClose;
        private string _runKind;
        private Border _logBody;
        private Ellipse _statusDot;
        private TextBlock _headerStatus;
        private Border _windowFrame;
        private Button _maxBtn;
        private Border _titleBar;
        private DispatcherTimer _unifyTimer;
        private int _unifySeq;
        private volatile MixEngine _engine;

        public MainWindow()
        {
            _appDir = AppDomain.CurrentDomain.BaseDirectory;
            RollLogFile();
            CleanStaleTasks();
            LoadSettings();
            Unified.UseStore(_appDir);
            BuildStyles();
            _unifyTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _unifyTimer.Tick += (s, e) => RunUnifyCheck();
            BuildUi();
            WindowResizer.Enable(this, 10.0);
            AppendLog("Axe 已启动");
            string ff = System.IO.Path.Combine(_appDir, "ffmpeg\\bin\\ffmpeg.exe");
            string fp = System.IO.Path.Combine(_appDir, "ffmpeg\\bin\\ffprobe.exe");
            if (!File.Exists(ff) || !File.Exists(fp))
            {
                AppendLog("警告：未检测到 FFmpeg 组件（应位于软件目录的 ffmpeg\\bin 下）。请使用完整解压的文件夹，不要只复制 Axe.exe。");
                ShowTab(true);
            }
            else if (!Environment.Is64BitOperatingSystem)
            {
                AppendLog("警告：当前是 32 位 Windows，内置的 FFmpeg 只能在 64 位系统运行，规格统一和快拼将无法使用。请在 64 位 Windows 10/11 上运行。");
                ShowTab(true);
            }
            Closing += OnClosing;
            Closed += (s, e) => SavePendingSettings();
            ContentRendered += (s, e) => CheckDevice();
            // Coming back from Explorer (e.g. after adding videos, filters or stickers) refreshes the
            // folder status, the preview and the galleries.
            Activated += (s, e) =>
            {
                RefreshUnify();
                if (!_busy) _effects.RefreshAssets();
            };
            int ratio = Math.Max(0, _ratioCombo.SelectedIndex);
            _effects.SetRatio(RatioW[ratio], RatioH[ratio]);
            _effects.RefreshAssets();
            RefreshUnify();
            _started = true;
        }

        // 设备检测 the first time Axe starts on this PC (settings.json remembers the PC): suggests 并行进程,
        // and 硬件加速 when an NVIDIA card can take the encoding. The GPU test encodes run off the UI
        // thread; the window is already shown.
        private void CheckDevice()
        {
            string key = DeviceCheck.Key();
            if (_settings.DeviceKey == key) return;
            int suggested = DeviceCheck.Suggest();
            string ff = System.IO.Path.Combine(_appDir, "ffmpeg\\bin\\ffmpeg.exe");
            bool askGpu = _nvencCheck.IsChecked != true;
            var t = new Thread(() =>
            {
                bool nvidia = false;
                try
                {
                    nvidia = askGpu && File.Exists(ff) && HwEncoder.Detect(ff) == HwKind.Nvenc;
                }
                catch
                {
                }
                Dispatcher.BeginInvoke(new Action(() => ShowDeviceCheck(key, suggested, nvidia)));
            });
            t.IsBackground = true;
            t.Start();
        }

        private void ShowDeviceCheck(string key, int suggested, bool nvidia)
        {
            // A task started meanwhile: asked again on the next start.
            if (_busy) return;
            int current;
            if (!int.TryParse(_parallelBox.Text.Trim(), out current) || current <= 0) current = _settings.ParallelJobs;
            RecordLog(string.Format("设备检测：{0}，建议并行进程 {1}{2}", DeviceCheck.Summary(), suggested, nvidia ? "；有 NVIDIA 显卡，建议勾选「硬件加速」" : ""));
            if (current != suggested || nvidia)
            {
                var dlg = new DeviceWindow(suggested, current, nvidia) { Owner = this };
                if (dlg.ShowDialog() == true)
                {
                    _parallelBox.Text = suggested.ToString();
                    _settings.ParallelJobs = suggested;
                    _settings.MixParallel = suggested;
                    if (nvidia) _nvencCheck.IsChecked = true;
                    RecordLog("已按建议改为并行进程 " + suggested + (nvidia ? "，并勾选硬件加速" : "") + "。");
                }
            }
            _settings.DeviceKey = key;
            SaveSettings();
        }

        // 成片效果 or 运行日志 in the right card.
        private void ShowTab(bool log)
        {
            _tabLog.IsChecked = log;
            _tabFx.IsChecked = !log;
            _effects.Visibility = log ? Visibility.Collapsed : Visibility.Visible;
            _logBody.Visibility = log ? Visibility.Visible : Visibility.Collapsed;
            if (log)
            {
                _logDot.Visibility = Visibility.Collapsed;
                _logBox.ScrollToEnd();
            }
        }

        // The effects panel changed a choice, a position or a size: keep it in the settings (saved a
        // moment later, so dragging does not write the file many times).
        private void EffectsChanged()
        {
            CollectEffectChoices();
            if (_saveTimer == null)
            {
                _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
                _saveTimer.Tick += (s, e) =>
                {
                    _saveTimer.Stop();
                    SaveSettings();
                };
            }
            _saveTimer.Stop();
            _saveTimer.Start();
        }

        private int FindRatioIndex(int w, int h)
        {
            for (int i = 0; i < RatioW.Length; i++)
            {
                if (RatioW[i] == w && RatioH[i] == h) return i;
            }
            return 0;
        }

        private void CleanStaleTasks()
        {
            // Unfinished preview renders (the window was closed while ffmpeg drew them).
            try
            {
                string preview = System.IO.Path.Combine(_appDir, "_preview");
                if (Directory.Exists(preview))
                {
                    var stale = new List<string>(Directory.GetDirectories(preview, "work_*"));
                    string anim = System.IO.Path.Combine(preview, "anim");
                    if (Directory.Exists(anim)) stale.AddRange(Directory.GetDirectories(anim, "*_*"));
                    // LUT copies the v2.4 preview had ffmpeg read (filters are applied in Axe now).
                    string luts = System.IO.Path.Combine(preview, "lut");
                    if (Directory.Exists(luts)) stale.Add(luts);
                    foreach (string d in stale)
                    {
                        try
                        {
                            if (DateTime.UtcNow - Directory.GetLastWriteTimeUtc(d) > TimeSpan.FromHours(1)) Directory.Delete(d, true);
                        }
                        catch
                        {
                        }
                    }
                }
            }
            catch
            {
            }
            try
            {
                foreach (string cache in Directory.GetDirectories(_appDir, "_cache_*"))
                {
                    foreach (string task in Directory.GetDirectories(cache, "_mix_tasks_*"))
                    {
                        // A recent one can belong to a second Axe window that is still mixing.
                        try
                        {
                            if (DateTime.UtcNow - Directory.GetLastWriteTimeUtc(task) > TimeSpan.FromHours(12)) Directory.Delete(task, true);
                        }
                        catch
                        {
                        }
                    }
                }
            }
            catch
            {
            }
        }

        private void RollLogFile()
        {
            try
            {
                string log = System.IO.Path.Combine(_appDir, "app.log");
                if (File.Exists(log) && new FileInfo(log).Length > 5 * 1024 * 1024)
                {
                    string bak = System.IO.Path.Combine(_appDir, "app.log.1");
                    if (File.Exists(bak)) File.Delete(bak);
                    File.Move(log, bak);
                }
            }
            catch
            {
            }
        }

        private void WriteLogFile(string s)
        {
            try
            {
                string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + s;
                lock (_logFileLock)
                {
                    File.AppendAllText(System.IO.Path.Combine(_appDir, "app.log"), line + Environment.NewLine, new UTF8Encoding(false));
                }
            }
            catch
            {
            }
        }

        private void RecordLog(string s)
        {
            WriteLogFile(s);
            Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action<string>(AppendLog), s);
        }

        private string SettingsPath
        {
            get { return System.IO.Path.Combine(_appDir, "settings.json"); }
        }

        private void LoadSettings()
        {
            string defSrc = System.IO.Path.GetFullPath(System.IO.Path.Combine(_appDir, "..\\视频素材"));
            string defOut = System.IO.Path.GetFullPath(System.IO.Path.Combine(_appDir, "..\\混剪输出"));
            _settings = new AppSettings
            {
                SourceRoot = defSrc,
                OutputRoot = defOut,
                IntroFile = "",
                Category = "",
                Count = 10,
                DurationSec = 30.0,
                ToleranceSec = 5.0,
                ParallelJobs = DeviceCheck.Suggest(),
                MixParallel = DeviceCheck.Suggest(),
                SegmentSeconds = 15,
                Width = 720,
                Height = 1280,
                FPS = 30,
                CRF = 23,
                NameFormat = OutputNaming.DefaultFormat
            };
            if (File.Exists(SettingsPath))
            {
                try
                {
                    var loaded = new JavaScriptSerializer().Deserialize<AppSettings>(File.ReadAllText(SettingsPath, Encoding.UTF8));
                    if (loaded != null) _settings = loaded;
                }
                catch
                {
                }
            }
            // A remembered folder that is not there right now (a drive not plugged in) shows the default
            // but stays remembered: 成片效果 changes save settings.json too, and only starting a task with
            // other folders should replace it.
            if (string.IsNullOrEmpty(_settings.SourceRoot)) _settings.SourceRoot = defSrc;
            if (string.IsNullOrEmpty(_settings.OutputRoot)) _settings.OutputRoot = defOut;
            if (_settings.IntroFile == null) _settings.IntroFile = "";
            _shownSource = Directory.Exists(_settings.SourceRoot) ? _settings.SourceRoot : defSrc;
            _shownOutput = Directory.Exists(_settings.OutputRoot) ? _settings.OutputRoot : defOut;
            _shownIntro = _settings.IntroFile.Length > 0 && File.Exists(_settings.IntroFile) ? _settings.IntroFile : "";
            // A settings file written by hand or by an older build may miss fields (they load as 0).
            if (_settings.Category == null) _settings.Category = "";
            if (_settings.Count <= 0) _settings.Count = 10;
            if (_settings.DurationSec <= 0) _settings.DurationSec = 30.0;
            if (_settings.ToleranceSec < 0) _settings.ToleranceSec = 5.0;
            if (_settings.ParallelJobs <= 0) _settings.ParallelJobs = DeviceCheck.Suggest();
            if (_settings.MixParallel <= 0) _settings.MixParallel = _settings.ParallelJobs;
            if (_settings.SegmentSeconds <= 0) _settings.SegmentSeconds = 15;
            if (_settings.FPS <= 0) _settings.FPS = 30;
            if (_settings.CRF <= 0) _settings.CRF = 23;
            if (_settings.Width <= 0 || _settings.Height <= 0)
            {
                _settings.Width = 720;
                _settings.Height = 1280;
            }
            if (string.IsNullOrWhiteSpace(_settings.NameFormat)) _settings.NameFormat = OutputNaming.DefaultFormat;
            // 成片效果 (missing in settings of v2.2 and earlier: everything off, default positions).
            var fx = new EffectOptions();
            if (_settings.Filter == null) _settings.Filter = "";
            if (_settings.Sticker == null) _settings.Sticker = "";
            _settings.Speed = EffectOptions.ValidSpeed(_settings.Speed);
            if (_settings.FilterStrength <= 0 || _settings.FilterStrength > 100) _settings.FilterStrength = 100;
            if (_settings.FilterStrength < 10) _settings.FilterStrength = 10;
            // The upload platforms' minimum (v2.5.0): 1000 kbps unless set otherwise in settings.json.
            if (_settings.MinKbps == 0) _settings.MinKbps = 1000;
            if (!(_settings.SubSize > 0))
            {
                _settings.SubX = fx.SubX;
                _settings.SubY = fx.SubY;
                _settings.SubSize = fx.SubSize;
            }
            if (!(_settings.StickerSize > 0))
            {
                _settings.StickerX = fx.StickerX;
                _settings.StickerY = fx.StickerY;
                _settings.StickerSize = fx.StickerSize;
            }
            // A copied Axe folder without the speech model starts with subtitles off.
            if (_settings.Subtitles && !MixEngine.SubtitleModelReady(_appDir)) _settings.Subtitles = false;
        }

        private EffectOptions CurrentEffects()
        {
            return new EffectOptions
            {
                Filter = _settings.Filter ?? "",
                FilterStrength = EffectOptions.ValidStrength(_settings.FilterStrength / 100.0),
                Sticker = _settings.Sticker ?? "",
                Speed = EffectOptions.ValidSpeed(_settings.Speed),
                Subtitles = _settings.Subtitles,
                SubX = _settings.SubX,
                SubY = _settings.SubY,
                SubSize = _settings.SubSize,
                StickerX = _settings.StickerX,
                StickerY = _settings.StickerY,
                StickerSize = _settings.StickerSize
            };
        }

        // The speech model is downloaded on first use (about 230 MB); true when it is in place.
        private bool DownloadSpeechModel()
        {
            var dlg = new ModelWindow(_appDir, RecordLog) { Owner = this };
            dlg.ShowDialog();
            bool ok = MixEngine.SubtitleModelReady(_appDir);
            if (ok) AppendLog("语音识别模型已就绪。");
            return ok;
        }

        private void CollectEffectChoices()
        {
            EffectOptions fx = _effects.Options;
            _settings.Filter = fx.Filter ?? "";
            _settings.FilterStrength = (int)Math.Round(EffectOptions.ValidStrength(fx.FilterStrength) * 100);
            _settings.Sticker = fx.Sticker ?? "";
            _settings.Speed = EffectOptions.ValidSpeed(fx.Speed);
            _settings.Subtitles = fx.Subtitles;
            _settings.SubX = fx.SubX;
            _settings.SubY = fx.SubY;
            _settings.SubSize = fx.SubSize;
            _settings.StickerX = fx.StickerX;
            _settings.StickerY = fx.StickerY;
            _settings.StickerSize = fx.StickerSize;
        }

        // Written next to it and swapped in, so a crash or a full disk never leaves half a file (which
        // would load as all defaults).
        private void SaveSettings()
        {
            string json;
            try
            {
                json = new JavaScriptSerializer().Serialize(_settings);
            }
            catch
            {
                return;
            }
            string tmp = SettingsPath + ".tmp";
            try
            {
                File.WriteAllText(tmp, json, new UTF8Encoding(true));
                if (File.Exists(SettingsPath)) File.Replace(tmp, SettingsPath, null);
                else File.Move(tmp, SettingsPath);
                return;
            }
            catch
            {
            }
            // Drives where swapping files fails (some network shares): write it in place.
            try
            {
                File.WriteAllText(SettingsPath, json, new UTF8Encoding(true));
                if (File.Exists(tmp)) File.Delete(tmp);
            }
            catch
            {
            }
        }

        // A change made just before closing (within the save delay) is not lost.
        private void SavePendingSettings()
        {
            if (_saveTimer == null || !_saveTimer.IsEnabled) return;
            _saveTimer.Stop();
            SaveSettings();
        }

        private string CacheDirFor(int ratioIndex)
        {
            if (ratioIndex < 0 || ratioIndex >= RatioW.Length) ratioIndex = 0;
            return System.IO.Path.Combine(_appDir, "_cache_" + RatioW[ratioIndex] + "x" + RatioH[ratioIndex]);
        }

        // The mark 规格统一 writes into videos unified for this ratio (see Unified).
        private string MarkFor(int ratioIndex)
        {
            if (ratioIndex < 0 || ratioIndex >= RatioW.Length) ratioIndex = 0;
            return Unified.Mark(RatioW[ratioIndex], RatioH[ratioIndex], _settings.FPS);
        }

        // Created on the UI thread so 停止 works from the first moment; the GPU check (which runs
        // test encodes) is done on the worker thread.
        private MixEngine CreateEngine()
        {
            var e = new MixEngine();
            e.AppDir = _appDir;
            e.FFmpeg = System.IO.Path.Combine(_appDir, "ffmpeg\\bin\\ffmpeg.exe");
            e.FFprobe = System.IO.Path.Combine(_appDir, "ffmpeg\\bin\\ffprobe.exe");
            int w = _settings.Width;
            int h = _settings.Height;
            e.CacheDir = System.IO.Path.Combine(_appDir, "_cache_" + w + "x" + h);
            e.Configure(w, h, _settings.FPS, _settings.CRF, _settings.SegmentSeconds);
            e.Effects = CurrentEffects();
            e.MinKbps = Math.Max(0, _settings.MinKbps);
            e.Log = RecordLog;
            e.Progress = p => Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action<int>(SetProgress), p);
            e.Status = s => Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action<string>(SetRunningStatus), s);
            return e;
        }

        private void TitleBarDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount >= 2)
            {
                ToggleMaximize();
                return;
            }
            try { DragMove(); } catch { }
        }

        private void MinClick(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void MaxClick(object sender, RoutedEventArgs e)
        {
            ToggleMaximize();
        }

        private void CloseClick(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void ToggleMaximize()
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }

        private void WindowStateChanged(object sender, EventArgs e)
        {
            if (_windowFrame == null) return;
            if (WindowState == WindowState.Maximized)
            {
                _windowFrame.Margin = new Thickness(8.0);
                _windowFrame.CornerRadius = new CornerRadius(10.0);
                _titleBar.CornerRadius = new CornerRadius(10.0, 10.0, 0.0, 0.0);
                if (_maxBtn != null) _maxBtn.Tag = Theme.Geo(Theme.IconRestore);
            }
            else
            {
                _windowFrame.Margin = new Thickness(10.0);
                _windowFrame.CornerRadius = new CornerRadius(18.0);
                _titleBar.CornerRadius = new CornerRadius(18.0, 18.0, 0.0, 0.0);
                if (_maxBtn != null) _maxBtn.Tag = Theme.Geo(Theme.IconMax);
            }
        }

        private void PickFolder(TextBox target)
        {
            // For the source folder the picker also shows which videos are unified for the chosen ratio.
            var dlg = target == _srcBox ? new FolderPicker(target.Text, CacheDirFor(_ratioCombo.SelectedIndex), MarkFor(_ratioCombo.SelectedIndex)) : new FolderPicker(target.Text);
            dlg.Owner = this;
            if (dlg.ShowDialog() == true && !string.IsNullOrEmpty(dlg.SelectedPath))
            {
                target.Text = dlg.SelectedPath;
                if (target == _srcBox) RefreshCategories();
            }
        }

        private void PickFile(TextBox target)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog();
            dlg.Title = "选择片头视频文件";
            dlg.Filter = "视频文件 (*.mp4;*.mov;*.mkv;*.avi)|*.mp4;*.mov;*.mkv;*.avi|所有文件 (*.*)|*.*";
            if (!string.IsNullOrEmpty(target.Text) && File.Exists(target.Text))
            {
                dlg.InitialDirectory = System.IO.Path.GetDirectoryName(target.Text);
            }
            if (dlg.ShowDialog() == true) target.Text = dlg.FileName;
        }

        // Rebuilds the 素材分类 list from the folder currently typed in 素材目录.
        private void RefreshCategories()
        {
            string keep = _catCombo.SelectedItem as string;
            if (string.IsNullOrEmpty(keep) || keep == "全部素材") keep = _settings.Category;
            _catCombo.Items.Clear();
            _catCombo.Items.Add("全部素材");
            string root = _srcBox != null ? _srcBox.Text.Trim() : _shownSource;
            try
            {
                if (Directory.Exists(root))
                {
                    foreach (var d in new DirectoryInfo(root).GetDirectories())
                    {
                        if (!MixEngine.IsSystemFolder(d)) _catCombo.Items.Add(d.Name);
                    }
                }
            }
            catch
            {
            }
            if (string.IsNullOrEmpty(keep))
            {
                _catCombo.SelectedIndex = 0;
            }
            else
            {
                int idx = _catCombo.Items.IndexOf(keep);
                _catCombo.SelectedIndex = idx >= 0 ? idx : 0;
            }
            RefreshUnify();
            UpdateNamePreview();
        }

        private string SelectedCategory()
        {
            string cat = _catCombo != null ? _catCombo.SelectedItem as string : null;
            return cat != null && cat != "全部素材" ? cat : "";
        }

        // Product name as 快拼 will use it: the category, or else the source folder's name.
        private string PreviewProduct()
        {
            return MixEngine.ProductName(_srcBox != null ? _srcBox.Text : "", SelectedCategory());
        }

        private void UpdateNamePreview()
        {
            if (_nameBox == null || _namePreview == null) return;
            double sec;
            if (!double.TryParse(_durBox != null ? _durBox.Text.Trim() : "", out sec) || sec <= 0) sec = _settings.DurationSec;
            var naming = new OutputNaming(_nameBox.Text, PreviewProduct(), DateTime.Now, 1);
            string example = naming.FileName(1, sec);
            _namePreview.Text = "示例：" + example;
            _namePreview.ToolTip = "示例：" + example + (naming.Format != _nameBox.Text.Trim() ? "\n实际使用的格式：" + naming.Format : "");
        }

        private void InsertToken(string token)
        {
            int at = _nameBox.SelectionStart;
            string text = _nameBox.Text;
            _nameBox.Text = text.Substring(0, at) + token + text.Substring(at + _nameBox.SelectionLength);
            _nameBox.SelectionStart = at + token.Length;
            _nameBox.SelectionLength = 0;
            _nameBox.Focus();
        }

        private void RefreshUnify()
        {
            if (_effects != null && _srcBox != null) _effects.SetSource(_srcBox.Text.Trim(), SelectedCategory());
            if (_unifyTimer == null || _unifyLine == null) return;
            _unifyTimer.Stop();
            _unifyTimer.Start();
        }

        // Checks the chosen folder against the 规格统一 record off the UI thread; only the newest
        // request updates the line under 素材目录.
        private void RunUnifyCheck()
        {
            _unifyTimer.Stop();
            string root = _srcBox.Text.Trim();
            string cat = SelectedCategory();
            string cache = CacheDirFor(_ratioCombo.SelectedIndex);
            string mark = MarkFor(_ratioCombo.SelectedIndex);
            int seq = ++_unifySeq;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                FolderStatus st;
                try
                {
                    st = UnifyRecord.Load(cache, mark).CheckFolder(root, cat);
                }
                catch (Exception ex)
                {
                    st = new FolderStatus { Error = ex.Message };
                }
                Unified.SaveStore();
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (seq == _unifySeq) ShowUnify(_unifyLine, st);
                }));
            });
        }

        // Shared by the main window and the folder picker.
        internal static void ShowUnify(TextBlock line, FolderStatus st)
        {
            line.ToolTip = null;
            if (st == null || st.Error != null)
            {
                line.Text = "";
                line.Visibility = Visibility.Collapsed;
                return;
            }
            line.Visibility = Visibility.Visible;
            if (st.Total == 0)
            {
                line.Text = "没有 MP4 视频";
                line.Foreground = Theme.SB(Theme.CTextHint);
                return;
            }
            if (st.Waiting.Count == 0)
            {
                line.Text = "✓ 已全部统一（" + st.Total + " 个视频）";
                line.Foreground = Theme.SB(Theme.CSuccessText);
                return;
            }
            line.Foreground = Theme.SB(Theme.CAccentDeep);
            if (st.Waiting.Count == st.Total)
            {
                line.Text = "未统一：共 " + st.Total + " 个视频";
            }
            else
            {
                // One line with ellipsis: measuring thousands of names would only slow the window down.
                int shown = Math.Min(st.Waiting.Count, 30);
                line.Text = string.Format("未统一 {0}/{1}：{2}{3}", st.Waiting.Count, st.Total, string.Join("、", st.Waiting.GetRange(0, shown).ToArray()), shown < st.Waiting.Count ? "…" : "");
            }
            var tip = new StringBuilder("未统一的视频：");
            for (int i = 0; i < st.Waiting.Count && i < 40; i++) tip.Append('\n').Append(st.Waiting[i]);
            if (st.Waiting.Count > 40) tip.Append("\n…等 ").Append(st.Waiting.Count).Append(" 个");
            line.ToolTip = tip.ToString();
        }

        private void NvencToggle(object sender, RoutedEventArgs e)
        {
            if (_suppressNvencEvent) return;
            if (_nvencCheck.IsChecked != true)
            {
                _settings.UseNvenc = false;
                AppendLog("已关闭硬件加速，使用 CPU 软件编码。");
                return;
            }
            string ff = System.IO.Path.Combine(_appDir, "ffmpeg\\bin\\ffmpeg.exe");
            _nvencCheck.IsEnabled = false;
            AppendLog("正在检测显卡硬件编码（NVIDIA / Intel / AMD）...");
            var t = new Thread(() =>
            {
                HwKind kind = File.Exists(ff) ? HwEncoder.Detect(ff) : HwKind.None;
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    _nvencCheck.IsEnabled = true;
                    if (kind != HwKind.None)
                    {
                        _settings.UseNvenc = true;
                        AppendLog("硬件加速可用：规格统一处理将使用 " + HwEncoder.DisplayName(kind) + " 编码，并与 CPU 一起分担任务。");
                        return;
                    }
                    _settings.UseNvenc = false;
                    _suppressNvencEvent = true;
                    _nvencCheck.IsChecked = false;
                    _suppressNvencEvent = false;
                    AppendLog("未检测到可用的显卡硬件编码（无兼容显卡或驱动过旧），已自动回退 CPU 编码。");
                    MessageBox.Show("当前显卡/驱动不支持硬件编码，将继续使用 CPU 软件编码。");
                }));
            });
            t.IsBackground = true;
            t.Start();
        }

        private bool CollectInputs()
        {
            _settings.SourceRoot = _srcBox.Text.Trim();
            _settings.OutputRoot = _outBox.Text.Trim();
            _settings.IntroFile = _introBox.Text.Trim();
            if (!string.IsNullOrEmpty(_settings.IntroFile) && !File.Exists(_settings.IntroFile))
            {
                MessageBox.Show("片头素材文件不存在，请重新选择或清空。");
                return false;
            }
            _settings.Category = SelectedCategory();
            int ratio = _ratioCombo.SelectedIndex;
            if (ratio < 0) ratio = 0;
            _settings.Width = RatioW[ratio];
            _settings.Height = RatioH[ratio];
            int n;
            if (!int.TryParse(_countBox.Text.Trim(), out n) || n <= 0)
            {
                MessageBox.Show("请输入正确的成片数量。");
                return false;
            }
            _settings.Count = n;
            double d;
            if (!double.TryParse(_durBox.Text.Trim(), out d) || !(d > 0.0))
            {
                MessageBox.Show("请输入正确的目标时长。");
                return false;
            }
            _settings.DurationSec = d;
            if (!double.TryParse(_tolBox.Text.Trim(), out d) || d < 0.0)
            {
                MessageBox.Show("请输入正确的浮动秒数（0 或正数）。");
                return false;
            }
            _settings.ToleranceSec = d;
            if (!(_settings.ToleranceSec < _settings.DurationSec))
            {
                MessageBox.Show("浮动秒数需小于目标时长。");
                return false;
            }
            if (!int.TryParse(_parallelBox.Text.Trim(), out n) || n <= 0)
            {
                MessageBox.Show("请输入正确的并行数。");
                return false;
            }
            _settings.ParallelJobs = n;
            _settings.MixParallel = n;
            _settings.UseNvenc = _nvencCheck.IsChecked == true;
            CollectEffectChoices();
            string format = _nameBox.Text.Trim();
            _settings.NameFormat = format.Length > 0 ? format : OutputNaming.DefaultFormat;
            if (!Directory.Exists(_settings.SourceRoot))
            {
                MessageBox.Show("素材目录不存在。");
                return false;
            }
            if (_settings.OutputRoot.Length == 0)
            {
                MessageBox.Show("请选择输出目录。");
                return false;
            }
            try
            {
                if (!Directory.Exists(_settings.OutputRoot)) Directory.CreateDirectory(_settings.OutputRoot);
            }
            catch (Exception ex)
            {
                MessageBox.Show("无法创建输出目录：" + ex.Message);
                return false;
            }
            SaveSettings();
            return true;
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern uint SetThreadExecutionState(uint flags);

        // While a task runs, Windows must not put the PC to sleep for being idle (a batch left to run
        // would stop halfway). The screen may still turn off, and closing the lid still sleeps.
        private static void KeepAwake(bool on)
        {
            try
            {
                // ES_CONTINUOUS, plus ES_SYSTEM_REQUIRED while busy.
                SetThreadExecutionState(on ? 0x80000001u : 0x80000000u);
            }
            catch
            {
            }
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            KeepAwake(busy);
            if (!busy) _stopping = false;
            if (busy) ShowTab(true);
            _preBtn.IsEnabled = !busy;
            _forceBtn.IsEnabled = !busy;
            UpdateMixButton();
            _statusText.Text = busy ? "正在执行，请勿关闭窗口..." : "就绪";
            _headerStatus.Text = busy ? "执行中" : "就绪";
            Color c = busy ? Theme.CRunning : Theme.CSuccess;
            _statusDot.Fill = Theme.SB(c);
            ((DropShadowEffect)_statusDot.Effect).Color = c;
        }

        // While a task runs the big button turns into 停止.
        private void UpdateMixButton()
        {
            _mixBtn.IsEnabled = !_stopping;
            _mixBtn.Style = (Style)Resources[_busy ? "StopButton" : "PrimaryButton"];
            _mixIcon.Data = Theme.Geo(_busy ? Theme.IconStop : Theme.IconPlay);
            _mixText.Text = _stopping ? "正在停止…" : (_busy ? "停止" : "开始快拼");
        }

        private void SetRunningStatus(string s)
        {
            if (_busy && !_stopping) _statusText.Text = s;
        }

        private void MixClick(object sender, RoutedEventArgs e)
        {
            if (_busy)
            {
                StopClick();
                return;
            }
            if (!CollectInputs() || !ConfirmReplace()) return;
            if (_settings.Subtitles && !MixEngine.SubtitleModelReady(_appDir) && !DownloadSpeechModel())
            {
                MessageBox.Show(this, "还没有语音识别模型，无法配字幕。\n下载模型，或取消勾选「识别语音配字幕」后再开始。", "Axe");
                return;
            }
            AppSettings s = _settings;
            Start(KindMix, engine =>
            {
                engine.PreferHardwareAccel(s.UseNvenc);
                return engine.Mix(s.Count, s.DurationSec, s.ToleranceSec, s.MixParallel, s.SourceRoot, s.Category, s.OutputRoot, s.IntroFile, s.NameFormat);
            });
        }

        private void PreClick(object sender, RoutedEventArgs e)
        {
            RunPreprocess(false);
        }

        private void ForceClick(object sender, RoutedEventArgs e)
        {
            RunPreprocess(true);
        }

        // From v2.3.0 规格统一 (also the one 快拼 does first) replaces the source videos. Said once.
        private bool ConfirmReplace()
        {
            if (_settings.ReplaceConfirmed) return true;
            string msg = "规格统一后的视频会直接替换素材文件夹里的原视频（文件名、位置不变），原视频不再保留。\n" +
                         "需要原画质的素材（例如以后要用剪映精剪、出横屏），请先另外备份。\n\n继续吗？";
            if (MessageBox.Show(this, msg, "Axe", MessageBoxButton.YesNo, MessageBoxImage.Information) != MessageBoxResult.Yes) return false;
            _settings.ReplaceConfirmed = true;
            SaveSettings();
            return true;
        }

        private void RunPreprocess(bool force)
        {
            if (_busy || !CollectInputs() || !ConfirmReplace()) return;
            AppSettings s = _settings;
            Start(KindUnify, engine =>
            {
                engine.PreferHardwareAccel(s.UseNvenc);
                engine.Preprocess(s.SourceRoot, s.Category, force, s.ParallelJobs);
                return null;
            });
        }

        private void StopClick()
        {
            if (_engine == null || _stopping) return;
            string msg = _runKind == KindMix
                ? "确定停止快拼吗？\n本批次已经生成的成片会全部删除。"
                : "确定停止规格统一吗？\n已经统一好的素材会保留，下次接着处理剩下的。";
            if (MessageBox.Show(this, msg, "Axe", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            RequestStop();
        }

        private void RequestStop()
        {
            MixEngine engine = _engine;
            if (engine == null || _stopping) return;
            _stopping = true;
            UpdateMixButton();
            _statusText.Text = "正在停止…";
            AppendLog("正在停止，请稍候…");
            engine.Cancel();
        }

        // Runs a task off the UI thread; a non-null result is shown as the finished output folder.
        private void Start(string kind, Func<MixEngine, string> work)
        {
            MixEngine engine = CreateEngine();
            _engine = engine;
            _runKind = kind;
            SetBusy(true);
            var t = new Thread(() =>
            {
                string result = null;
                try
                {
                    result = work(engine);
                }
                catch (Exception ex)
                {
                    RecordLog("错误: " + ex.Message);
                    RecordLog(ex.ToString());
                }
                finally
                {
                    _engine = null;
                    Unified.SaveStore();
                    bool stopped = engine.IsCancelled;
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        SetBusy(false);
                        if (stopped) _statusText.Text = "已停止";
                        else if (result != null) _statusText.Text = "完成：" + result;
                        RefreshUnify();
                        if (_closeAfterStop) Close();
                    }));
                }
            });
            t.IsBackground = true;
            t.Start();
        }

        // Closing during a task stops it first (with the same clean-up as 停止), then closes.
        private void OnClosing(object sender, CancelEventArgs e)
        {
            if (!_busy || _forceClose) return;
            e.Cancel = true;
            if (_closeAfterStop) return;
            string msg = _runKind == KindMix
                ? "正在快拼，确定要退出吗？\n退出会停止当前任务，本批次已生成的成片会删除。"
                : "正在规格统一，确定要退出吗？\n退出会停止当前任务，已统一好的素材会保留，下次接着处理。";
            if (MessageBox.Show(this, msg, "Axe", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            _closeAfterStop = true;
            RequestStop();
            // Should the clean-up ever take unusually long, close anyway.
            var guard = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
            guard.Tick += (s, a) =>
            {
                guard.Stop();
                _forceClose = true;
                Close();
            };
            guard.Start();
        }

        private void OpenOutputFolder()
        {
            string dir = _outBox.Text.Trim();
            try
            {
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                var psi = new ProcessStartInfo("explorer.exe", "\"" + dir + "\"");
                psi.UseShellExecute = true;
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                AppendLog("无法打开输出目录：" + ex.Message);
            }
        }

        private static readonly Brush LogBrush = Theme.SB(Color.FromRgb(220, 226, 234));

        private void AppendLog(string s)
        {
            var para = new Paragraph(new Run(DateTime.Now.ToString("HH:mm:ss") + "  " + s));
            para.Foreground = LogBrush;
            var blocks = _logBox.Document.Blocks;
            blocks.Add(para);
            // Thousands of paragraphs make the RichTextBox crawl; app.log keeps the full history.
            while (blocks.Count > MaxLogParagraphs) blocks.Remove(blocks.FirstBlock);
            _logBox.ScrollToEnd();
            if (_started && _logBody.Visibility != Visibility.Visible) _logDot.Visibility = Visibility.Visible;
        }

        private void SetProgress(int p)
        {
            _progress.Value = p;
        }
    }
}
