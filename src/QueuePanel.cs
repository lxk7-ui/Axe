using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using static VideoMixApp.Theme;

namespace VideoMixApp
{
    // 任务队列 page (v2.7.0): the queued 快拼 tasks in the order they run, each with its state. A
    // finished task opens its folder on double-click; × removes a task that is not running.
    internal sealed class QueuePanel : Grid
    {
        private readonly StackPanel _rows = new StackPanel();
        private readonly StackPanel _empty;
        private readonly TextBlock _count;
        private readonly Button _clear;
        private TextBlock _runningText;
        private ProgressBar _runningBar;

        public event Action<QueueTask> Remove;
        public event Action<QueueTask> Open;
        public event Action ClearFinished;
        // Title and detail line of a task (the main window knows the ratio names).
        public Func<QueueTask, string[]> Describe;

        public QueuePanel()
        {
            RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.0, GridUnitType.Star) });
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = _rows };
            scroll.Resources.Add(typeof(ScrollBar), FolderPicker.ScrollBarStyle());
            Children.Add(scroll);
            // v2.7.1: an empty queue says how a task gets in.
            _empty = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0.0, 70.0, 0.0, 0.0), IsHitTestVisible = false };
            var emptyIcon = MakeIcon(IconQueue, K.LineHi, 40.0);
            emptyIcon.HorizontalAlignment = HorizontalAlignment.Center;
            _empty.Children.Add(emptyIcon);
            _empty.Children.Add(new TextBlock { Text = "没有任务", FontSize = 14.0, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0.0, 12.0, 0.0, 0.0) }.Fg(K.TextSub));
            _empty.Children.Add(new TextBlock { Text = "在左边设置好一个产品，点「加入队列」", FontSize = 12.5, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0.0, 6.0, 0.0, 0.0) }.Fg(K.TextHint));
            Children.Add(_empty);
            var foot = new Grid { Margin = new Thickness(0.0, 10.0, 0.0, 0.0) };
            foot.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
            foot.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _count = new TextBlock { FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center }.Fg(K.TextSub);
            _clear = new Button { Content = "清除已完成", Height = 28.0, Padding = new Thickness(12.0, 0.0, 12.0, 0.0), FontSize = 12.5, Style = MainWindow.MakeSecondaryButtonStyle() };
            _clear.Click += (s, e) =>
            {
                if (ClearFinished != null) ClearFinished();
            };
            Grid.SetColumn(_clear, 1);
            foot.Children.Add(_count);
            foot.Children.Add(_clear);
            Grid.SetRow(foot, 1);
            Children.Add(foot);
        }

        public void Show(IList<QueueTask> tasks, QueueTask running)
        {
            _rows.Children.Clear();
            _runningText = null;
            _runningBar = null;
            int waiting = 0;
            int finished = 0;
            int index = 0;
            foreach (var t in tasks)
            {
                index++;
                if (string.IsNullOrEmpty(t.State)) waiting++;
                else if (t.State == "done" || t.State == "failed") finished++;
                _rows.Children.Add(Row(index, t, t == running || t.State == "running"));
            }
            _empty.Visibility = tasks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            _count.Text = tasks.Count == 0 ? "" : "共 " + tasks.Count + " 个" + (waiting > 0 ? " · 等待 " + waiting : "");
            _clear.Visibility = finished > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        public void SetProgress(int pct)
        {
            pct = Math.Max(0, Math.Min(100, pct));
            if (_runningText != null) _runningText.Text = "进行中 " + pct + "%";
            if (_runningBar != null) _runningBar.Value = pct;
        }

        private UIElement Row(int index, QueueTask t, bool running)
        {
            string[] text = Describe != null ? Describe(t) : new[] { t.SourceRoot, "" };
            var card = new Chamfer
            {
                Cut = 9.0,
                Corners = Cuts.BottomRight,
                BorderThickness = 1.0,
                Padding = new Thickness(12.0, 8.0, 6.0, 8.0),
                Margin = new Thickness(0.0, 0.0, 6.0, 8.0),
                AccentSize = 8.0
            }.Bg(K.Panel2).Bd(running ? K.AccentLine : K.Line);
            if (running) card.SetResourceReference(Chamfer.AccentProperty, K.Accent);
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var num = new TextBlock { Text = index.ToString("00"), FontSize = 15.0, FontFamily = TechFont, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0.0, 0.0, 12.0, 0.0), VerticalAlignment = VerticalAlignment.Center }.Fg(K.Accent);
            g.Children.Add(num);
            var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            texts.Children.Add(new TextBlock { Text = text[0], FontSize = 13.5, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis }.Fg(K.Text));
            texts.Children.Add(new TextBlock { Text = text[1], FontSize = 12.0, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0.0, 2.0, 0.0, 0.0) }.Fg(K.TextHint));
            Grid.SetColumn(texts, 1);
            g.Children.Add(texts);
            string state;
            string key;
            if (running)
            {
                state = "进行中";
                key = K.Accent;
            }
            else if (t.State == "done")
            {
                state = "完成 " + t.Made + " 条";
                key = K.Keep;
            }
            else if (t.State == "failed")
            {
                state = "失败";
                key = K.Danger;
            }
            else
            {
                state = "等待";
                key = K.TextHint;
            }
            var chip = new Chamfer { Cut = 5.0, Corners = Cuts.TopLeft, BorderThickness = 1.0, Padding = new Thickness(8.0, 2.0, 8.0, 3.0), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10.0, 0.0, 4.0, 0.0) }.Bg(K.Soft(key)).Bd(K.Edge(key));
            var stateText = new TextBlock { Text = state, FontSize = 12.0 }.Fg(key == K.TextHint ? K.TextSub : key);
            chip.Child = stateText;
            if (running) _runningText = stateText;
            Grid.SetColumn(chip, 2);
            g.Children.Add(chip);
            var remove = new Button
            {
                Width = 24.0,
                Height = 24.0,
                Focusable = false,
                Cursor = Cursors.Hand,
                ToolTip = "移出队列",
                Visibility = running ? Visibility.Hidden : Visibility.Visible,
                Template = MainWindow.ButtonTpl(K.Clear, K.Soft(K.Danger), K.Edge(K.Danger), null, null, 5.0),
                Content = new System.Windows.Shapes.Path { Data = Geo(IconClose), StrokeThickness = 1.6, Width = 10.0, Height = 10.0, Stretch = Stretch.Uniform }.Stroke(K.TextSub)
            };
            remove.Click += (s, e) =>
            {
                if (Remove != null) Remove(t);
            };
            Grid.SetColumn(remove, 3);
            g.Children.Add(remove);
            if (running)
            {
                // v2.7.1: a thin progress line along the bottom of the running task.
                var stack = new StackPanel();
                stack.Children.Add(g);
                _runningBar = new ProgressBar { Minimum = 0.0, Maximum = 100.0, Margin = new Thickness(0.0, 7.0, 6.0, -2.0), Style = MainWindow.MakeProgressBarStyle(3.0) };
                stack.Children.Add(_runningBar);
                card.Child = stack;
            }
            else
            {
                card.Child = g;
            }
            string tip = "素材：" + t.SourceRoot + (string.IsNullOrEmpty(t.Category) ? "" : "\\" + t.Category) + "\n输出：" + t.OutputRoot;
            if (t.State == "done" && !string.IsNullOrEmpty(t.Result)) tip += "\n成片：" + t.Result + "（双击打开）";
            else if (t.State == "failed" && !string.IsNullOrEmpty(t.Result)) tip += "\n" + t.Result;
            card.ToolTip = tip;
            card.MouseLeftButtonDown += (s, e) =>
            {
                if (e.ClickCount == 2 && t.State == "done" && Open != null) Open(t);
            };
            return card;
        }
    }
}
