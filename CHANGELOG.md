# CHANGELOG — YoloDetector 版本改动记录

> 格式约定：最新版本在最前；每个小节写明「改动范围 / 为什么这么改 / 优化点」三部分。

## v2.0（2026-08-24）架构重构与稳定性治理

### 改动范围

**1. 目录重组为五层架构（namespace 同步调整）**

| 旧位置 | 新位置 |
| --- | --- |
| 根目录 `MainForm.cs` / `MainForm.resx` | `UI/MainForm.cs` + `UI/MainForm.Layout.cs`（拆分为纯视图 + 布局 partial） |
| 根目录 `ICameraApi.cs` / `ANGEHUACameraApiClient.cs` / `CameraApiFactory.cs` | `Cameras/` |
| 根目录 `AppConfig.cs` | `Configuration/`（拆分为 `AppConfig.cs` + `CameraConfig.cs` + `YoloConfig.cs`） |
| 根目录 `Logger.cs` | `Infrastructure/Logging/Logger.cs` |
| `YoloDetection/*.cs` | `Detection/` |
| （新增） | `App/VideoDetectionController.cs`、`App/CameraController.cs` |

**2. 删除死代码**：`ApiSignUtil.cs`、`YoloDetection/DetectionOverlayForm.cs`、`TransparentOverlay.cs`（LibVLC 方案遗留）、`packages.config`、NuGet 包 LibVLCSharp.WinForms 与 VideoLAN.LibVLC.Windows、`YoloV26Detector.Detect(byte[])/TestDetectImage()`、接口死抽象 `IFrameProcessor`/`ProcessingResult`、无效配置项 `DetectInterval`。

**3. Bug 修复（并发与资源管理）**

| # | 问题 | 修复 |
| --- | --- | --- |
| 1 | `YoloDetectionService.Dispose()` 与检测线程竞态：AutoResetEvent 在线程仍存活时被 Dispose → ObjectDisposedException | 整体重写为 **Monitor.Wait/Pulse 协议**（无句柄资源）；停止协议改为"volatile 置位 → PulseAll → 有界 Join"，超时不销毁线程依赖资源 |
| 2 | `DetectionLoop` 锁外裸读 `_lastDetections`，且把内部 List 引用直接传给事件订阅者 | 锁内发布不可变快照，事件一律传快照 |
| 3 | **每帧内存泄漏约 6MB**：`FrameReady` 事件的 Mat 走 `ProcessFrame` 分支后无人 Dispose | 明确帧所有权契约，`VideoDetectionController` 统一 try/finally 终结 |
| 4 | `RtspFrameCapturer.Start()` Open 失败路径不释放 VideoCapture；捕获循环异常路径 bgrFrame 泄漏；BGR8 时 double-dispose | 失败路径完整 Dispose；finally 兜底 + `ReferenceEquals` 判重 |
| 5 | `_isRunning` 停止标志非 volatile，跨线程可见性无保证 | volatile 或锁内状态枚举 |
| 6 | 后台帧回调 `BeginInvoke` 到正在关闭的窗体抛 ObjectDisposedException（退出偶发崩溃根源） | `SafeBeginInvoke` 统一防护，调度失败就地释放随行 Bitmap |
| 7 | 状态轮询 `async void` 无重入保护，网络慢时请求堆积 | `CameraController.TryGetStatusAsync` 内 Interlocked 防重入 |
| 8 | `TcpClient.ConnectAsync` 无超时，网络不可达时永久挂起 | 竞速+放弃模式，超时孤儿任务挂接异常观察延续 |
| 9 | `Logger.Close()` 后在途回调再写日志会重新打开文件句柄 | 增加 `_closed` 单向标志 |

**4. 架构与接口调整**

- `MainForm` 从 1416 行 God Class 拆分：主文件只留事件转发与结果显示，布局独立 partial；相机连接委托 `CameraController`，检测编排委托 `VideoDetectionController`——**UI 层从此零 Mat 接触**
- `ICameraApi` 接口去掉每个方法的冗余 ip 参数（构造注入统一），`DeviceStatus` 拆分为独立文件
- 双静态日志通道（LogManager + YoloV26Detector.DiagnosticLogger）合并为统一 `LogManager` 门面
- 检测器目标类别由硬编码 person 改为 `TargetClassIds` 可配置集合

### 为什么这么改

原版由早期 AI 辅助生成，功能可用但存在系统性隐患：分层混乱（UI 直接管理全部 OpenCV 资源）、多处并发竞态（WaitHandle 生命周期、非 volatile 标志、锁外共享读取）、多路径资源泄漏（每帧级内存泄漏、失败路径句柄泄漏），长期运行必然出现内存增长与退出崩溃。本次以"结构可维护 + 运行稳定"为目标做整体治理。

### 优化点

- ONNX 模型实例跨预览会话复用（旧版每次开始预览都重新加载模型，耗时秒级）
- 移除 LibVLC 依赖后输出目录减少数十 MB 无用 native DLL
- 消除连接成功后每 5 秒两条的状态刷新日志刷屏
- `Detection/yoloConfig.json` 乱码注释清理、模型路径与新目录对齐

### 验证

- 构建 0 警告 0 错误；冒烟测试通过（启动存活、关闭消息正常退出 ExitCode=0，FormClosing 清理链路完整）
- 检测算法数学逻辑（letterbox 缩放/pad 计算/坐标还原/NMS/过滤阈值语义）保持不变，现场调好的检测效果不受影响
