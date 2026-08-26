# YoloDetector 架构与技术要点

> 面向 AI/新维护者的快速入手文档。读完本文 + 根目录 `AGENTS.md`（约定与红线）即可安全开工。
> 配套阅读顺序建议：`AGENTS.md` → 本文 → 具体源码。

## 1. 系统概览

RTSP 网络摄像头接入 → OpenCV 逐帧捕获 → YOLO(ONNX) 推理检测人员 → 检测框叠加绘制 → WinForms 预览窗口显示。

```
┌─────────────────────────── UI 层 (YoloDetector.UI) ───────────────────────────┐
│  MainForm：按钮事件转发 / 日志面板 / SafeBeginInvoke 显示 Bitmap               │
│  只接触 Bitmap 与结果快照，禁止接触 OpenCV Mat                                 │
└───────────────▲───────────────────────────────────────────────▲───────────────┘
                │ previewSink(Bitmap)                            │ detectionSink(快照)
┌───────────────┴──────────────── 编排层 (YoloDetector.App) ─────┴───────────────┐
│  VideoDetectionController：帧流转与 Mat 所有权终结点、模型跨会话复用            │
│  CameraController：连接状态机、防重入状态轮询、拉/推流操作                      │
└───────────────┬───────────────────────────────────────────────┬───────────────┘
                │                                               │
┌───────────────▼────────── 检测域 (YoloDetector.Detection) ─────▼───────────────┐
│  RtspFrameCapturer(IFrameSource) ──FrameReady──▶ YoloDetectionService          │
│                                                  (IDetectionPipeline)          │
│  检测线程内: YoloV26Detector 推理 → ResultProcessor 后处理 → Visualizer 绘制    │
│  事件出口: DetectionsUpdated(快照) / FrameProcessed(Mat,归订阅者)               │
└─────────────────────────────────────────────────────────────────────────────────┘
┌────────────── 相机域 (Cameras) ─────────────┐ ┌── 配置 (Configuration) ────────┐
│ ICameraApi + AngehuaCameraApiClient         │ │ AppConfig 加载 appsettings /   │
│ + CameraApiFactory(品牌注册)                │ │ cameraConfigs / yoloConfig     │
└─────────────────────────────────────────────┘ └────────────────────────────────┘
```

依赖方向：`UI → App → Detection/Cameras → Infrastructure/Configuration`，严禁反向。

**程序集边界（模块化）**：`Detection/` 编译为独立类库 `YoloDetector.Detection.dll`（命名空间 `YoloDetection`，工程 `Detection/YoloDetector.Detection.csproj`），主程序经项目引用使用；该程序集**禁止引用宿主任何业务命名空间**，日志经 `LogManager.Initialize` 的委托注入、配置经方法参数注入——整个目录复制到其他解决方案即可迁移（接入指南：`docs/MODULE.md`）。主 csproj 中已 `<Compile Remove="Detection\**\*.cs">` 防止重复编译。

**多目标与离线依赖**：类库双目标编译——`net472` 与 `netstandard2.0` **能力完全一致**（无条件编译差异）：位图后端统一 SkiaSharp（SKBitmap/Bgra8888 与 OpenCV BGRA 布局对齐，SIMD CvtColor + Buffer.MemoryCopy 整块拷贝），可视化与互转 API 全平台同源同效果；宿主显示层在 `App/SkBitmapExtensions.cs` 做一次 SKBitmap→Drawing.Bitmap 边界转换（类库内禁止 System.Drawing）。托管依赖与 native 运行库均已 vendor 入 git（`Detection/libs/` + `Detection/libs/native/`，Windows 与 Linux 双平台共约 201MB，最大单文件 libOpenCvSharpExtern.so 72MB < GitHub 100MB 限制），克隆即完整、编译运行全离线；`tools/collect-native.ps1` 仅在更换依赖版本后重新收集时使用。native 经类库 csproj 的 None+Link 规则平铺复制到输出目录（运行时 DllImport 按 exe 目录解析，.dll/.so 共存互不冲突）。

