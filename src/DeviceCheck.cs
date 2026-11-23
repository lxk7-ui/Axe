using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using static VideoMixApp.Theme;

namespace VideoMixApp
{
    // 设备检测 (v2.6.0): what this PC has and how many videos it should make at once (并行进程). Run
    // the first time Axe starts on a PC; settings.json remembers which PC was checked last, so a
    // copied Axe folder asks again on the next PC.
    //
    // Measured on a 16-thread laptop (i5-12500H, 16 GB), 16 videos of 60 s with 成片效果: 4 at once
    // 96.4 s, 6 at once 93.0 s (three runs), 8 97.1 s, 10 97.2 s (three runs), 16 97.4 s; a batch of
    // 10 took 59 s at both 6 and 10. Beyond a share of the CPU threads more jobs only compete for
    // memory bandwidth (and each gets fewer threads), while each needs 0.3-0.45 GB of memory.
    internal static class DeviceCheck
    {
        // Jobs per CPU thread at the measured optimum, and the memory one job may take.
        private const double JobsPerThread = 0.4;
        private const double GbPerJob = 0.45;
        // Left for Windows, Axe itself and whatever else is open.
        private const double ReservedGb = 3.0;
        private const int MaxJobs = 12;

        [StructLayout(LayoutKind.Sequential)]
        private struct MemoryStatus
        {
            public uint Length;
            public uint MemoryLoad;
            public ulong TotalPhys;
            public ulong AvailPhys;
            public ulong TotalPageFile;
            public ulong AvailPageFile;
            public ulong TotalVirtual;
            public ulong AvailVirtual;
            public ulong AvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GlobalMemoryStatusEx(ref MemoryStatus m);

        private static bool Memory(out ulong total, out ulong available)
        {
            total = 0;
            available = 0;
            try
            {
                var m = new MemoryStatus { Length = (uint)Marshal.SizeOf(typeof(MemoryStatus)) };
                if (!GlobalMemoryStatusEx(ref m)) return false;
                total = m.TotalPhys;
                available = m.AvailPhys;
                return true;
            }
            catch
            {
                return false;
            }
        }

        // Physical memory available right now (including the file cache Windows hands back at once), in
        // MB; -1 when Windows does not tell.
        public static long FreeMemoryMb()
        {
            ulong total;
            ulong available;
            return Memory(out total, out available) ? (long)(available >> 20) : -1;
        }

        // Installed memory in GB (0 when unknown).
        public static double TotalMemoryGb()
        {
            ulong total;
            ulong available;
            return Memory(out total, out available) ? total / 1073741824.0 : 0;
        }

        public static int Threads
        {
            get { return Math.Max(1, Environment.ProcessorCount); }
        }

        // This PC as settings.json remembers it.
        public static string Key()
        {
            return Environment.MachineName + "|" + Threads.ToString(CultureInfo.InvariantCulture) + "|" + ((int)Math.Round(TotalMemoryGb())).ToString(CultureInfo.InvariantCulture);
        }

        // 并行进程 for this PC: by CPU threads, and no more than the memory carries.
        public static int Suggest()
        {
            return Suggest(Threads, TotalMemoryGb());
        }

        internal static int Suggest(int threads, double memoryGb)
        {
            int byCpu = Math.Max(2, (int)Math.Round(threads * JobsPerThread));
            int byMemory = memoryGb > 0 ? Math.Max(1, (int)((memoryGb - ReservedGb) / GbPerJob)) : byCpu;
            return Math.Max(1, Math.Min(MaxJobs, Math.Min(byCpu, byMemory)));
        }

        // CPU (libx264) encodes at once, whatever 并行进程 says (v2.8.3): every encode gets at least 2
        // threads, so more than half the CPU threads in jobs only share the same CPU among more
        // processes. Measured on the 16-thread laptop, 180 s videos with filter, sticker and subtitles:
        // 6, 8 or 10 at once all take 19.5-19.8 s per video, but 10 need 1.8 GB against 1.4 GB for 8
        // (_tools\v30\bench2.ps1), and that memory is what makes the PC sluggish. Stream-copy joins
        // and GPU encodes are not limited by this.
        public static int EncodeCap()
        {
            return EncodeCap(Threads);
        }

        internal static int EncodeCap(int threads)
        {
            return Math.Max(2, threads / 2);
        }

        public static string Summary()
        {
            return string.Format(CultureInfo.InvariantCulture, "CPU {0} 线程 · 内存 {1:0} GB", Threads, TotalMemoryGb());
        }
    }

    // The result of 设备检测, once per PC: the PC, the suggested 并行进程 next to the current one, a hint
    // for 硬件加速 when an NVIDIA card can take the encoding. DialogResult true = use the suggestion.
    internal sealed class DeviceWindow : Window
    {
        public DeviceWindow(int suggested, int current, bool nvidiaHint)
        {
            Width = 400;
            SizeToContent = SizeToContent.Height;
            var body = new StackPanel { Margin = new Thickness(22.0, 6.0, 22.0, 18.0) };
            body.Children.Add(new TextBlock { Text = "这台电脑：" + DeviceCheck.Summary(), FontSize = 13.0 }.Fg(K.Text));
            var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0.0, 12.0, 0.0, 0.0) };
            line.Children.Add(new TextBlock { Text = "建议并行进程：", FontSize = 14.0, VerticalAlignment = VerticalAlignment.Bottom }.Fg(K.Text));
            line.Children.Add(new TextBlock { Text = suggested.ToString(CultureInfo.InvariantCulture), FontSize = 24.0, FontFamily = TechFont, FontWeight = FontWeights.Bold, Margin = new Thickness(2.0, 0.0, 0.0, -2.0), VerticalAlignment = VerticalAlignment.Bottom }.Fg(K.Accent));
            line.Children.Add(new TextBlock { Text = "（现在是 " + current.ToString(CultureInfo.InvariantCulture) + "）", FontSize = 12.5, Margin = new Thickness(6.0, 0.0, 0.0, 3.0), VerticalAlignment = VerticalAlignment.Bottom }.Fg(K.TextSub));
            body.Children.Add(line);
            if (nvidiaHint)
            {
                body.Children.Add(new TextBlock { Text = "检测到 NVIDIA 显卡，建议勾选「硬件加速」", FontSize = 12.5, Margin = new Thickness(0.0, 8.0, 0.0, 0.0) }.Fg(K.TextSub));
            }
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0.0, 18.0, 0.0, 0.0) };
            // (v2.7.1: the main button on the right, as in Axe's other dialogs.)
            var use = new Button { Content = "用建议值", Width = 104, Height = 32, Style = MainWindow.MakePrimaryButtonStyle(), FontSize = 13.0, FontWeight = FontWeights.SemiBold, IsDefault = true };
            use.Click += (s, e) => DialogResult = true;
            var keep = new Button { Content = "保持现在", Width = 92, Height = 32, Margin = new Thickness(0.0, 0.0, 10.0, 0.0), Style = MainWindow.MakeSecondaryButtonStyle(), FontSize = 13.0, IsCancel = true };
            keep.Click += (s, e) => DialogResult = false;
            buttons.Children.Add(keep);
            buttons.Children.Add(use);
            body.Children.Add(buttons);
            DialogChrome.Apply(this, "设备检测", IconBolt, body, false);
        }
    }
}
