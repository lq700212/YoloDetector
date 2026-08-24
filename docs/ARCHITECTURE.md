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

## 2. 线程模型（稳定性核心）

三个线程协作，全部生命周期由控制器管理：

| 线程 | 所属 | 职责 |
| --- | --- | --- |
| UI 线程 | MainForm | 界面、定时器轮询（5s）、显示帧 |
| RTSP 捕获线程 | RtspFrameCapturer.CaptureLoop | 锁内 `_capture.Read` → BGR 统一转换 → 克隆后经 FrameReady 发布 |
| YOLO 检测线程 | YoloDetectionService.DetectionLoop | 取帧 → 推理 → 后处理 → 快照事件 → 可视化 → 帧事件 |

关键机制：

- **单槽位缓冲**：`ProcessFrame` 在锁内克隆新帧并覆盖旧帧（旧帧立即 Dispose），检测慢时自动丢帧，不积压内存。
- **Monitor.Wait/Pulse 信号协议**：检测线程无帧时挂起（零 CPU）；禁用 AutoResetEvent/SemaphoreSlim——WaitHandle 在线程未退出时 Dispose 会产生 ObjectDisposedException 竞态（v1 实际崩溃过）。
- **停止协议**：`volatile/锁内置位 → Monitor.PulseAll → 有界 Join(3~10s)`。Join 超时绝不销毁线程依赖的资源，让其自行退出；下次 Start 会先等旧线程退出，保证任何时刻最多一个检测线程。
- **后台异常零逃逸**：两个工作循环整体 try/catch，记录日志继续运行。

## 3. Mat 所有权链路（内存不泄漏的关键）

每一帧 Mat 从产生到消亡的所有权转移路径，全链路无泄漏：

```
CaptureLoop: frame(栈上,finally释放) ─clone→ bgrFrame(可能=frame,判重释放)
   └─clone→ copy ══FrameReady事件══▶ VideoDetectionController.OnFrameReady
                                        ├─ ProcessFrame(copy): 管道内部 clone 入缓冲
                                        └─ finally copy.Dispose()   ← 所有权终结点①
检测线程: _pendingFrame ─取走─▶ frame
   └─visualizer.Draw(frame)→ outputFrame ══FrameProcessed事件══▶ OnFrameProcessed
        └─ MatToBitmap(outputFrame) → bitmap；outputFrame.Dispose() ← 所有权终结点②
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

加载容错：文件缺失或损坏一律回退代码默认值。业务模块不直接读 AppConfig（由调用方注入参数值），保持 Detection 域零外部依赖。

换 YOLO 模型：onnx 放 `Detection/model/`，改 yoloConfig.json 的 `ModelPath` 即可。模型获取/pt转onnx 见 `YOLOTest/doc/YOLO V26 ONNX 模型获取与验证完全指南.md`。

## 6. 已知限制（后续可改进项）

- **RTSP 断流无自动重连**：流中断后捕获线程持续空转重试（Read 失败 sleep 50ms），画面冻结但不崩溃；需要重连机制时在 `RtspFrameCapturer` 内实现。
- **停止预览可能短暂阻塞 UI**：网络假死时 FFmpeg Read 阻塞数秒，Stop 的锁保护会等它结束（保证不崩溃不泄漏）。如需优化可做后台异步停止 + UI 即时反馈。
- **每帧 BeginInvoke 显示无节流**：25fps 下 UI 来得及消费；若将来卡顿可在控制器加 in-flight 计数丢帧策略。
- HIK/DAHUA 品牌客户端未实现（工厂回退到 Angehua 实现），扩展方式见 `Cameras/ICameraApi.cs` 注释。

## 7. 常用调试入口

- 日志分级开关：`Detection/yoloConfig.json` 的 `YoloDebugLog`（推理过程）/`DetectionResultLog`（每帧结果），输出到 `logs/log_yyyy-MM-dd.txt`，调试完关回 false。
- 检测算法对照验证：`YOLOTest/test/*.py`（Python 侧同模型脚本），或本地图片喂 `YoloV26Detector.Detect(Mat)`。
- 版本历史见根目录 `CHANGELOG.md`。