## 2. 线程模型（稳定性核心）

三个线程协作，全部生命周期由控制器管理：

| 线程 | 所属 | 职责 |
| --- | --- | --- |
| UI 线程 | MainForm | 界面、定时器轮询（5s）、显示帧 |
| RTSP 捕获线程 | RtspFrameCapturer.CaptureLoop | 私有 VideoCapture 上 `Read` → BGR 统一转换 → 克隆后经 FrameReady 发布；每轮刷新心跳，连续读失败按原地址重建流 |
| RTSP 看门狗 (Timer) | RtspFrameCapturer.WatchdogTick | 心跳停滞超 15s 判定线程卡死（静默半开连接）→ 废弃当前代际、全新实例+线程顶上 |
| YOLO 检测线程 | YoloDetectionService.DetectionLoop | 取帧 → 推理 → 后处理 → 快照事件 → 可视化 → 帧事件 |

关键机制：

- **单槽位缓冲**：`ProcessFrame` 在锁内克隆新帧并覆盖旧帧（旧帧立即 Dispose），检测慢时自动丢帧，不积压内存。
- **Monitor.Wait/Pulse 信号协议**：检测线程无帧时挂起（零 CPU）；禁用 AutoResetEvent/SemaphoreSlim——WaitHandle 在线程未退出时 Dispose 会产生 ObjectDisposedException 竞态（v1 实际崩溃过）。
- **停止协议**：`volatile/锁内置位 → Monitor.PulseAll → 有界 Join(3~10s)`。Join 超时绝不销毁线程依赖的资源，让其自行退出；下次 Start 会先等旧线程退出，保证任何时刻最多一个检测线程。
- **后台异常零逃逸**：两个工作循环整体 try/catch，记录日志继续运行。
- **断流自愈**：捕获线程私有 VideoCapture（外部永不 Release，防"卡在 native Read 时被外部释放"崩溃）；普通断流走"连续失败→Reopen"，静默半开（Read 永久挂起，本机 FFmpeg 构建无超时可依）由看门狗代际更替兜底，僵尸线程苏醒后自杀并释放自己的实例。

## 3. Mat 所有权链路（内存不泄漏的关键）

每一帧 Mat 从产生到消亡的所有权转移路径，全链路无泄漏：

```
CaptureLoop: frame(栈上,finally释放) ─clone→ bgrFrame(可能=frame,判重释放)
   └─clone→ copy ══FrameReady事件══▶ VideoDetectionController.OnFrameReady
                                        ├─ ProcessFrame(copy): 管道内部 clone 入缓冲
                                        └─ finally copy.Dispose()   ← 所有权终结点①
检测线程: _pendingFrame ─取走─▶ frame
   └─visualizer.Draw(frame)→ outputFrame ══FrameProcessed事件══▶ OnFrameProcessed
        └─ MatToSKBitmap(outputFrame) → ToDrawingBitmap() → bitmap
             outputFrame.Dispose()                        ← 所有权终结点②
             └─ SafeBeginInvoke(bitmap) ─▶ PictureBox.Image 替换时 Dispose 旧图 ← 终结点③
```

规则：**事件传出的资源归订阅者所有**；调度到 UI 失败时随行资源就地 Dispose。

## 4. YOLO 推理实现细节（YoloV26Detector）

### 4.1 预处理（letterbox）

BGR Mat → 保持宽高比缩放到模型输入尺寸（默认 640x640）→ 居中放置、黑色填充：

```csharp
scale = min(inputW/w, inputH/h);  scaledW = w*scale;  scaledH = h*scale;
padX = (inputW - scaledW)/2;      padY = (inputH - scaledH)/2;
// 坐标还原因子（注意 X/Y 分开算，不能用统一 1/scale）：
scaleX = w / scaledW;             scaleY = h / scaledH;
```

性能关键：整块 `Marshal.Copy(Data, rawBytes)` 拷出像素后单层循环填 CHW+归一化+BGR→RGB；
**禁止逐像素 `Mat.Get<Vec3b>`**（百万次 P/Invoke，30~80ms/帧 → 现在 1~3ms）。

