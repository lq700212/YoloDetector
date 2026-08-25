---
name: "收集native运行库"
description: "重新收集检测模块的 native 运行库（OpenCvSharpExtern/onnxruntime/libSkiaSharp 等）到 Detection/libs/native/。注意：native 已入 git，克隆即完整，日常无需执行；仅在更换 OpenCvSharp/OnnxRuntime/SkiaSharp 依赖版本后（需同步改脚本内版本号）、或 native 文件缺失/损坏时使用。脚本为主：人工运行 powershell 脚本即可完成，AI 只需代跑并验证产物。"
---

# 收集 native 运行库（collect-native）

## 背景

检测类库的托管依赖（OpenCvSharp.dll 等）与 native 运行库**均已 vendor 入 git**（`Detection/libs/` + `Detection/libs/native/`，Windows 与 Linux 双平台共约 201MB，最大单文件 72MB < GitHub 100MB 限制），克隆仓库即为完整可编译、可运行状态（Windows 与 Linux 双平台开箱可用），**日常无需执行本技能**。本脚本仅在更换依赖版本时用于从 NuGet 全局缓存重新收集新版 native。

## 何时需要执行

| 场景 | 是否需要 |
| --- | --- |
| 全新克隆仓库后 | ❌ 不需要（native 已随 git 分发，双平台齐全） |
| 更换了 OpenCvSharp/OnnxRuntime/SkiaSharp 依赖版本 | ✅ 必须（同步修改脚本内的版本号） |
| native 文件被误删/损坏 | ✅ 使用 |
| 日常开发 | ❌ 不需要 |

## 执行

```powershell
powershell -ExecutionPolicy Bypass -File tools\collect-native.ps1
```

脚本逻辑：从 `%USERPROFILE%\.nuget\packages\` 复制 win-x64 native（OpenCvSharpExtern.dll、opencv_videoio_ffmpeg4100_64.dll、onnxruntime.dll、onnxruntime_providers_shared.dll、libSkiaSharp.dll）到 `Detection\libs\native\`；libSkiaSharp.so（Linux 部署用）在 NuGet 缓存有 `skiasharp.nativeassets.linux` 包时一并收集，缺失则打印手动获取提示（从 nuget.org 下载 SkiaSharp.NativeAssets.Linux 的 nupkg，改扩展名 .zip 解压取 `runtimes/linux-x64/native/libSkiaSharp.so`）。

## 验证

1. 脚本输出清单应含 5 个文件（4 个 .dll + 1 个 .so），无 [缺失] 黄字
2. `dotnet build YoloDetector.csproj` 后检查 `bin\Debug\net472\` 下存在上述 native 文件（csproj 的 None+Link 规则自动平铺复制）
3. 真实加载验证：新建 `OpenCvSharp.Mat` 不抛 TypeInitializationException 即为成功

## 常见问题

- **脚本报 [缺失] NuGet 缓存路径不存在**：本机从未还原过对应包——先在有网环境 `dotnet build` 一次让 NuGet 拉包，再重跑本脚本
- **修改了依赖版本**：同步修改 `tools\collect-native.ps1` 里 `$items` 的 Version 与 `Detection/YoloDetector.Detection.csproj` 的 HintPath 所指 DLL
- **编辑脚本注意事项**：`collect-native.ps1` 含中文注释，必须保存为 **UTF-8 带 BOM**（PowerShell 5.1 对无 BOM 文件按 ANSI 解析会解析失败）
