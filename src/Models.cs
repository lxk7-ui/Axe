using System.Collections.Generic;

namespace VideoMixApp
{
    public static class AppInfo
    {
        public const string Version = "v2.8.3";
    }

    // The field names below are persisted in manifest.json / settings.json by
    // JavaScriptSerializer; keep them unchanged so caches from older versions stay valid.
    public class SpecInfo
    {
        public int width;
        public int height;
        public int fps;
        public int gop;
        public int crf;
        public int segmentSeconds;
    }

    public class SourceEntry
    {
        public string tag;
        public string path;
        public string category;
        public long size;
        public string lastWrite;
    }

    public class ClipEntry
    {
        public string file;
        public double duration;
        public string category;
        public int sourceIndex;
        // The clip was verified to carry a sound track (false in manifests of older versions,
        // whose clips are checked once).
        public bool audio;
    }

    public class Manifest
    {
        public string builtAt;
        public SpecInfo spec;
        public string sourceSignature;
        public List<SourceEntry> sources;
        public List<ClipEntry> clips;
        // Sources whose last unification failed (absent in manifests of older versions). 快拼's automatic
        // incremental pass skips them until the file changes; the 规格统一 button retries them.
        public List<SourceEntry> failed;
    }

    public class AppSettings
    {
        public string SourceRoot;
        public string OutputRoot;
        public string IntroFile;
        public string Category;
        public int Count;
        public double DurationSec;
        public double ToleranceSec;
        public int ParallelJobs;
        public int MixParallel;
        public int SegmentSeconds;
        public int Width;
        public int Height;
        public int FPS;
        public int CRF;
        // Historical name: now means "use any available GPU encoder (NVIDIA / Intel / AMD)".
        public bool UseNvenc;
        // 成片命名 template, see OutputNaming.
        public string NameFormat;
        // 成片效果 (v2.3.0; absent in older settings files, which load as "off"): 滤镜 and 贴纸 are "",
        // "随机" or a name; Speed 0 means 1x. Positions and sizes are relative, see EffectOptions.
        public string Filter;
        public string Sticker;
        public double Speed;
        public bool Subtitles;
        public double SubX;
        public double SubY;
        public double SubSize;
        public double StickerX;
        public double StickerY;
        public double StickerSize;
        // The one-time notice that 规格统一 replaces the source videos was confirmed.
        public bool ReplaceConfirmed;
        // 滤镜强度 in per cent (v2.5.0; 0 in older settings files means 100).
        public int FilterStrength;
        // Lowest video bitrate of the outputs in kbps (v2.5.0; 0 in older settings files means 1000,
        // a negative number turns the check off).
        public int MinKbps;
        // The PC 设备检测 last ran on (v2.6.0, see DeviceCheck.Key); another PC is checked on first start.
        public string DeviceKey;
        // 任务队列 (v2.7.0; absent in older settings files, which load as empty).
        public List<QueueTask> Queue;
        // 背景色 (v2.8.0, a Palette id; absent or unknown means the first scheme).
        public string Theme;
    }

    // One 快拼 of the 任务队列 (v2.7.0): the main window's settings when it was added. Runs one after
    // another; 并行进程 and 硬件加速 are taken from the window when the queue starts.
    public class QueueTask
    {
        public string SourceRoot;
        public string Category;
        public string OutputRoot;
        public string IntroFile;
        public string NameFormat;
        public int Width;
        public int Height;
        public int Count;
        public double DurationSec;
        public double ToleranceSec;
        public string Filter;
        public int FilterStrength;
        public string Sticker;
        public double Speed;
        public bool Subtitles;
        public double SubX;
        public double SubY;
        public double SubSize;
        public double StickerX;
        public double StickerY;
        public double StickerSize;
        // "" = waiting (also after it was stopped: its videos are removed), "running", "done", "failed".
        public string State;
        // Done: the batch folder and how many videos it holds. Failed: why (the last error logged).
        public string Result;
        public int Made;
    }
}
