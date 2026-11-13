# vendor/asr：语音识别运行库

「识别语音配字幕」用到的第三方文件。它们体积大、不是本项目的代码，所以不放进仓库，这里只留许可文件。
`package.ps1` 打包时会从这个文件夹取下面 4 个文件：

| 文件 | 大小 | 来自 | SHA-256 |
|---|---|---|---|
| `onnxruntime.dll` | 17.0 MB | [ONNX Runtime](https://github.com/microsoft/onnxruntime) 1.28.2（随 sherpa-onnx 的 Windows x64 预编译包） | `7f66f939a881baf4f46a2216496798edf4a1429878b646d12674aa62f27d8a25` |
| `sherpa-onnx-c-api.dll` | 3.5 MB | [sherpa-onnx](https://github.com/k2-fsa/sherpa-onnx) 的 Windows x64 预编译包 | `ee2af9f49b8fc5dae135ad20c0b955cbab90d17b79cc1ad0db91e03b24962f68` |
| `model\silero_vad.onnx` | 0.6 MB | [Silero VAD](https://github.com/snakers4/silero-vad)（人声检测） | `9e2449e1087496d8d4caba907f23e0bd3f78d91fa552479bb9c23ac09cbb1fd6` |
| `model\tokens.txt` | 0.3 MB | [sherpa-onnx-sense-voice-zh-en-ja-ko-yue-2024-07-17](https://huggingface.co/csukuangfj/sherpa-onnx-sense-voice-zh-en-ja-ko-yue-2024-07-17)（SenseVoice 模型的词表） | `f449eb28dc567533d7fa59be34e2abca8784f771850c78a47fb731a31429a1dc` |

## 怎么拿到

最省事：从 [Releases](https://github.com/lxk7-ui/Axe/releases) 下载任意一个 v2.3.0 及以后的发布包，解压后把 `Axe\asr\` 里的同名文件复制过来：

```
Axe\asr\onnxruntime.dll          ->  vendor\asr\onnxruntime.dll
Axe\asr\sherpa-onnx-c-api.dll    ->  vendor\asr\sherpa-onnx-c-api.dll
Axe\asr\model\silero_vad.onnx    ->  vendor\asr\model\silero_vad.onnx
Axe\asr\model\tokens.txt         ->  vendor\asr\model\tokens.txt
```

复制后可以用上表的 SHA-256 核对（`Get-FileHash 文件名 -Algorithm SHA256`）。

语音识别模型本身（`model.int8.onnx`，约 228MB）不随发布包分发，Axe 在第一次勾选「识别语音配字幕」时下载到 `asr\model\`，下载地址见 `src/SpeechModel.cs`。

## 许可

| 文件 | 协议 |
|---|---|
| [LICENSE-onnxruntime.txt](LICENSE-onnxruntime.txt) | ONNX Runtime |
| [LICENSE-sherpa-onnx.txt](LICENSE-sherpa-onnx.txt) | sherpa-onnx |
| [LICENSE-silero-vad.txt](LICENSE-silero-vad.txt) | Silero VAD |
| [LICENSE-SenseVoice.txt](LICENSE-SenseVoice.txt) | SenseVoice 模型 |

这 4 个许可文件会随发布包一起分发（`Axe\asr\licenses\`）。
