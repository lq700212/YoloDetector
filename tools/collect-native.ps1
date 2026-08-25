# ============================================================
# collect-native.ps1 — 检测模块 native 运行库收集脚本
#
# 作用：从本机 NuGet 全局缓存复制 OpenCV/OnnxRuntime/SkiaSharp 的
#       native 库（Windows .dll + Linux .so）到 Detection\libs\native\。
#
# 什么时候需要跑：
#   - native 已随 git 分发，克隆即完整，日常无需执行
#   - 更换 OpenCvSharp/OnnxRuntime/SkiaSharp 依赖版本后必须执行
#     （同步修改下方 $winItems/$linuxItems 的版本号）
#
# 用法：powershell -ExecutionPolicy Bypass -File tools\collect-native.ps1
# ============================================================

$ErrorActionPreference = 'Stop'
$nugetCache = Join-Path $env:USERPROFILE '.nuget\packages'
$destDir = Join-Path $PSScriptRoot '..\Detection\libs\native'

# Windows native：包名 / 版本 / 包内文件（runtimes\win-x64\native\）
$winItems = @(
    @{ Package = 'opencvsharp4.runtime.win';     Version = '4.10.0.20240615'; Files = @('OpenCvSharpExtern.dll', 'opencv_videoio_ffmpeg4100_64.dll') },
    @{ Package = 'microsoft.ml.onnxruntime';     Version = '1.20.0';          Files = @('onnxruntime.dll', 'onnxruntime_providers_shared.dll') },
    @{ Package = 'skiasharp.nativeassets.win32'; Version = '2.88.9';          Files = @('libSkiaSharp.dll') }
)

# Linux native：部署到 Linux 时需要（OpenCV 用非官方 linux-x64 构建以匹配托管层 4.10 版本）
$linuxItems = @(
    @{ Package = 'opencvsharp4.unofficial.runtime.linux-x64'; Version = '4.10.0.20241108'; File = 'libOpenCvSharpExtern.so' },
    @{ Package = 'microsoft.ml.onnxruntime';                  Version = '1.20.0';          File = 'libonnxruntime.so' },
    @{ Package = 'microsoft.ml.onnxruntime';                  Version = '1.20.0';          File = 'libonnxruntime_providers_shared.so' },
    @{ Package = 'skiasharp.nativeassets.linux';              Version = '2.88.9';          File = 'libSkiaSharp.so' }
)

New-Item -ItemType Directory -Path $destDir -Force | Out-Null

Write-Host '--- Windows (win-x64) ---'
foreach ($item in $winItems) {
    $nativeRoot = Join-Path $nugetCache "$($item.Package)\$($item.Version)\runtimes\win-x64\native"
    if (-not (Test-Path $nativeRoot)) {
        Write-Host "[缺失] $nativeRoot 不存在 —— 请先在有网机器 dotnet build 一次让 NuGet 还原" -ForegroundColor Yellow
        continue
    }
    foreach ($file in $item.Files) {
        $src = Join-Path $nativeRoot $file
        if (Test-Path $src) {
            Copy-Item $src $destDir -Force
            Write-Host "[OK] $file"
        }
        else {
            Write-Host "[跳过] $file 在包中不存在（部分包按需携带，属正常）" -ForegroundColor DarkGray
        }
    }
}

Write-Host '--- Linux (linux-x64) ---'
foreach ($item in $linuxItems) {
    $src = Join-Path $nugetCache "$($item.Package)\$($item.Version)\runtimes\linux-x64\native\$($item.File)"
    if (Test-Path $src) {
        Copy-Item $src $destDir -Force
        Write-Host "[OK] $($item.File)"
    }
    else {
        Write-Host "[缺失] $($item.Package) $($item.Version) 不在 NuGet 缓存 —— 有网机器执行 dotnet restore -r linux-x64 或手动下载 nupkg 解压取 $((Get-Item $src -ErrorAction SilentlyContinue).Name)" -ForegroundColor Yellow
    }
}

Write-Host ''
Write-Host '收集完成。native 清单：'
Get-ChildItem $destDir | ForEach-Object { '  {0}  ({1} MB)' -f $_.Name, [int]($_.Length / 1MB) }
