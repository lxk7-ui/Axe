param(
    [string]$Out = (Join-Path $PSScriptRoot "bin\Axe.exe")
)
# Builds Axe.exe for .NET Framework 4.5+ (built into Windows 8/10/11) with the Roslyn compiler
# that ships with the .NET SDK. No NuGet packages or Visual Studio are needed. Also builds the
# speech recognition helper asr\AxeAsr.exe (64-bit console program, see asr\AxeAsr.cs).
$ErrorActionPreference = "Stop"
$sdk = Get-ChildItem "C:\Program Files\dotnet\sdk" -Directory | Sort-Object { [version]($_.Name -replace '[^0-9.].*$', '') } -Descending | Select-Object -First 1
$csc = Join-Path $sdk.FullName "Roslyn\bincore\csc.dll"
# Lowest complete reference set on this machine. The exe binds to the 4.0.0.0 framework assemblies;
# Roslyn emits Array.Empty<T> (added in 4.6), so the floor is .NET Framework 4.6, which every
# Windows 10/11 build includes. The bundled ffmpeg is 64-bit and needs Windows 10+ anyway.
$ref = "C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.7.1"
if (-not (Test-Path $csc)) { throw "Roslyn compiler not found: $csc" }
if (-not (Test-Path (Join-Path $ref "mscorlib.dll"))) { throw ".NET Framework reference assemblies not found: $ref" }
$refs = @("mscorlib.dll", "System.dll", "System.Core.dll", "System.Xaml.dll", "System.Web.Extensions.dll",
          "WindowsBase.dll", "PresentationCore.dll", "PresentationFramework.dll") | ForEach-Object { "/reference:" + (Join-Path $ref $_) }
New-Item -ItemType Directory -Force (Split-Path $Out) | Out-Null
$src = Get-ChildItem (Join-Path $PSScriptRoot "src") -Filter *.cs | ForEach-Object { $_.FullName }
$args = @("exec", $csc, "/nologo", "/noconfig", "/nostdlib+", "/target:winexe", "/platform:anycpu", "/optimize+",
          "/langversion:latest", "/utf8output", "/codepage:65001", "/warn:4",
          ("/win32icon:" + (Join-Path $PSScriptRoot "Axe.ico")), "/out:$Out") + $refs + $src
$log = & dotnet @args 2>&1
$log | Where-Object { $_ -match 'error|warning' } | ForEach-Object { Write-Host $_ }
if ($LASTEXITCODE -ne 0) { throw "Build failed ($LASTEXITCODE)" }

$asr = Join-Path (Split-Path $Out) "asr\AxeAsr.exe"
New-Item -ItemType Directory -Force (Split-Path $asr) | Out-Null
$asrRefs = @("mscorlib.dll", "System.dll", "System.Core.dll") | ForEach-Object { "/reference:" + (Join-Path $ref $_) }
$args = @("exec", $csc, "/nologo", "/noconfig", "/nostdlib+", "/target:exe", "/platform:x64", "/optimize+",
          "/langversion:latest", "/utf8output", "/codepage:65001", "/warn:4", "/out:$asr") + $asrRefs + @((Join-Path $PSScriptRoot "asr\AxeAsr.cs"))
$log = & dotnet @args 2>&1
$log | Where-Object { $_ -match 'error|warning' } | ForEach-Object { Write-Host $_ }
if ($LASTEXITCODE -ne 0) { throw "AxeAsr build failed ($LASTEXITCODE)" }
Get-Item $Out, $asr | Select-Object FullName, Length, LastWriteTime
