# ============================================================
# collect-native.ps1 — 检测模块 native 运行库收集脚本
#
# 作用：从本机 NuGet 全局缓存复制 OpenCV/OnnxRuntime 的 native DLL
#       到 Detection\libs\native\，使项目获得离线运行能力。
#
# 什么时候需要跑：
#   - 全新克隆仓库后（native 不入 git，仅托管依赖入库）
#   - 更换 OpenCvSharp/OnnxRuntime 版本后
#   - 只需在有网的开发机上跑一次；之后整个项目目录（含已收集的
#     libs\native）可整包拷贝到无网环境离线编译与部署
#
# 用法：powershell -ExecutionPolicy Bypass -File tools\collect-native.ps1
# ============================================================

$ErrorActionPreference = 'Stop'
$nugetCache = Join-Path $env:USERPROFILE '.nuget\packages'
$destDir = Join-Path $PSScriptRoot '..\Detection\libs\native'

# 依赖清单：包名 / 版本 / 包内 native 路径（win-x64 与 linux-x64）
$items = @(
    @{ Package = 'opencvsharp4.runtime.win';      Version = '4.10.0.20240615'; Files = @('OpenCvSharpExtern.dll', 'opencv_videoio_ffmpeg4100_64.dll') },
    @{ Package = 'microsoft.ml.onnxruntime';      Version = '1.20.0';          Files = @('onnxruntime.dll', 'onnxruntime_providers_shared.dll') },
    @{ Package = 'skiasharp.nativeassets.win32';  Version = '2.88.9';          Files = @('libSkiaSharp.dll') }
)

New-Item -ItemType Directory -Path $destDir -Force | Out-Null

foreach ($item in $items) {
    $nativeRoot = Join-Path $nugetCache "$($item.Package)\$($item.Version)\runtimes\win-x64\native"
    if (-not (Test-Path $nativeRoot)) {
        Write-Host "[缺失] $nativeRoot 不存在 —— 请先在有网机器 dotnet build 一次让 NuGet 还原，或手动放置" -ForegroundColor Yellow
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

# Linux native（libSkiaSharp.so）：部署到 Linux 时需要；Windows-only 场景可缺
$linuxSo = Join-Path $nugetCache 'skiasharp.nativeassets.linux\2.88.9\runtimes\linux-x64\native\libSkiaSharp.so'
if (Test-Path $linuxSo) {
    Copy-Item $linuxSo $destDir -Force
    Write-Host "[OK] libSkiaSharp.so (linux-x64)"
}
else {
    Write-Host "[提示] libSkiaSharp.so 未收集（Linux 部署才需要）：从 NuGet 下载 SkiaSharp.NativeAssets.Linux 2.88.9 的 nupkg 解压取 runtimes/linux-x64/native/libSkiaSharp.so 放入 $destDir" -ForegroundColor DarkGray
}

Write-Host ''
Write-Host '收集完成。native 清单：'
Get-ChildItem $destDir | ForEach-Object { '  {0}  ({1} MB)' -f $_.Name, [int]($_.Length / 1MB) }
