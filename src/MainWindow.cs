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
        private Rectangle _logDot;
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
        private Chamfer _logBody;
        private Rectangle _statusDot;
        private TextBlock _headerStatus;
        private Chamfer _windowFrame;
        private Button _maxBtn;
        private Border _titleBar;
        private DispatcherTimer _unifyTimer;
        private int _unifySeq;
        private volatile MixEngine _engine;
        // v2.7.0: 素材规则, the combination count and the 任务队列.
        private Button _rulesBtn;
        private TextBlock _comboLine;
        private DispatcherTimer _comboTimer;
        private int _comboSeq;
        private Button _queueBtn;
        private System.Windows.Controls.Primitives.ToggleButton _tabQueue;
        private TextBlock _queueBadge;
        private QueuePanel _queuePanel;
        private volatile bool _queueStop;
        // 开始快拼 with an empty queue: the window's settings as a task of its own (not listed).
        private QueueTask _oneOff;
        private QueueTask _runTask;
        // Why settings.json could not be read at start (shown in the log once the window is built).
        private string _settingsProblem;
        private const int PageEffects = 0;
        private const int PageQueue = 1;
        private const int PageLog = 2;

        public MainWindow()
        {
            _appDir = AppDomain.CurrentDomain.BaseDirectory;
            RollLogFile();
            CleanStaleTasks();
            LoadSettings();
            Unified.UseStore(_appDir);
            // 背景色 before anything is built, so every window starts in the saved scheme.
            Theme.Apply(_settings.Theme);
            Theme.Changed += ApplyGlow;
            BuildStyles();
            _unifyTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _unifyTimer.Tick += (s, e) => RunUnifyCheck();
            _comboTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            _comboTimer.Tick += (s, e) => RunComboCheck();
            BuildUi();
            RefreshQueue();
            WindowResizer.Enable(this, 10.0);
            AppendLog("Axe 已启动");
            if (_settingsProblem != null)
            {
                WriteLogFile(_settingsProblem);
                AppendLog(_settingsProblem);
                ShowTab(true);
            }
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
                // v2.8.3: not while a task runs. Every switch back to Axe read the whole folder again,
                // while 规格统一 was replacing those very files; the task refreshes it when it ends.
                if (_busy) return;
                RefreshUnify();
                _effects.RefreshAssets();
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
            ShowPage(log ? PageLog : PageEffects);
        }

        // 成片效果, 任务队列 or 运行日志.
        private void ShowPage(int page)
        {
            _tabFx.IsChecked = page == PageEffects;
            _tabQueue.IsChecked = page == PageQueue;
            _tabLog.IsChecked = page == PageLog;
            _effects.Visibility = page == PageEffects ? Visibility.Visible : Visibility.Collapsed;
            _queuePanel.Visibility = page == PageQueue ? Visibility.Visible : Visibility.Collapsed;
            _logBody.Visibility = page == PageLog ? Visibility.Visible : Visibility.Collapsed;
            if (page == PageLog)
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
            // 变速 changes how much material a video holds.
            RefreshCombos();
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

        // v2.8.3: lines from a running task are added to the log in one go when the window has time
        // (below input and drawing), instead of one dispatcher call and one layout pass each.
        private readonly System.Collections.Concurrent.ConcurrentQueue<string> _logQueue = new System.Collections.Concurrent.ConcurrentQueue<string>();
        private int _logFlushPending;

        private void RecordLog(string s)
        {
            WriteLogFile(s);
            _logQueue.Enqueue(s);
            if (Interlocked.Exchange(ref _logFlushPending, 1) == 0)
                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(FlushLog));
        }

        private void FlushLog()
        {
            // Cleared first: a line queued while these are added schedules the next flush.
            Volatile.Write(ref _logFlushPending, 0);
            string s;
            int n = 0;
            while (_logQueue.TryDequeue(out s))
            {
                AddLogLine(s);
                n++;
            }
            if (n > 0) EndLogLines();
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
                catch (Exception ex)
                {
                    // v2.8.0: an unreadable file (edited by hand, a broken disk) is kept aside before the
                    // first save replaces it with the defaults, so folders and the queue can be recovered.
                    string bad = System.IO.Path.Combine(_appDir, "settings.bad.json");
                    try
                    {
                        File.Copy(SettingsPath, bad, true);
                        _settingsProblem = "注意：settings.json 无法读取（" + ex.Message + "），已按默认设置启动；原文件另存为 settings.bad.json。";
                    }
                    catch
                    {
                        _settingsProblem = "注意：settings.json 无法读取，已按默认设置启动。";
                    }
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
            // 任务队列: a task still marked as running was cut short (Axe closed or crashed): it waits again.
            if (_settings.Queue == null) _settings.Queue = new List<QueueTask>();
            _settings.Queue.RemoveAll(t => t == null || string.IsNullOrEmpty(t.SourceRoot));
            foreach (var t in _settings.Queue)
            {
                if (t.State == "running" || t.State == null) t.State = "";
            }
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

        // The bitrate floor 规格统一 raises clips to (v2.7.0, as MixEngine.RaiseFloor), 0 = none.
        private int RaiseFloor
        {
            get { return _settings.MinKbps > 0 ? (int)Math.Round(_settings.MinKbps * 1.1) : 0; }
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
                // Maximized windows reach past the screen by the frame width: keep the edge on screen.
                _windowFrame.Margin = new Thickness(8.0);
                _windowFrame.Cut = 0.0;
                if (_maxBtn != null) _maxBtn.Tag = Theme.Geo(Theme.IconRestore);
            }
            else
            {
                _windowFrame.Margin = new Thickness(10.0);
                _windowFrame.Cut = 18.0;
                if (_maxBtn != null) _maxBtn.Tag = Theme.Geo(Theme.IconMax);
            }
            if (_glowFrame != null)
            {
                _glowFrame.Margin = _windowFrame.Margin;
                _glowFrame.Cut = _windowFrame.Cut;
            }
        }

        private void PickFolder(TextBox target)
        {
            // For the source folder the picker also shows which videos are unified for the chosen ratio.
            var dlg = target == _srcBox ? new FolderPicker(target.Text, CacheDirFor(_ratioCombo.SelectedIndex), MarkFor(_ratioCombo.SelectedIndex), RaiseFloor) : new FolderPicker(target.Text);
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
            RefreshCombos();
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
            string appDir = _appDir;
            int floor = RaiseFloor;
            int seq = ++_unifySeq;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                FolderStatus st;
                try
                {
                    st = UnifyRecord.Load(cache, mark, floor).CheckFolder(root, cat);
                }
                catch (Exception ex)
                {
                    st = new FolderStatus { Error = ex.Message };
                }
                if (st.Error == null && st.Total > 0)
                {
                    try
                    {
                        new ClipRules(appDir).Count(MixEngine.ScanSources(root, cat), out st.NoOpen, out st.NoUse);
                    }
                    catch
                    {
                        // An unreadable rules file is reported when 快拼 starts.
                    }
                }
                Unified.SaveStore();
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (seq == _unifySeq) ShowUnify(_unifyLine, st);
                }));
            });
        }

        // 素材规则 of the chosen folder (and category).
        private void EditRules()
        {
            string root = _srcBox.Text.Trim();
            if (!Directory.Exists(root))
            {
                MessageDialog.Show(this, "素材目录不存在。");
                return;
            }
            int r = Math.Max(0, _ratioCombo.SelectedIndex);
            var dlg = new ClipRulesWindow(_appDir, root, SelectedCategory(), RatioW[r], RatioH[r]) { Owner = this };
            if (dlg.ShowDialog() != true) return;
            RecordLog("素材规则已保存：" + (dlg.SavedSummary.Length > 0 ? dlg.SavedSummary : "没有规则") + "（" + root + (SelectedCategory().Length > 0 ? "\\" + SelectedCategory() : "") + "）");
            RefreshUnify();
        }

        // 已用组合 / 剩余组合 / 总组合 for the folder, length and 变速 in the window (counted off the UI
        // thread, a moment after the last change).
        private void RefreshCombos()
        {
            if (_comboTimer == null || _comboLine == null) return;
            _comboTimer.Stop();
            _comboTimer.Start();
        }

        private void RunComboCheck()
        {
            _comboTimer.Stop();
            string root = _srcBox.Text.Trim();
            string cat = SelectedCategory();
            int r = Math.Max(0, _ratioCombo.SelectedIndex);
            int w = RatioW[r];
            int h = RatioH[r];
            double target;
            double tol;
            int count;
            if (!double.TryParse(_durBox.Text.Trim(), out target) || !(target > 0) || !double.TryParse(_tolBox.Text.Trim(), out tol) || tol < 0 || tol >= target)
            {
                // A count still running for the previous values must not bring its figures back.
                _comboSeq++;
                SetComboView(null);
                return;
            }
            if (!int.TryParse(_countBox.Text.Trim(), out count)) count = 0;
            string intro = _introBox.Text.Trim();
            double speed = EffectOptions.ValidSpeed(_settings.Speed);
            string cache = CacheDirFor(r);
            string mark = MarkFor(r);
            string appDir = _appDir;
            int floor = RaiseFloor;
            int seq = ++_comboSeq;
            // A newer request makes this count stop early (typing a length starts one per pause).
            Func<bool> stale = () => seq != Volatile.Read(ref _comboSeq);
            ThreadPool.QueueUserWorkItem(_ =>
            {
                ComboView view = null;
                try
                {
                    if (Directory.Exists(root))
                    {
                        view = CountCombos(appDir, root, cat, cache, mark, floor, w, h, target, tol, intro, speed, count, stale);
                        // A newer request took over.
                        if (view == null) return;
                    }
                }
                catch
                {
                    view = null;
                }
                Unified.SaveStore();
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (seq == _comboSeq) SetComboView(view);
                }));
            });
        }

        // What the 组合 line shows, and (v2.7.1) the same as four figures for the block.
        private sealed class ComboView
        {
            public string Text;
            public string Tip;
            public bool Warn;
            // 素材, 已用组合, 剩余组合, 总组合; null when Text is a message instead (总时长不够 …).
            public string[] Figures;
        }

        // The 组合 line as text (EngineTest4 calls this by name and parameter names).
        // false: a newer request took over (stale), nothing to show.
        private static bool Combos(string appDir, string root, string cat, string cache, string mark, int floor, int w, int h, double target, double tol,
            string intro, double speed, int count, Func<bool> stale, out string text, out string tip, out bool warn)
        {
            ComboView v = CountCombos(appDir, root, cat, cache, mark, floor, w, h, target, tol, intro, speed, count, stale);
            text = v != null ? v.Text : null;
            tip = v != null ? v.Tip : null;
            warn = v != null && v.Warn;
            return v != null;
        }

        // The clips 快拼 would use (not failed, not 成片不用, readable) with their lengths as 快拼 sees
        // them, and the length range of the clips of one video (片头 and 变速 counted as in Mix).
        // null: a newer request took over (stale), nothing to show.
        private static ComboView CountCombos(string appDir, string root, string cat, string cache, string mark, int floor, int w, int h, double target, double tol,
            string intro, double speed, int count, Func<bool> stale)
        {
            var record = UnifyRecord.Load(cache, mark, floor);
            var rules = new ClipRules(appDir);
            var clips = new List<ComboClip>();
            foreach (var f in MixEngine.ScanSources(root, cat))
            {
                if (stale != null && stale()) return null;
                if (record.Check(f) == UnifyRecord.State.Failed) continue;
                ClipRule rule = ClipRule.None;
                try { rule = rules.Of(f.FullName); } catch (ClipRulesException) { }
                if ((rule & ClipRule.NoUse) != 0) continue;
                PoolClip c = record.Usable(f);
                double d = c != null ? c.Duration : Unified.Read(f).Duration;
                if (!(d > 0.2)) continue;
                clips.Add(new ComboClip
                {
                    Id = "norm_" + MixEngine.MakeTag(f.FullName) + ".mp4",
                    Path = f.FullName,
                    Key = History.NameOf(f.FullName).ToLowerInvariant(),
                    Duration = d,
                    NoOpen = (rule & ClipRule.NoOpen) != 0
                });
            }
            double introDur = 0;
            if (intro.Length > 0 && File.Exists(intro)) introDur = Math.Max(0, Unified.Read(new FileInfo(intro)).Duration);
            double lo = target - tol;
            double hi = target + tol;
            if (hi - introDur < 1.0) return new ComboView { Text = "素材 " + clips.Count + " 条 · 片头比成片还长", Warn = true };
            double bodyMin = Math.Max(0.0, lo - introDur) * speed;
            double bodyMax = (hi - introDur) * speed;
            // Shown even when the history cannot be read right now (another program holding it).
            History history = History.Load(appDir) ?? new History();
            ComboStats st = ComboCounter.Stats(clips, bodyMin, bodyMax, history, w + "x" + h, root, cat,
                target.ToString("0", System.Globalization.CultureInfo.InvariantCulture), tol.ToString("0", System.Globalization.CultureInfo.InvariantCulture), stale);
            if (st == null) return null;
            var range = string.Format("按每条成片 {0:0.#}~{1:0.#} 秒{2}{3}计算。", lo, hi, introDur > 0 ? string.Format("（片头 {0:0.#} 秒不计）", introDur) : "",
                Math.Abs(speed - 1.0) > 0.001 ? "、变速 " + EffectOptions.SpeedText(speed) : "");
            if (st.Clips == 0) return new ComboView { Text = "素材 0 条" };
            if (st.TooShort)
            {
                return new ComboView
                {
                    Text = "素材 " + st.Clips + " 条 · 总时长不够一条成片",
                    Tip = "所有素材加起来比一条成片还短，成片里会有重复的片段。" + range,
                    Warn = true
                };
            }
            // "约" where a number is estimated (old records counted by folder; a coarse count).
            string about = st.TotalApprox && st.Total < 1e16 ? "约" : "";
            string used = (st.UsedApprox ? "约" : "") + ComboCounter.Text(st.Used);
            string remaining = about + ComboCounter.Text(st.Remaining);
            string total = about + ComboCounter.Text(st.Total);
            bool warn = count > 0 && st.Remaining < count;
            return new ComboView
            {
                Text = string.Format("素材 {0} 条 · 已用组合 {1} · 剩余组合 {2} · 总组合 {3}", st.Clips, used, remaining, total),
                Figures = new[] { st.Clips.ToString(), used, remaining, total },
                Warn = warn,
                Tip = range + (warn ? string.Format("\n剩余组合比成片数量 {0} 少，这一批最多出 {1} 条不重复的成片。", count, ComboCounter.Text(st.Remaining)) : "")
                    + (st.UsedApprox ? "\nv2.7.0 以前出的成片没有记下用了哪些素材，按素材目录和时长估算。" : "")
            };
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
                line.Fg(K.TextHint);
                return;
            }
            string rules = ClipRules.Summary(st.NoOpen, st.NoUse);
            string tail = (st.LowRate > 0 ? " · 码率待提高 " + st.LowRate : "") + (rules.Length > 0 ? " · " + rules : "");
            if (st.LowRate > 0) line.ToolTip = "有 " + st.LowRate + " 个视频的码率低于平台要求，下次规格统一（或快拼前自动做的那一步）会把它们的码率提高一次。";
            if (st.Waiting.Count == 0)
            {
                line.Text = "✓ 已全部统一（" + st.Total + " 个视频）" + tail;
                line.Fg(st.LowRate > 0 ? K.Warn : K.Keep);
                return;
            }
            line.Fg(K.Warn);
            if (st.Waiting.Count == st.Total)
            {
                line.Text = "未统一：共 " + st.Total + " 个视频" + tail;
            }
            else
            {
                // One line with ellipsis: measuring thousands of names would only slow the window down.
                int shown = Math.Min(st.Waiting.Count, 30);
                line.Text = string.Format("未统一 {0}/{1}{4}：{2}{3}", st.Waiting.Count, st.Total, string.Join("、", st.Waiting.GetRange(0, shown).ToArray()), shown < st.Waiting.Count ? "…" : "", tail);
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
                    MessageDialog.Show(this, "当前显卡/驱动不支持硬件编码，将继续使用 CPU 软件编码。");
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
                MessageDialog.Show(this, "片头素材文件不存在，请重新选择或清空。");
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
                MessageDialog.Show(this, "请输入正确的成片数量。");
                return false;
            }
            _settings.Count = n;
            double d;
            if (!double.TryParse(_durBox.Text.Trim(), out d) || !(d > 0.0))
            {
                MessageDialog.Show(this, "请输入正确的目标时长。");
                return false;
            }
            _settings.DurationSec = d;
            if (!double.TryParse(_tolBox.Text.Trim(), out d) || d < 0.0)
            {
                MessageDialog.Show(this, "请输入正确的浮动秒数（0 或正数）。");
                return false;
            }
            _settings.ToleranceSec = d;
            if (!(_settings.ToleranceSec < _settings.DurationSec))
            {
                MessageDialog.Show(this, "浮动秒数需小于目标时长。");
                return false;
            }
            if (!int.TryParse(_parallelBox.Text.Trim(), out n) || n <= 0)
            {
                MessageDialog.Show(this, "请输入正确的并行数。");
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
                MessageDialog.Show(this, "素材目录不存在。");
                return false;
            }
            if (_settings.OutputRoot.Length == 0)
            {
                MessageDialog.Show(this, "请选择输出目录。");
                return false;
            }
            try
            {
                if (!Directory.Exists(_settings.OutputRoot)) Directory.CreateDirectory(_settings.OutputRoot);
            }
            catch (Exception ex)
            {
                MessageDialog.Show(this, "无法创建输出目录：" + ex.Message);
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
            _statusKey = busy ? K.Running : K.Keep;
            _statusDot.Fill(_statusKey);
            ApplyGlow();
        }

        // While a task runs the big button turns into 停止; with tasks waiting in the queue it starts them.
        private void UpdateMixButton()
        {
            _mixBtn.IsEnabled = !_stopping;
            _mixBtn.Style = (Style)Resources[_busy ? "StopButton" : "PrimaryButton"];
            _mixIcon.Data = Theme.Geo(_busy ? Theme.IconStop : Theme.IconPlay);
            int waiting = WaitingCount();
            _mixText.Text = _stopping ? "正在停止…" : (_busy ? "停止" : (waiting > 0 ? "开始队列（" + waiting + "）" : "开始快拼"));
        }

        private void SetRunningStatus(string s)
        {
            if (_busy && !_stopping) _statusText.Text = s;
        }

        // 开始快拼: the window's settings as one task. 开始队列（N）: the waiting tasks of the 任务队列.
        // Either way, tasks added to the queue meanwhile run afterwards.
        private void MixClick(object sender, RoutedEventArgs e)
        {
            if (_busy)
            {
                StopClick();
                return;
            }
            bool queue = WaitingCount() > 0;
            if (queue)
            {
                if (!CollectPerformance() || !ConfirmReplace()) return;
                bool subs = false;
                foreach (var t in Queue) if (string.IsNullOrEmpty(t.State) && t.Subtitles) subs = true;
                if (subs && !SpeechModelForQueue()) return;
                _oneOff = null;
            }
            else
            {
                if (!CollectInputs() || !ConfirmReplace()) return;
                if (_settings.Subtitles && !SpeechModelForQueue()) return;
                _oneOff = Snapshot();
            }
            RunTasks();
        }

        private bool SpeechModelForQueue()
        {
            if (MixEngine.SubtitleModelReady(_appDir) || DownloadSpeechModel()) return true;
            MessageDialog.Show(this, "还没有语音识别模型，无法配字幕。\n下载模型，或取消勾选「识别语音配字幕」后再开始。");
            return false;
        }

        // 加入队列: the window's settings as a task at the end of the queue (also while one runs).
        private void QueueAddClick()
        {
            if (!CollectInputs()) return;
            if (_settings.Subtitles && !SpeechModelForQueue()) return;
            QueueTask t = Snapshot();
            Queue.Add(t);
            SaveSettings();
            RefreshQueue();
            ShowPage(PageQueue);
            string[] d = DescribeTask(t);
            AppendLog(string.Format("已加入任务队列（第 {0} 个）：{1} · {2}", Queue.Count, d[0], d[1]));
        }

        private List<QueueTask> Queue
        {
            get { return _settings.Queue ?? (_settings.Queue = new List<QueueTask>()); }
        }

        private int WaitingCount()
        {
            int n = 0;
            if (_settings == null || _settings.Queue == null) return 0;
            foreach (var t in _settings.Queue) if (string.IsNullOrEmpty(t.State)) n++;
            return n;
        }

        private QueueTask Snapshot()
        {
            AppSettings s = _settings;
            return new QueueTask
            {
                SourceRoot = s.SourceRoot,
                Category = s.Category ?? "",
                OutputRoot = s.OutputRoot,
                IntroFile = s.IntroFile ?? "",
                NameFormat = s.NameFormat,
                Width = s.Width,
                Height = s.Height,
                Count = s.Count,
                DurationSec = s.DurationSec,
                ToleranceSec = s.ToleranceSec,
                Filter = s.Filter ?? "",
                FilterStrength = s.FilterStrength,
                Sticker = s.Sticker ?? "",
                Speed = s.Speed,
                Subtitles = s.Subtitles,
                SubX = s.SubX,
                SubY = s.SubY,
                SubSize = s.SubSize,
                StickerX = s.StickerX,
                StickerY = s.StickerY,
                StickerSize = s.StickerSize,
                State = ""
            };
        }

        // 并行进程 and 硬件加速, which every task of a queue run uses.
        private bool CollectPerformance()
        {
            int n;
            if (!int.TryParse(_parallelBox.Text.Trim(), out n) || n <= 0)
            {
                MessageDialog.Show(this, "请输入正确的并行数。");
                return false;
            }
            _settings.ParallelJobs = n;
            _settings.MixParallel = n;
            _settings.UseNvenc = _nvencCheck.IsChecked == true;
            SaveSettings();
            return true;
        }

        // Title and detail line of a task: "布基双面胶", "10 条 · 180±40 秒 · 9:16 · 滤镜 随机 · 1.5x".
        private string[] DescribeTask(QueueTask t)
        {
            var parts = new List<string> { t.Count + " 条", t.DurationSec.ToString("0.#") + "±" + t.ToleranceSec.ToString("0.#") + " 秒" };
            int r = FindRatioIndex(t.Width, t.Height);
            parts.Add(RatioW[r] == t.Width && RatioH[r] == t.Height ? RatioLabels[r].Split(' ')[0] : t.Width + "x" + t.Height);
            if (!string.IsNullOrEmpty(t.Filter)) parts.Add("滤镜 " + t.Filter);
            if (!string.IsNullOrEmpty(t.Sticker)) parts.Add("贴纸 " + t.Sticker);
            double speed = EffectOptions.ValidSpeed(t.Speed);
            if (Math.Abs(speed - 1.0) > 0.001) parts.Add(EffectOptions.SpeedText(speed));
            if (t.Subtitles) parts.Add("字幕");
            if (!string.IsNullOrEmpty(t.IntroFile)) parts.Add("片头");
            return new[] { MixEngine.ProductName(t.SourceRoot, t.Category), string.Join(" · ", parts.ToArray()) };
        }

        private void RefreshQueue()
        {
            if (_queuePanel == null) return;
            _queuePanel.Show(Queue, _runTask);
            int waiting = WaitingCount();
            int shown = waiting + (_runTask != null && _runTask != _oneOff ? 1 : 0);
            _queueBadge.Text = shown > 0 ? shown.ToString() : "";
            _queueBadge.Visibility = shown > 0 ? Visibility.Visible : Visibility.Collapsed;
            UpdateMixButton();
        }

        private void RemoveTask(QueueTask t)
        {
            if (t == _runTask) return;
            Queue.Remove(t);
            SaveSettings();
            RefreshQueue();
        }

        private void ClearFinishedTasks()
        {
            Queue.RemoveAll(t => t != _runTask && (t.State == "done" || t.State == "failed"));
            SaveSettings();
            RefreshQueue();
        }

        private void OpenTaskFolder(QueueTask t)
        {
            try
            {
                if (!string.IsNullOrEmpty(t.Result) && Directory.Exists(t.Result)) Process.Start(new ProcessStartInfo("explorer.exe", "\"" + t.Result + "\"") { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                AppendLog("无法打开成片文件夹：" + ex.Message);
            }
        }

        // The next task to make (on the UI thread): the one-off first, then the queue in order. A task
        // whose settings cannot be right (settings.json edited by hand) fails at once instead.
        private QueueTask NextTask()
        {
            if (_oneOff != null && string.IsNullOrEmpty(_oneOff.State)) return _oneOff;
            foreach (var t in Queue)
            {
                if (!string.IsNullOrEmpty(t.State)) continue;
                if (t.Width <= 0 || t.Height <= 0 || t.Count <= 0 || !(t.DurationSec > 0) || t.ToleranceSec < 0 || t.ToleranceSec >= t.DurationSec
                    || string.IsNullOrWhiteSpace(t.SourceRoot) || string.IsNullOrWhiteSpace(t.OutputRoot))
                {
                    t.State = "failed";
                    t.Result = "任务设置不完整（数量、时长或目录），请移出队列后重新加入";
                    continue;
                }
                return t;
            }
            return null;
        }

        // Makes the tasks one after another on a worker thread. 停止 ends the running task (its videos
        // are removed, as for a single 快拼, and it waits again); tasks finished before stay as they
        // are, and the next ones do not start.
        private void RunTasks()
        {
            _runKind = KindMix;
            _queueStop = false;
            SetBusy(true);
            int parallel = _settings.MixParallel;
            bool hw = _settings.UseNvenc;
            var worker = new Thread(() =>
            {
                int doneTasks = 0;
                int failedTasks = 0;
                int made = 0;
                string last = null;
                bool stopped = false;
                while (true)
                {
                    QueueTask task = null;
                    MixEngine engine = null;
                    int number = 0;
                    int of = 0;
                    Dispatcher.Invoke(new Action(() =>
                    {
                        if (_queueStop) return;
                        task = NextTask();
                        if (task == null) return;
                        try
                        {
                            engine = CreateEngine(task);
                        }
                        catch (Exception ex)
                        {
                            RecordLog("错误: " + ex.Message);
                            task.State = "failed";
                            task.Result = "错误：" + ex.Message;
                            failedTasks++;
                            engine = null;
                            SaveSettings();
                            RefreshQueue();
                            return;
                        }
                        _engine = engine;
                        _runTask = task;
                        task.State = "running";
                        number = doneTasks + failedTasks + 1;
                        // This one, the ones done in this run and the ones still waiting.
                        of = number + WaitingCount();
                        SaveSettings();
                        RefreshQueue();
                    }));
                    if (task == null) break;
                    // Its engine could not be made: on to the next one.
                    if (engine == null) continue;
                    string[] d = DescribeTask(task);
                    if (of > 1) RecordLog(string.Format("▶ 任务 {0}/{1}：{2} · {3}", number, of, d[0], d[1]));
                    string result = null;
                    string error = null;
                    try
                    {
                        engine.PreferHardwareAccel(hw);
                        result = engine.Mix(task.Count, task.DurationSec, task.ToleranceSec, parallel, task.SourceRoot, task.Category, task.OutputRoot, task.IntroFile, task.NameFormat);
                    }
                    catch (Exception ex)
                    {
                        error = ex.Message;
                        RecordLog("错误: " + ex.Message);
                        RecordLog(ex.ToString());
                    }
                    Unified.SaveStore();
                    bool cancelled = engine.IsCancelled;
                    int count = engine.LastMade;
                    Dispatcher.Invoke(new Action(() =>
                    {
                        _engine = null;
                        _runTask = null;
                        if (cancelled)
                        {
                            // Its videos are removed: it waits to be made again.
                            task.State = "";
                            task.Result = null;
                        }
                        else if (result != null)
                        {
                            task.State = "done";
                            task.Result = result;
                            task.Made = count;
                        }
                        else
                        {
                            task.State = "failed";
                            task.Result = error != null ? "错误：" + error : "没有出片，原因见运行日志";
                        }
                        if (task == _oneOff && cancelled) _oneOff = null;
                        SaveSettings();
                        RefreshQueue();
                        RefreshCombos();
                    }));
                    if (cancelled)
                    {
                        stopped = true;
                        break;
                    }
                    if (result != null)
                    {
                        doneTasks++;
                        made += count;
                        last = result;
                    }
                    else
                    {
                        failedTasks++;
                    }
                }
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    _oneOff = null;
                    // Tasks NextTask found unusable were marked failed on the way.
                    SaveSettings();
                    SetBusy(false);
                    int tasks = doneTasks + failedTasks;
                    if (stopped) _statusText.Text = doneTasks > 0 ? string.Format("已停止（之前完成的 {0} 个任务保留）", doneTasks) : "已停止";
                    else if (tasks > 1) _statusText.Text = string.Format("队列完成：{0} 个任务，共 {1} 条成片{2}", doneTasks, made, failedTasks > 0 ? "，" + failedTasks + " 个失败" : "");
                    else if (last != null) _statusText.Text = "完成：" + last;
                    if (tasks > 1 || stopped && doneTasks > 0) RecordLog(_statusText.Text);
                    RefreshQueue();
                    RefreshUnify();
                    if (_closeAfterStop) Close();
                }));
            });
            worker.IsBackground = true;
            worker.Start();
        }

        // The engine for one task: its folder, ratio and 成片效果; quality settings from the window.
        private MixEngine CreateEngine(QueueTask t)
        {
            MixEngine e = CreateEngine();
            e.CacheDir = System.IO.Path.Combine(_appDir, "_cache_" + t.Width + "x" + t.Height);
            e.Configure(t.Width, t.Height, _settings.FPS, _settings.CRF, _settings.SegmentSeconds);
            e.Effects = new EffectOptions
            {
                Filter = t.Filter ?? "",
                FilterStrength = EffectOptions.ValidStrength(t.FilterStrength / 100.0),
                Sticker = t.Sticker ?? "",
                Speed = EffectOptions.ValidSpeed(t.Speed),
                Subtitles = t.Subtitles,
                SubX = t.SubX,
                SubY = t.SubY,
                SubSize = t.SubSize,
                StickerX = t.StickerX,
                StickerY = t.StickerY,
                StickerSize = t.StickerSize
            };
            e.Progress = p => Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action<int>(pp =>
            {
                SetProgress(pp);
                _queuePanel.SetProgress(pp);
            }), p);
            return e;
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
            if (!MessageDialog.Confirm(this, "替换原视频", msg, "继续", "取消", MessageDialog.Kind.Question)) return false;
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
            if (!_busy || _stopping) return;
            if (!MessageDialog.Confirm(this, "停止", StopQuestion(false), "停止", "继续运行", MessageDialog.Kind.Danger)) return;
            RequestStop();
        }

        // What 停止 (or closing the window) removes and keeps.
        private string StopQuestion(bool closing)
        {
            if (_runKind != KindMix)
            {
                return closing ? "正在规格统一，确定要退出吗？\n退出会停止当前任务，已统一好的素材会保留，下次接着处理。"
                    : "确定停止规格统一吗？\n已经统一好的素材会保留，下次接着处理剩下的。";
            }
            QueueTask t = _runTask;
            bool queue = WaitingCount() > 0 || (t != null && t != _oneOff) || Queue.Exists(q => q.State == "done");
            if (!queue)
            {
                return closing ? "正在快拼，确定要退出吗？\n退出会停止当前任务，本批次已生成的成片会删除。"
                    : "确定停止快拼吗？\n本批次已经生成的成片会全部删除。";
            }
            string name = t != null ? "「" + MixEngine.ProductName(t.SourceRoot, t.Category) + "」" : "当前任务";
            return (closing ? "正在快拼，确定要退出吗？\n" : "确定停止吗？\n") + "正在做的" + name + "这一批成片会删除；已经做完的任务保留，后面的任务不再开始。";
        }

        private void RequestStop()
        {
            if (!_busy || _stopping) return;
            _queueStop = true;
            _stopping = true;
            UpdateMixButton();
            _statusText.Text = "正在停止…";
            AppendLog("正在停止，请稍候…");
            MixEngine engine = _engine;
            if (engine != null) engine.Cancel();
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
            if (!MessageDialog.Confirm(this, "退出", StopQuestion(true), "退出", "继续运行", MessageDialog.Kind.Danger)) return;
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

        // Errors red, warnings and stops amber, stage and task titles in the accent colour, finished
        // batches green, the indented detail lines dimmer (v2.7.1; v2.8.0: colours of the 背景色).
        private static string LogColor(string s, out bool bold)
        {
            string t = s.TrimStart();
            bold = false;
            Func<string, bool> starts = p => t.StartsWith(p, StringComparison.Ordinal);
            if (starts("错误") || starts("失败")) return K.Danger;
            if (starts("警告") || starts("注意") || starts("已停止")) return K.Warn;
            if (starts("===") || starts("▶"))
            {
                bold = true;
                return K.Accent;
            }
            if (starts("全部完成") || starts("规格统一完成") || starts("识别语音完成") || starts("队列完成")) return K.Keep;
            return s.StartsWith(" ", StringComparison.Ordinal) ? K.LogDim : K.LogText;
        }

        private void AppendLog(string s)
        {
            AddLogLine(s);
            EndLogLines();
        }

        private void AddLogLine(string s)
        {
            bool bold;
            string color = LogColor(s, out bold);
            var para = new Paragraph { Margin = new Thickness(0.0, 0.0, 0.0, 3.0) };
            var time = new Run(DateTime.Now.ToString("HH:mm:ss") + "  ");
            time.SetResourceReference(TextElement.ForegroundProperty, K.LogDim);
            para.Inlines.Add(time);
            var text = new Run(s) { FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal };
            text.SetResourceReference(TextElement.ForegroundProperty, color);
            para.Inlines.Add(text);
            _logBox.Document.Blocks.Add(para);
        }

        private void EndLogLines()
        {
            var blocks = _logBox.Document.Blocks;
            // Thousands of paragraphs make the RichTextBox crawl; app.log keeps the full history.
            while (blocks.Count > MaxLogParagraphs) blocks.Remove(blocks.FirstBlock);
            // A hidden log is scrolled to the end when it is shown (ShowPage).
            if (_logBody.Visibility == Visibility.Visible) _logBox.ScrollToEnd();
            else if (_started) _logDot.Visibility = Visibility.Visible;
        }

        private void SetProgress(int p)
        {
            _progress.Value = p;
        }
    }
}
