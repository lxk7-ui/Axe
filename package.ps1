param(
    [string]$Version = "v2.8.3",
    # Folder of the previous release; ffmpeg, settings.json and the shortcut script are taken from it.
    [string]$BaseDir = (Join-Path (Split-Path $PSScriptRoot) "_axe_original\Axe"),
    [switch]$NoZip,
    # Package the existing bin\Axe.exe as it is (for effect-only updates, or when build.ps1 cannot run).
    [switch]$NoBuild
)
# Assembles the portable "Axe" folder and zips it as Axe_<version>_精简版.zip next to this source folder.
# The speech model for 识别语音配字幕 (about 228 MB) is not included: Axe downloads it on first use
# into asr\model; everything else it needs (AxeAsr, sherpa-onnx, the voice detector) is.
$ErrorActionPreference = "Stop"
$outRoot = Split-Path $PSScriptRoot
if ($NoBuild) {
    if (-not (Test-Path (Join-Path $PSScriptRoot "bin\Axe.exe")) -or -not (Test-Path (Join-Path $PSScriptRoot "bin\asr\AxeAsr.exe"))) { throw "-NoBuild needs bin\Axe.exe and bin\asr\AxeAsr.exe" }
}
else {
    & (Join-Path $PSScriptRoot "build.ps1") | Out-Null
}

$stage = Join-Path $outRoot ("Axe_" + $Version)
$app = Join-Path $stage "Axe"
if (Test-Path $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
New-Item -ItemType Directory -Force (Join-Path $app "ffmpeg\bin"), (Join-Path $app "asr\model"), (Join-Path $app "asr\licenses") | Out-Null

Copy-Item (Join-Path $PSScriptRoot "bin\Axe.exe") $app
Copy-Item (Join-Path $PSScriptRoot "Axe.ico") $app
Copy-Item (Join-Path $BaseDir "创建桌面快捷方式.vbs") $app
if (Test-Path (Join-Path $BaseDir "settings.json")) { Copy-Item (Join-Path $BaseDir "settings.json") $app }
Copy-Item (Join-Path $BaseDir "ffmpeg\LICENSE.txt") (Join-Path $app "ffmpeg")
# Only ffmpeg and ffprobe are used; ffplay.exe (170 MB) and the HTML docs are left out.
Copy-Item (Join-Path $BaseDir "ffmpeg\bin\ffmpeg.exe") (Join-Path $app "ffmpeg\bin")
Copy-Item (Join-Path $BaseDir "ffmpeg\bin\ffprobe.exe") (Join-Path $app "ffmpeg\bin")

# 识别语音配字幕: helper, sherpa-onnx runtime, voice detector and tokens (the model is downloaded).
$vendor = Join-Path $PSScriptRoot "vendor\asr"
Copy-Item (Join-Path $PSScriptRoot "bin\asr\AxeAsr.exe") (Join-Path $app "asr")
Copy-Item (Join-Path $vendor "sherpa-onnx-c-api.dll"), (Join-Path $vendor "onnxruntime.dll") (Join-Path $app "asr")
Copy-Item (Join-Path $vendor "model\tokens.txt"), (Join-Path $vendor "model\silero_vad.onnx") (Join-Path $app "asr\model")
Copy-Item (Join-Path $vendor "LICENSE-*.txt") (Join-Path $app "asr\licenses")

# 成片效果: filters, stickers and where they come from.
Copy-Item (Join-Path $PSScriptRoot "package\effects") $app -Recurse

# Our text files with Windows line ends (Notepad of older Windows 10, e.g. LTSC 2016, shows bare LF
# as one long line) and a BOM.
foreach ($pair in @(@("package\使用说明.txt", "使用说明.txt"), @("package\effects\版权说明.txt", "effects\版权说明.txt"))) {
    $text = [IO.File]::ReadAllText((Join-Path $PSScriptRoot $pair[0])) -replace "`r?`n", "`r`n"
    [IO.File]::WriteAllText((Join-Path $app $pair[1]), $text, (New-Object Text.UTF8Encoding($true)))
}

Get-ChildItem $app -Recurse -File | Group-Object { $_.DirectoryName.Substring($stage.Length + 1) } | ForEach-Object {
    "{0,-26} {1,4} files {2,8:N1} MB" -f $_.Name, $_.Count, (($_.Group | Measure-Object Length -Sum).Sum / 1MB)
} | Write-Host
if ($NoZip) { return }

$zip = Join-Path $outRoot ("Axe_{0}_精简版.zip" -f $Version)
if (Test-Path $zip) { Remove-Item -LiteralPath $zip -Force }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($stage, $zip, [System.IO.Compression.CompressionLevel]::Optimal, $false)
Get-Item $zip | Select-Object FullName, @{ n = "MB"; e = { [math]::Round($_.Length / 1MB, 1) } } | Format-List | Out-String | Write-Host
