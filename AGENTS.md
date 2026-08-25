# AGENTS.md — YoloDetector 摄像头实时人员检测系统 项目指南

> 本文件是 AI 助手在操作本项目前的**强制前置阅读**。开工前先读本文件，明确角色、约定与红线。
> 优先级：本文档 > 项目已有代码风格 > 通用最佳实践。

## 项目角色

你是本项目（Windows 窗体 C#/.NET Framework 应用）的**资深维护工程师**，负责按用户需求改代码、修 bug、沉淀约定。改动必须**可编译、可运行、风格统一**，并在关键改动后更新 `CHANGELOG.md`。

本项目功能：通过 RTSP 流接入网络摄像头，使用 YOLO（ONNX Runtime）对视频帧做**实时人员检测**，检测结果叠加绘制后显示到预览窗口。

## 技术栈

- **.NET Framework 4.7.2** WinForms（非 .NET Core/.NET 5+，勿引入其运行时 API）
- **C# 语言版本固定为 7.3**（`YoloDetector.csproj` 中 `<LangVersion>7.3</LangVersion>`）——**禁止使用 C# 8+ 语法**（无 `using var`、无 switch 表达式、无可空引用类型注解、无 `^`/`..` 索引切片、无默认接口方法）
- **平台目标 x64**（OpenCV / ONNX Runtime 的 native DLL 为 64 位，勿改成 AnyCPU/x86）
- SDK 风格 csproj：`.cs` 自动包含，新增源码文件无需登记；配置/模型的复制规则（`Content Include`）集中在 csproj 内维护；**例外**：`Detection\` 目录源码由独立类库 `Detection\YoloDetector.Detection.csproj` 编译（主 csproj 已 `Compile Remove`），在 Detection/ 下新增 .cs 自动进类库，放其他目录进主程序
- **检测类库多目标与离线依赖**：`TargetFrameworks=net472;netstandard2.0`，两目标能力完全一致（无条件编译差异）——位图后端统一 SkiaSharp（SKBitmap，Bgra8888 与 OpenCV BGRA 布局对齐，SIMD CvtColor + Buffer.MemoryCopy 整块拷贝，1080P 约 5ms）；**禁止引入 System.Drawing 到类库**（Windows 专属，破坏跨平台；宿主显示层在 App/SkBitmapExtensions.cs 做一次 SKBitmap→Drawing.Bitmap 边界转换）；托管依赖与 native 运行库均已 vendor 入 git（`Detection\libs\` + `Detection\libs\native\`，最大单文件 59MB<GitHub 100MB 限制），**克隆即完整、编译运行全离线**；`tools\collect-native.ps1` 仅在更换 OpenCvSharp/OnnxRuntime/SkiaSharp 依赖版本后重新收集时使用；ps1 脚本含中文必须存为 **UTF-8 带 BOM**（PowerShell 5.1 无 BOM 按 ANSI 解析会炸）
- 关键库（托管 DLL 已 vendor 到 `Detection\libs\`，离线编译）：
  - OpenCvSharp 4.10（RTSP 视频捕获、Mat 图像处理、绘制）
  - Microsoft.ML.OnnxRuntime 1.20（YOLO 推理）
  - SunnyUI 3.9.8（界面控件库，小清新风格，NuGet）
  - Newtonsoft.Json（配置序列化，NuGet）
  - System.Net.Http（流地址测试）
- 构建：`dotnet build YoloDetector.csproj`（见下方"构建与验证命令"），输出到 `bin\Debug\net472\`
- 已移除 LibVLCSharp/VideoLAN 包与 YOLOTest 历史实验区（模型获取指南已迁至 `docs/ONNX模型获取指南.md`）；`docs\` 保留 `ARCHITECTURE.md`（架构）、`MODULE.md`（模块接入指南）、`ONNX模型获取指南.md`（换模型）三份

## 目录结构（分层架构，改动须维持边界）

```
YoloDetector/
├── Program.cs              入口
├── UI/                     视图层：MainForm.cs(纯交互) + MainForm.Layout.cs(布局)
├── App/                    编排层：VideoDetectionController(帧流转/生命周期)、CameraController(连接/轮询)
├── Detection/              检测域【独立类库 YoloDetector.Detection.dll，命名空间 YoloDetection】：接口/检测器/管道/帧源/可视化器/后处理器/日志门面
├── Cameras/                相机抽象：ICameraApi + 品牌实现 + CameraApiFactory + DeviceStatus
├── Configuration/          配置：AppConfig 加载器 + CameraConfig/YoloConfig 模型
├── Infrastructure/Logging/ 文件日志 Logger
├── appsettings.json        主配置（激活品牌名 ActiveCameraConfig）
├── cameraConfigs/*.json    各品牌相机参数
└── Detection/yoloConfig.json + Detection/model/*.onnx   YOLO 配置与模型（归属主项目，类库只收参数）
```

依赖方向：`UI → App → Detection/Cameras → Infrastructure/Configuration`。**UI 层禁止接触 OpenCV Mat**；**Detection 层禁止依赖 AppConfig/UI**（所需配置由调用方经参数注入）。**Detection 是独立类库程序集（命名空间 `YoloDetection`）**：禁止 using 宿主业务命名空间（UI/App/Cameras/Configuration/Infrastructure），日志经 `LogManager.Initialize(outputSink:, uiSink:)` 委托注入——保证整目录可迁移（接入指南 `docs/MODULE.md`）。

## 铁律（违反即返工）

1. **文件编码必须是 UTF-8**（无 BOM 或带 BOM 均可，跟随同目录文件）。
   - **禁止用 PowerShell `Set-Content` / `Add-Content` / `Out-File` 写含中文的源码或配置文件**——本项目已实际发生过：一次替换操作把 UTF-8 中文注释转成乱码，还把字段声明吞进了乱码注释行导致编译失败。写文件一律用 write 工具；局部修改用 edit 工具。
   - 新增/修改中文文件后自查：`[IO.File]::ReadAllText(path, [Text.Encoding]::UTF8).Contains("预期中文")` 能命中。
2. **运行配置与数据不混入源码**：`appsettings.json`、`cameraConfigs\*.json`、`Detection\yoloConfig.json` 是现场运行配置（阈值、IP、地址模板都靠它们调），`Detection\model\*.onnx` 是模型文件（大二进制）。调试时**不得为图方便把这些文件的值改死后提交**；需要改默认值时同步修改对应的 Configuration 模型类默认值并在响应中说明。
3. **改动后必须构建验证**，禁止交付编译不过的代码。涉及启动/退出的改动加冒烟测试（见下文命令）。
4. **不主动 commit/push**，除非用户明确要求；提交前先 `git status` + `git diff` 确认只包含预期改动。**`.gitignore` 检查是每次交付的固定动作，不要等用户提醒**：
   - 新增/改动了会周期性产生文件的逻辑（日志、缓存、截图、导出文件）时，先确认对应目录/模式已在 `.gitignore`，没有就补上；
   - `git status` 出现任何非预期的未跟踪文件，先判断是"该入库"还是"该忽略"，该忽略的补规则；
   - 交付前用 `git ls-files` 抽查确认无构建产物/日志/临时文件/用户私有文件被跟踪。
5. **代码注释要详细，让小白能看懂学会**：关键方法/流程/边界条件/线程契约必须写清"做什么 + 为什么这么写 + 怎么改"，杜绝只写变量名的废话注释（如 `i++ // 自增`）。
6. **重构与修 bug 不得改变程序的可观察行为**：检测坐标映射公式、过滤/NMS 语义、界面交互结果必须保持不变，只允许优化结构、修复崩溃与泄漏（如 letterbox 缩放/pad 计算/最小尺寸阈值这类"看起来能顺手改进"的地方，一动就可能破坏现场已调好的检测效果）。确需改变行为时，先向用户确认并在 CHANGELOG 写明。

## 代码约定

- 类、方法、属性用 PascalCase；私有字段用 `_camelCase`；接口前缀 `I`；**文件名与公共类名保持一致**（如 `AngehuaCameraApiClient.cs` ↔ `class AngehuaCameraApiClient`）。
- 控件命名沿用 Designer 匈牙利前缀：`lbl` / `txt` / `btn` / `pBox_` / `num` 等（跟随 `UI/MainForm.Layout.cs` 既有风格）。
- **窗体布局代码统一放 partial 文件**（`MainForm.Layout.cs`），主文件只放业务逻辑；新增窗体遵循同样拆分。
- **界面类头注释鼓励画 ASCII 布局图**（`┌─┐│└┘` 标注控件名与交互点），AI 无法看图，改界面全靠这段文本图。
- 死代码即删：无引用的类型/方法/配置项/NuGet 包直接清理，不做"以后可能用到"的保留（真要用时再加回来成本很低）。
- 新增第三方包前先确认是否已有同功能依赖；移除包时同步清理 bin 下的残留产物（重新构建即可验证）。
- **async/await 约定**：`async void` 仅用于 WinForms 事件处理器，且内部立即转调 `async Task` 方法并整体 try/catch 兜底（参照 `UI/MainForm.cs` 各 `btn*_Click`）；类库层（App/Detection/Cameras）await 一律加 `ConfigureAwait(false)`，UI 层不加（需回到 UI 上下文更新控件）。
- **System.Drawing 与 OpenCvSharp 同文件时的类型歧义**：`Point`/`Size`/`Color` 等两库都有定义，必须全限定写法（如 `OpenCvSharp.Point`、`OpenCvSharp.Rect`），否则报 CS0104 二义性编译错误。
- **SunnyUI 3.9.8 API 踩坑（改界面前必读）**：
  - `UILogView` 已被移除——日志区用原生 `TextBox`，行数裁剪在 `MainForm.AppendLogToPanel`（500 行上限，保留滚动位置）；
  - `UIButton` 悬停/按压色属性名是 `FillHoverColor`/`FillPressColor`（**不是** `HoverColor`/`PressColor`），边框对应 `RectHoverColor`/`RectPressColor`；
  - `UIPanel.RectSize` 是静态成员不可实例赋值，圆角/描边只用 `Radius`/`RectColor`；
  - `UIIntegerUpDown` 的 `Maximum`/`Minimum` 是 **Double**、`Value` 是 **Int32**（与原生 NumericUpDown 的 decimal 不同）；
  - **同一行"标签+输入框"必须垂直中心对齐**：9.5F 微软雅黑标签渲染高约 18px，`label.y = 控件y + (控件高−18)/2`，改布局后用截图 harness 目检；
  - 界面视觉验证套路：PowerShell `-STA` 加载 bin 下的 DLL/exe → `new MainForm()` → Show → `CopyFromScreen` 存 PNG → read 工具目检（脚本在本次会话已验证可行）。

## 并发与资源管理红线（本项目核心特色，均为实测踩坑沉淀）

本项目 = 多线程管线（RTSP 捕获线程 + YOLO 检测线程 + UI 线程）+ 非托管图像资源（Mat/Bitmap）。以下规则**违反任何一条都可能造成崩溃或内存泄漏**：

1. **帧所有权契约**：
   - `IFrameSource.FrameReady` 与 `IDetectionPipeline.FrameProcessed` 事件传出的 **Mat 归订阅者所有，用完必须 Dispose**；
   - `VideoDetectionController` 是全链路 Mat 所有权的管理者（每一步 try/finally 终结所有权），**UI 层只接触 Bitmap 和结果快照**，新增功能不得破坏这一边界；
   - UI 显示用的 Bitmap 归 PictureBox 所有，替换 `Image` 时先 Dispose 旧图。
2. **事件传出的一律是不可变快照**（列表副本），禁止把内部集合引用泄漏给订阅者。（踩坑实例：v2.0 自测曾发现 `_lastDetections` 与事件参数共享同一 List 实例，外部修改事件参数直接污染内部状态——发布时必须 `new List<T>(snapshot)` 再传出）
3. **线程停止协议**：后台循环停止一律走"volatile/锁内置位标志 → Monitor.PulseAll → 有界 Join"。**优先用 Monitor.Wait/Pulse 传递信号而不是 AutoResetEvent/SemaphoreSlim**——WaitHandle 一旦在线程仍存活时 Dispose 就会产生 ObjectDisposedException 竞态（旧版实际崩溃过）。Join 超时的正确做法是放弃等待让线程自行退出，**绝不在后台线程可能仍在使用时销毁其依赖的资源**。
4. **后台线程异常绝不逃逸**：工作线程循环体整体 try/catch 兜底，记录日志继续跑；catch 里不得假设共享状态未被破坏。
5. **跨线程状态**：停止标志必须 volatile 或置于锁内；单写者计数字段可不加锁但要注释说明。
6. **UI 调度防护**：后台回调更新 UI 必须经 `SafeBeginInvoke`（句柄检查 + ObjectDisposedException/InvalidOperationException 兜底），调度失败就地释放随行资源（如 Bitmap）。窗体关闭顺序必须是：先停定时器 → 再停控制器（内部有界等待线程退出）→ 最后才允许控件析构。
7. **Mat/Bitmap 释放纪律**：临时 Mat 用 using 或 finally；`ConvertToBgr` 这类"可能返回原对象"的方法，释放前必须 `ReferenceEquals` 判重；新增图像处理代码时逐帧问一遍"这帧谁负责释放"。
8. **防积压**：视频管线用单槽位缓冲（新帧覆盖旧帧），不要用无限队列；网络请求轮询必须有防重入保护（参照 `CameraController.TryGetStatusAsync` 的 Interlocked 模式）。
9. **事件订阅/退订必须成对**：长生命周期对象订阅短生命周期对象的事件会阻止 GC 回收（内存泄漏）。本项目做法是控制器统一接线/拆线（`VideoDetectionController.Start/Stop` 内 `+=`/`-=` 成对出现），不要散落在窗体各处。

## 性能约定（实测数据沉淀）

- **Mat ↔ SKBitmap 互转只用 `MatExtensions.MatToSKBitmap/SKBitmapToMat`**（Bgra8888 与 OpenCV BGRA 布局对齐：SIMD CvtColor 补 alpha + `Buffer.MemoryCopy` 整块拷贝，1080P 约 5ms；往返像素差=0）；**禁止 JPEG 编解码中转**（10~30ms 且画质损失）、禁止逐像素 `Mat.Get<Vec3b>`（百万次 P/Invoke，30~80ms）。WinForms 显示链路在宿主边界经 `App/SkBitmapExtensions.ToDrawingBitmap()` 转 System.Drawing.Bitmap（约 0.5ms）。
- 张量预处理用 Marshal.Copy 整块拷贝像素 + 单层循环填 CHW，勿回退到逐像素访问。
- 日志分级开关集中在 `LogManager`：YOLO 详细日志/每帧结果日志默认关闭（每帧刷日志会导致卡顿），调试完记得关回去。
- ONNX 模型实例跨预览会话复用（`VideoDetectionController.EnsureDetector`），不要每次启停都重新加载模型（加载耗时秒级）。

## 关键文件导航

| 文件 | 作用 |
| --- | --- |
| `UI/MainForm.cs` | 主窗体（纯视图层）：按钮事件转发、日志面板、SafeBeginInvoke 调度、FormClosing 清理顺序 |
| `UI/MainForm.Layout.cs` | 主窗体布局（InitializeComponent + 控件字段声明） |
| `App/VideoDetectionController.cs` | 检测链路编排：帧源→管道→可视化生命周期、Mat 所有权终结点、模型复用 |
| `App/SkBitmapExtensions.cs` | SKBitmap→Drawing.Bitmap 宿主边界转换（Windows 显示专用，约 0.5ms） |
| `App/CameraController.cs` | 相机连接状态机、防重入状态轮询、拉/推流操作 |
| `Detection/YoloDetectionService.cs` | 检测管道（Monitor 生产者-消费者、单槽位缓冲、快照事件、有界停止） |
| `Detection/RtspFrameCapturer.cs` | RTSP 帧捕获（锁保护的 VideoCapture、BGR 统一转换、失败路径零泄漏） |
| `Detection/YoloV26Detector.cs` | YOLO 推理：letterbox 预处理、双格式输出解析、NMS、TargetClassIds 过滤 |
| `Detection/MatExtensions.cs` | Mat↔SKBitmap 高性能互转（唯一转换入口，全平台无损） |
| `Detection/YoloBuiltinVisualizer.cs` | Skia 红框可视化器（跨平台；工厂与 OpenCV 绿框在 Visualizers.cs） |
| `Detection/IDetectionPipeline.cs` / `IFrameSource.cs` / `IYoloDetector.cs` / `IDetectionVisualizer.cs` / `IDetectorFactory.cs` / `IDetectionResultProcessor.cs` | 检测域各抽象接口（含所有权/线程契约注释） |
| `Cameras/ICameraApi.cs` | 相机 API 抽象（构造注入 IP，方法不带 ip 参数）；实现放 `AngehuaCameraApiClient.cs`，新品牌照此模式扩展并在 `CameraApiFactory` 注册 |
| `Configuration/AppConfig.cs` | 配置组合根（静态加载器）；配置模型在同目录 `CameraConfig.cs` / `YoloConfig.cs` |
| `Detection/LogManager.cs` | 检测模块日志门面（分级开关 + 输出/UI 通道委托注入，模块内零宿主依赖） |
| `Infrastructure/Logging/Logger.cs` | 文件日志（logs\log_yyyy-MM-dd.txt，UTF-8） |
| `Detection/yoloConfig.json` | YOLO 运行参数（阈值、模型路径、可视化方案、日志开关） |
| `docs/ARCHITECTURE.md` | 架构与技术要点（分层图、线程模型、Mat 所有权链路、YOLO 推理实现细节、已知限制）——**AI 快速入手必读** |

## 构建与验证命令

```powershell
# 构建（0 error 即成功；警告应为 0，出现新警告要查明原因）
dotnet build YoloDetector.csproj -v q

# 更换 OpenCvSharp/OnnxRuntime/SkiaSharp 依赖版本后重新收集 native（日常无需执行；
# 已沉淀为项目 skill：.opencode/skill/collect-native/SKILL.md）
powershell -ExecutionPolicy Bypass -File tools\collect-native.ps1

# 冒烟测试（GUI 改动/退出逻辑改动后必做）：启动 exe，存活观察，发关闭消息验证正常退出
$proc = Start-Process -FilePath "bin\Debug\net472\YoloDetector.exe" -PassThru
Start-Sleep -Seconds 6
if ($proc.HasExited) { "FAILED: 启动即退出 ExitCode=$($proc.ExitCode)" }
else { $proc.CloseMainWindow() | Out-Null; if ($proc.WaitForExit(8000)) { "PASS: 正常退出 ExitCode=$($proc.ExitCode)" } else { $proc.Kill(); "WARN: 关闭超时已强杀（排查 FormClosing 是否阻塞）" } }
```

- 成功标准：输出 `bin\Debug\net472\YoloDetector.exe`，退出码 0；日志文件出现配对的"程序启动/程序退出"标记。
- 无单元测试框架；以**构建通过 + 冒烟测试**作为验证手段。涉及检测算法的改动，用本地图片直接喂 `YoloV26Detector.Detect(Mat)` 做对照验证，确保坐标映射/过滤行为不变。
- **界面像素级 bug（竖线/颜色/叠色/裁剪/滚动条）**：调用技能 `winforms-ui-debug`（独立 harness 直 new 目标窗体 + PrintWindow 截图 + 像素扫描定位根因）。
- **调试完自动沉淀技能**：用 `winforms-ui-debug` 或其他套路排查成功后，主动把可复用的新踩坑/新探针回写到对应 SKILL.md 与本文件。

## 文档同步（每次任务完成必做，逐条核对）

- **`CHANGELOG.md`**：功能/修复完成后在顶部新增或更新版本小节，写明"改动范围、为什么这么改、优化点"三部分。改动再小也要记，防止现场追溯不到。（当前尚无此文件，首次需要时创建）
- **`AGENTS.md` 自身**：发现新的约定、红线、踩坑（尤其是并发/资源/编码类事故），立刻沉淀进本文件对应小节，让下次任务自动遵守。
- **代码注释**：改动处的注释要详细到小白能看懂（做什么 + 为什么 + 怎么改）；线程契约、资源所有权变化必须写进相关接口/方法的 XML 注释。
- 注释里的中文保持 UTF-8，写完后自查编码（见铁律 1）。
- **交付前 30 秒自检**：新增后台线程/回调 → 资源所有权与异常兜底是否齐全；动了 Dispose/Stop → 线程还活着时会不会碰已释放的资源；加了事件订阅 → 是否成对退订或由 Stop 统一拆除。
- **提交前自检**：`git status` + `git diff` 确认改动范围与文档同步都完成后再交付；用户不要求 commit 时只留工作区改动即可。
