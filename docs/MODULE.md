# YOLO 检测模块接入指南

> 目标读者：需要把本项目的 YOLO 检测能力接入到自己项目中的开发人员。
> 模块以独立类库 `YoloDetector.Detection.dll` 交付，**不依赖本项目的任何业务代码**（UI/相机/配置均通过接口与委托注入），复制即用。

## 1. 模块包含什么

```
Detection/                        ← 整个目录复制走即可
├── YoloDetector.Detection.csproj 类库工程文件（net472;netstandard2.0 双目标 / C# 7.3）
├── YoloV26Detector.cs            YOLO 推理实现（letterbox 预处理/双格式解析/NMS）
├── YoloDetectionService.cs       检测管道（后台检测线程/单槽位缓冲/Monitor 停止协议）
├── RtspFrameCapturer.cs          RTSP 帧源（可选，自带视频源时不需要）
├── Visualizers.cs                可视化器工厂 + OpenCV 绿框可视化器
├── YoloBuiltinVisualizer.cs      Skia 红框可视化器（跨平台）
├── MatExtensions.cs              Mat↔SKBitmap 高性能互转（全平台无损）
├── DetectionResult.cs / YoloClasses.cs   结果模型 / COCO 类别表
├── LogManager.cs                 日志门面（输出通道由宿主注入）
├── libs/                         离线托管依赖（已入 git）+ libs/native/ 运行库（collect 脚本收集）
└── I*.cs                         5 个抽象接口（见下表）
```

## 2. 环境要求（离线友好）

| 项 | 要求 |
| --- | --- |
| 框架 | 接入方任选：.NET Framework 4.7.2+ / .NET Core 2.0+ / .NET 5/6/7/8+ / Mono（类库多目标编译自动匹配） |
| 平台 | 任意 CPU 架构由 native 库决定；Windows x64 已验证，Linux/macOS 需对应平台 OpenCV/OnnxRuntime native 库 |
| NuGet | **无需**——托管依赖已 vendor 进 `Detection/libs/`（OpenCvSharp.dll、Microsoft.ML.OnnxRuntime.dll、SkiaSharp.dll 及 Span 支撑库），编译完全离线 |
| native 运行库 | `Detection/libs/native/`（约 113MB）**已入 git**——克隆即完整，无需任何收集步骤 |
| 模型 | ONNX 格式 YOLO 模型文件（路径由调用方指定，模块不关心放哪里） |

**平台能力矩阵（全平台能力一致，使用不变、效果不变）**：

| 能力 | net472（Windows） | netstandard2.0（Windows/Linux/macOS） |
| --- | --- | --- |
| YOLO 推理 / 检测管道 / RTSP 帧源 | ✅ | ✅ |
| YoloBuiltin 可视化器（红框+标签） | ✅ | ✅ 同一套 Skia 实现，渲染效果一致 |
| OpenCV 可视化器（绿框） | ✅ | ✅ |
| MatToSKBitmap / SKBitmapToMat | ✅ | ✅ 无损互转（往返像素差 = 0） |

- 位图类型统一为 **SkiaSharp 的 SKBitmap**（Google Skia 跨平台封装）：System.Drawing(GDI+) 仅 Windows 存在，故模块全平台统一走 Skia，API 与渲染效果不再有平台差异
- 性能：Bgra8888 与 OpenCV BGRA 布局一致，SIMD CvtColor + 整块内存拷贝，1080P 互转约 5ms/帧（25fps 场景充裕）
- Windows WinForms 宿主显示：在宿主边界做一次 SKBitmap→System.Drawing.Bitmap 转换（参考本项目 `App/SkBitmapExtensions.cs`，约 0.5ms）

> Linux 部署：需将 `libs/native/` 中 OpenCV/OnnxRuntime 的 native 替换为目标平台版本（OpenCvSharp4.runtime.linux 包、onnxruntime 的 libonnxruntime.so）；libSkiaSharp.so 已随收集脚本提供。模块托管代码零改动。

## 3. 最小接入示例（约 20 行）