### 4.2 输出解析（双格式自适应）

模型输出张量按形状自适应解析（`[1,N,D]` 或 `[1,D,N]` 转置均支持）：

- **NMS 输出格式**（vecLen≤7）：`[x1,y1,x2,y2,conf,cls]` 角点坐标，已在模型输入空间
- **原始预测格式**：`[cx,cy,w,h,obj_conf,cls_0..cls_n]`，坐标可能为归一化值（全部 <2 时 ×640 还原）

### 4.3 坐标还原公式（行为红线，勿改）

```csharp
// 模型输入空间(带padding的640×640) → 原图空间
x' = (x_model - padX) * scaleX;    y' = (y_model - padY) * scaleY;    // 角点/中心点
w' = w_model * scaleX;             h' = h_model * scaleY;             // 仅中心点格式
```

之后按序过滤：`TargetClassIds`(默认 person=0) → 置信度阈值 → 裁剪到画面边界（完全出界才丢弃）→ NMS(IoU≥阈值抑制) → 按置信度取前 5。
边界裁剪与最小尺寸过滤(10×20)在 `DefaultResultProcessor` 中完成——**这些数值是现场调好的，改动需用户确认**。

## 5. 配置体系

| 文件 | 内容 | 对应模型类 |
| --- | --- | --- |
| `appsettings.json` | 仅 `ActiveCameraConfig` 激活品牌名 | CameraConfig |
| `cameraConfigs/{品牌}.json` | 连接参数/API路径/流地址模板/刷新间隔 | CameraConfig 及嵌套子类 |
| `Detection/yoloConfig.json` | 模型路径/阈值/可视化方案/日志开关 | YoloConfig |
| `Detection/esdConfig.json` | 静电触摸检测：Enabled/姿态模型路径/ROI 归一化标定/Hold/Grace/Margin | EsdConfig → EsdAnalysisOptions |

加载容错：文件缺失或损坏一律回退代码默认值。业务模块不直接读 AppConfig（由调用方注入参数值），保持 Detection 域零外部依赖。

换 YOLO 模型：onnx 放 `Detection/model/`，改 yoloConfig.json 的 `ModelPath` 即可。模型获取/pt转onnx 见 `docs/ONNX模型获取指南.md`。
静电触摸检测的姿态模型（yolo11n-pose.onnx）由 `tools/download_pose_model.py` 一键下载导出（官方 .pt 正源 + ultralytics 官方 API 导出）。

## 6. 已知限制（后续可改进项）

- **静默半开断流的强杀重建会遗留僵尸线程**：普通断流约 1.5 秒自动重连；真·静默半开（Read 永久挂起）由看门狗在约 15~20 秒强制重建，被废弃的线程与其 VideoCapture 在其苏醒前无法安全释放（native 无法打断），作为已知泄漏留给进程退出回收。
- **启动预览撞上无响应地址可能阻塞较久**：Start 的 Open 同步执行（保持"失败立即返回 false"契约），FFmpeg 内部握手/重试无超时；如需优化可改异步打开 + UI "连接中"状态。
- **每帧 BeginInvoke 显示无节流**：25fps 下 UI 来得及消费；若将来卡顿可在控制器加 in-flight 计数丢帧策略。
- HIK/DAHUA 品牌客户端未实现（工厂回退到 Angehua 实现），扩展方式见 `Cameras/ICameraApi.cs` 注释。

## 7. 常用调试入口

- 日志分级开关：`Detection/yoloConfig.json` 的 `YoloDebugLog`（推理过程）/`DetectionResultLog`（每帧结果），输出到 `logs/log_yyyy-MM-dd.txt`，调试完关回 false。
- 检测算法对照验证：本地图片喂 `YoloV26Detector.Detect(Mat)`（模块 API 见 docs/MODULE.md）。
- 版本历史见根目录 `CHANGELOG.md`。
