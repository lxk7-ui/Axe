<h1 align="center">Axe</h1>

<p align="center">
  批量视频快拼工具：素材规格统一 · 无损拼接 · 跨批次去重 · 滤镜贴纸变速 · 识别语音配字幕 · 解压即用
</p>

<p align="center">
  <img src="https://img.shields.io/badge/Windows-10%20%7C%2011%20x64-0078D6" alt="Windows 10 | 11 x64">
  <img src="https://img.shields.io/badge/.NET%20Framework-4.6%2B-512BD4" alt=".NET Framework 4.6+">
  <img src="https://img.shields.io/badge/C%23-WPF-239120" alt="C# WPF">
  <img src="https://img.shields.io/badge/FFmpeg-GPL%20v3-007808" alt="FFmpeg">
</p>

把一个产品文件夹里的**自有或已授权**视频素材，随机组合成一批时长相近、**互不重复**的成片。
先把素材统一成相同规格，之后的拼接就是无损 copy，不重新编码，10 条 3 分钟的成片几秒钟出完。
Windows 绿色软件：解压即用，不安装、不写注册表，整个文件夹可以拷到别的电脑。

当前版本 **v2.6.0**，各版本的变化见 [更新记录](CHANGELOG.md)。

## 功能

- **两步出片**：第一步「规格统一」把每个素材整体转成相同规格（默认 720x1280、30fps、H.264/AAC），不切段、不裁剪；第二步「快拼」从统一好的素材里随机抽取，无损直接拼接。
- **成片去重（跨天、跨批次）**：对每条成片的素材顺序计算指纹并持久保存，今天、明天乃至以后生成的成片，都不会和历史上任何一条出现完全相同的组合。
- **成片规格**：5 种比例（9:16、16:9、1:1、3:4、4:3，只缩放补边、不裁剪）；目标时长 ± 浮动秒数；可选固定片头；成片命名支持 `{产品}` `{日期}` `{序号}` `{批次}` `{时长}` 变量。
- **成片效果，所见即所得**：12 种滤镜（可调强度）、41 个动态贴纸、1x～4x 变速（声音音调不变）；右侧实时预览，贴纸和字幕直接在预览里拖动、滚轮调大小。
- **识别语音配字幕**：在本机识别素材里的人声（SenseVoice + sherpa-onnx，不上传任何内容），按说话时间把字幕烧录进成片。
- **码率保障**：成片视频码率不够 1000 kbps 的自动重新编码到约 1350 kbps，避免投放平台提示「视频码率不小于1000kbps」；够的照常无损拼接。
- **硬件加速**：NVIDIA / Intel 核显 / AMD 显卡编码，显卡与 CPU 同时分担；某个素材显卡编码失败自动改用 CPU 重做。
- **设备检测**：每台电脑第一次打开时按 CPU 线程数和内存建议「并行进程」；内存不够时自动少开，不把电脑拖卡。
- **规格统一记录**：素材目录下方直接显示哪些视频已统一、哪些没有；统一好的视频自带标记，拷到别的电脑不用重做；只处理新增或改动过的素材。
- **稳**：随时可停止且不留残余；某个视频卡住或时间戳损坏时自动换兼容模式重做，不拖累其他视频；磁盘空间预检；设置文件先写临时文件再替换；运行日志落盘。

## 下载

