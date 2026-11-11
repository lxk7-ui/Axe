using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using static VideoMixApp.Theme;

namespace VideoMixApp
{
    // Downloads the speech model for 识别语音配字幕 (first use only). Closing the window stops the
    // download; the next start continues where it stopped.
    internal sealed class ModelWindow : Window
    {
        private readonly string _appDir;
        private readonly Action<string> _log;
        private readonly ProgressBar _bar;
        private readonly TextBlock _status;
        private readonly Button _go;
        private readonly DispatcherTimer _timer;
        private SpeechModelDownloader _dl;
        private long _lastDone;
        private readonly Stopwatch _clock = new Stopwatch();
        private double _speed;

        public ModelWindow(string appDir, Action<string> log)
        {
            _appDir = appDir;
            _log = log;
            Width = 440;
            SizeToContent = SizeToContent.Height;
            Resources["Bar"] = MainWindow.MakeProgressBarStyle();
            var body = new StackPanel { Margin = new Thickness(22.0, 6.0, 22.0, 18.0) };
            body.Children.Add(new TextBlock
            {
                Text = "语音识别模型 · 约 " + (MixEngine.AsrModelSize >> 20) + "MB",
                FontSize = 13.0,
                Foreground = SB(CTextMain)
            });
            _bar = new ProgressBar { Minimum = 0, Maximum = 1000, Value = 0, Margin = new Thickness(0.0, 12.0, 0.0, 6.0), Style = (Style)Resources["Bar"] };
            body.Children.Add(_bar);
            _status = new TextBlock { FontSize = 12.0, Foreground = SB(CTextHint), TextWrapping = TextWrapping.Wrap, Text = "未下载" };
            body.Children.Add(_status);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0.0, 16.0, 0.0, 0.0) };
            _go = new Button { Content = "下载", Width = 96, Height = 32, Margin = new Thickness(0.0, 0.0, 8.0, 0.0), Style = MainWindow.MakePrimaryButtonStyle(), FontSize = 13.0 };
            _go.Click += (s, e) => GoClick();
            var cancel = new Button { Content = "取消", Width = 80, Height = 32, Style = MainWindow.MakeSecondaryButtonStyle(), FontSize = 13.0 };
            cancel.Click += (s, e) => Close();
            buttons.Children.Add(_go);
            buttons.Children.Add(cancel);
            body.Children.Add(buttons);
            DialogChrome.Apply(this, "下载语音识别模型", IconSparkle, body, false);
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _timer.Tick += (s, e) => Tick();
            Closing += (s, e) =>
            {
                if (_dl != null) _dl.Cancel();
                _timer.Stop();
            };
        }

        private void GoClick()
        {
            if (_dl != null)
            {
                _dl.Cancel();
                return;
            }
            _dl = new SpeechModelDownloader(_appDir, _log);
            _dl.Finished += () => Dispatcher.BeginInvoke(new Action(Done));
            _go.Content = "停止";
            _status.Foreground = SB(CTextHint);
            _status.Text = "正在连接…";
            _lastDone = 0;
            _speed = 0;
            _clock.Restart();
            _timer.Start();
            _dl.Start();
        }

        private void Tick()
        {
            SpeechModelDownloader dl = _dl;
            if (dl == null) return;
            long done = dl.Done;
            double secs = _clock.Elapsed.TotalSeconds;
            if (secs > 0.25)
            {
                double now = (done - _lastDone) / secs;
                _speed = _speed <= 0 ? now : _speed * 0.6 + now * 0.4;
                _lastDone = done;
                _clock.Restart();
            }
            _bar.Value = dl.Total > 0 ? 1000.0 * done / dl.Total : 0;
            if (dl.Phase == "下载") _status.Text = string.Format("已下载 {0}/{1}MB · {2:0.0}MB/s", done >> 20, dl.Total >> 20, _speed / 1048576.0);
            else if (dl.Phase == "校验") _status.Text = "正在校验…";
        }

        private void Done()
        {
            SpeechModelDownloader dl = _dl;
            _dl = null;
            _timer.Stop();
            if (dl != null && dl.Succeeded)
            {
                _bar.Value = 1000;
                _status.Foreground = SB(CSuccessText);
                _status.Text = "✓ 已完成";
                var close = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
                close.Tick += (s, e) =>
                {
                    close.Stop();
                    // The window may have been closed by hand meanwhile; DialogResult would then throw.
                    if (!IsVisible) return;
                    try
                    {
                        DialogResult = true;
                    }
                    catch (InvalidOperationException)
                    {
                        Close();
                    }
                };
                close.Start();
                return;
            }
            _go.Content = "继续下载";
            if (dl != null && dl.Error != null)
            {
                _status.Foreground = SB(CAccentDeep);
                _status.Text = dl.Error;
            }
            else
            {
                _status.Text = "已停止（下次接着下载）";
            }
        }
    }
}