```csharp
using System;
using System.Collections.Generic;
using YoloDetection;

class Demo
{
    static void Main()
    {
        // 1. 日志接管（可选；不调用则输出到 Debug 窗口）
        LogManager.Initialize(
            enableYoloLog: false, enableGeneralLog: true, enableDetectionResultLog: false,
            outputSink: msg => Console.WriteLine(msg),   // 落地通道：文件/控制台/任意
            uiSink: null);                               // UI 通道：可为 null

        // 2. 创建检测器并加载模型
        var detector = new YoloV26Detector();
        detector.Initialize(@"C:\models\yolo26n.onnx");
        detector.TargetClassIds.Add(0);          // 检测类别（COCO id），默认仅 person=0
        detector.ConfidenceThreshold = 0.35f;    // 置信度阈值
        detector.NmsThreshold = 0.5f;            // NMS 阈值

        // 3. 创建检测管道（内部自带检测线程，帧进 → 结果出）
        using (var pipeline = new YoloDetectionService(detector))
        {
            // 结果快照（不可变列表，可自由持有）
            pipeline.DetectionsUpdated += (s, dets) =>
            {
                foreach (var d in dets)
                    Console.WriteLine($"{d.ClassName} {d.Confidence:F2} @({d.X:F0},{d.Y:F0}) {d.Width:F0}x{d.Height:F0}");
            };
            // 绘制后的帧（Mat 归订阅者所有，用完必须 Dispose！）
            pipeline.FrameProcessed += (s, mat) => { /* 显示/编码后 mat.Dispose() */ };

            pipeline.Start();

            // 4. 喂帧（任意线程、任意帧率；内部克隆，frame 可立即释放）
            //    frame 来源随意：RTSP/本地相机/图片序列/别的 SDK，只要是 OpenCvSharp Mat(BGR)
            while (HasNextFrame())
            {
                using (var frame = GrabFrame())
                {
                    pipeline.ProcessFrame(frame);
                }
            }

            pipeline.Stop();   // 有界等待检测线程退出，安全
        }
        detector.Dispose();
    }
}
```

带 RTSP 流的完整场景参考本项目 `App/VideoDetectionController.cs`（帧所有权全链路管理范例）。

## 4. 接口一览（扩展点）

| 接口 | 职责 | 默认实现 | 换实现的场景 |
| --- | --- | --- | --- |
| `IYoloDetector` | 预处理→推理→坐标还原+候选过滤 | `YoloV26Detector` | 接入其他模型结构（如 YOLOv8/v11 差异解析） |
| `IDetectionPipeline` | 帧缓冲/检测线程/事件发布 | `YoloDetectionService` | 自定义调度策略（如双缓冲） |
| `IFrameSource` | 视频帧采集 | `RtspFrameCapturer` | USB 相机/文件流/厂商 SDK 取流 |
| `IDetectionVisualizer` | 结果绘制 | `YoloBuiltinVisualizer`/`OpenCVVisualizer` | 自定义绘制样式 |
| `IDetectionResultProcessor` | 结果后处理（边界裁剪/尺寸过滤） | `DefaultResultProcessor` | 业务过滤规则（如区域入侵判定） |

## 5. 关键契约（务必遵守，否则崩溃/泄漏）

1. **Mat 所有权**：`ProcessFrame(frame)` 内部克隆，调用方随后可立即释放；`FrameProcessed` 传出的 Mat **归订阅者所有，用完必须 Dispose**；`DetectionsUpdated` 传出的列表是不可变快照，可自由持有。
2. **线程模型**：`ProcessFrame` 可任意线程调用；两个事件在模块内部检测线程触发，订阅者若更新 UI 必须自行调度（WinForms 用 `Control.BeginInvoke`）。
3. **检测器非线程安全**：同一 `IYoloDetector` 实例的 `Detect` 必须串行（管道内部已保证，直接调用时注意）。
4. **坐标语义**：`DetectionResult.X/Y` 是**原图像素坐标系**下的框中心点，Width/Height 为框宽高；`Left/Top/Right/Bottom` 为便捷计算属性。

## 6. 迁移步骤清单

1. 复制 `Detection/` 整个目录到目标解决方案（含 libs/ 与 libs/native/，不含 bin/obj）
2. 目标项目添加对 `YoloDetector.Detection.csproj` 的项目引用（或编译后直接引用 DLL）
3. 无需联网——托管依赖与 native 均已随 git 分发；仅在更换依赖版本时跑 `tools/collect-native.ps1` 重新收集
4. 按第 3 节示例写初始化与喂帧代码
5. 按需调用 `LogManager.Initialize` 接管日志
6. 模型文件随目标项目分发（模块只接收路径参数）

## 7. 离线部署（工厂/无网现场）

部署机**只需要编译输出目录整包拷贝**，与 NuGet/网络完全无关：

```
部署包 = bin\Debug\{目标框架}\ 整个目录
  ├─ 宿主.exe / 宿主.dll
  ├─ YoloDetector.Detection.dll        检测模块
  ├─ OpenCvSharp.dll                   托管依赖（已 vendor，随编译复制）
  ├─ Microsoft.ML.OnnxRuntime.dll
  ├─ SkiaSharp.dll                     跨平台绘制后端（托管）
  ├─ System.Memory.dll 等 Span 支撑库
  ├─ OpenCvSharpExtern.dll (59MB)      native 运行库（collect 脚本收集后随编译复制）
  ├─ libSkiaSharp.dll (9MB)            Skia native（Windows；Linux 为 libSkiaSharp.so）
  ├─ onnxruntime.dll (11MB)
  ├─ opencv_videoio_ffmpeg4100_64.dll  RTSP 解码依赖
  ├─ Detection\model\*.onnx            模型
  └─ Detection\yoloConfig.json 等配置
```

验证部署包完整性：目标机器上直接运行，日志出现"程序启动"且开始预览不报 `DllNotFoundException` / `TypeInitializationException` 即为完整。