到 [Releases](https://github.com/lxk7-ui/Axe/releases) 下载 `Axe-v2.6.0-win64.zip`（约 152MB），完整解压后双击 `Axe` 文件夹里的 `Axe.exe`。

- **系统要求**：64 位 Windows 10 / 11。用到的 .NET Framework 是系统自带的，不需要另装任何东西。
- 压缩包里已带 FFmpeg 和语音识别运行库；「识别语音配字幕」的模型（约 228MB）第一次勾选时联网下载一次。
- 请整体解压 / 拷贝，不要只复制 `Axe.exe`。缓存、历史、日志都生成在 Axe 文件夹里，不污染系统其他位置。

> [!NOTE]
> 程序没有代码签名，第一次运行时 Windows 可能提示“已保护你的电脑”，点 **更多信息 → 仍要运行**。安全软件提示时请允许 `ffmpeg.exe` 运行。

## 快速上手

1. 「素材目录」选一个产品文件夹（里面是 `.mp4` 素材），或选包含多个产品文件夹的大文件夹，再用「素材分类」指定其中一个；
2. 「输出目录」选成片保存的位置，软件会在其下自动按「产品名\11月9日（批次）」建文件夹；
3. 填成片数量、目标时长和浮动秒数（例如 180 ± 40 秒）；
4. 需要的话在右侧「成片效果」里选滤镜、贴纸、变速，勾选「识别语音配字幕」；
5. 点 **开始快拼**。第一次会先自动做规格统一（会替换素材文件夹里的原视频，原素材另有用处请先备份），之后再出片就是秒级。

完整说明见 [使用说明](package/使用说明.txt)（随发布包一起分发的那一份）。

## 为什么要先统一规格

不同分辨率、帧率的素材如果完全不处理直接拼接，成片会出现花屏、无声、时间戳错乱。
Axe 把「转码」这件慢的事只对每个素材做一次，结果直接替换原素材并带上标记；
之后无论出多少批成片，都只是把统一好的文件按不同顺序无损拼起来，所以快。
加了滤镜、贴纸、变速或字幕时，每条成片在拼接时多编码一次，所有效果在这一次里一起完成。

## 文档

| 文档 | 内容 |
|---|---|
| [使用说明](package/使用说明.txt) | 安装与升级、界面各项设置、去重规则、素材管理、文件位置、常见问题 |
| [更新记录](CHANGELOG.md) | v1.9.0 至今每个版本的时间和更新内容 |
| [成片效果素材的来源与许可](package/effects/版权说明.txt) | 滤镜、贴纸各自的出处和协议 |

## 从源码构建

需要 Windows 10/11 x64、[.NET SDK](https://dotnet.microsoft.com/download)（用它自带的 Roslyn 编译器，任意较新版本）和 .NET Framework 4.7.1 引用程序集（装了 Visual Studio 或 .NET Framework 4.7.1 Developer Pack 就有）。不需要 NuGet 包，也不需要打开 Visual Studio。

```powershell
git clone https://github.com/lxk7-ui/Axe.git
cd Axe
pwsh build.ps1          # 生成 bin\Axe.exe 和 bin\asr\AxeAsr.exe
```

`bin\Axe.exe` 要和 `ffmpeg\bin\ffmpeg.exe`、`ffmpeg\bin\ffprobe.exe`、`effects\`、`asr\` 放在一起才能运行。最省事的做法是从 Releases 下载一份发布包解压，用新编译的 exe 覆盖里面的同名文件。

打包成发布用的压缩包：

```powershell
pwsh package.ps1 -Version v2.6.0 -BaseDir <解压后的上一版 Axe 文件夹>
```

`package.ps1` 从 `-BaseDir` 取 FFmpeg 和桌面快捷方式脚本，从 `vendor\asr\` 取语音识别运行库（不在仓库里，获取方法见 [vendor/asr/README.md](vendor/asr/README.md)），在源码文件夹旁边生成 `Axe_<版本>\` 和 `Axe_<版本>_精简版.zip`。

## 项目结构

```
src/            程序源码（C#，WPF 界面全部用代码搭建）
  MainWindow*.cs      主窗口：设置、任务调度、日志
  MixEngine*.cs       引擎：规格统一（Unify）、快拼（Mix）、语音识别调度（Asr）
  Effects*.cs / Lut.cs / PreviewRenderer.cs   成片效果、调色文件处理、实时预览
  FolderPicker.cs     可搜索的文件夹选择窗口
  HwEncoder.cs / MediaProbe.cs / DeviceCheck.cs   显卡编码探测、视频信息读取、设备检测
asr/            AxeAsr：识别语音配字幕的辅助程序（64 位控制台程序，调用 sherpa-onnx）
package/        随发布包分发的文件：使用说明、成片效果素材（effects：滤镜、贴纸）
vendor/asr/     语音识别运行库的许可文件和获取说明（运行库本身不入库）
build.ps1       编译脚本
package.ps1     打包脚本
```

## 第三方组件

| 组件 | 协议 | 用途 |
|---|---|---|
| [FFmpeg](https://ffmpeg.org)（[BtbN](https://github.com/BtbN/FFmpeg-Builds) win64 GPL 构建，N-128212-g17a4a474f4） | GPL v3 | 转码、拼接、成片效果。以独立程序随发布包分发，协议见发布包 `ffmpeg\LICENSE.txt` |
| [sherpa-onnx](https://github.com/k2-fsa/sherpa-onnx) | Apache-2.0 | 语音识别运行库 |
| [ONNX Runtime](https://github.com/microsoft/onnxruntime) 1.28.2 | MIT | 模型推理 |
| [SenseVoice](https://github.com/FunAudioLLM/SenseVoice)（sherpa-onnx 转换的 int8 模型） | 见 [LICENSE-SenseVoice.txt](vendor/asr/LICENSE-SenseVoice.txt) | 语音识别模型，首次使用时下载 |
| [Silero VAD](https://github.com/snakers4/silero-vad) | MIT | 人声检测 |
| [Derby film looks](https://github.com/joinfiber/derby-film)、[Lightframes](https://github.com/joancomadran/lightframes) | MIT | 滤镜 01～07 |
| [slay LUTs](https://github.com/slaycamera/luts)、[GlassMeter film looks](https://github.com/TrondW/glassmeter-film-looks) | CC BY 4.0 | 滤镜 08～12 |
| [Fluent Emoji](https://github.com/microsoft/fluentui-emoji) / [Fluent Emoji Animated](https://github.com/microsoft/fluentui-emoji-animated) | MIT | 动态图标贴纸 |
| [得意黑 Smiley Sans](https://github.com/atelier-anchor/smiley-sans) | SIL OFL 1.1 | 促销文字贴纸的字体 |

促销文字贴纸（限时特惠、爆款推荐等）为本项目绘制。各素材的具体对应关系见 [版权说明](package/effects/版权说明.txt)。

## 使用规范

本工具用于对**自有或已获授权**的素材进行规格统一和批量组合。请只处理你有权使用的素材，尊重著作权与各平台规则。「成片去重」指的是本工具生成的成片之间素材组合不重复，不承诺、也不应被用于规避平台的重复内容检测。

## 许可

随附的第三方组件（FFmpeg、sherpa-onnx、ONNX Runtime、滤镜、贴纸、字体等）遵循各自的协议，见上表。
