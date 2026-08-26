---
name: "全量回归验证"
description: "YoloDetector 一键全量回归验证：构建主项目、运行 109 个进程内回归用例（配置含EsdConfig与ROI标定写回/Mat互转/宿主位图转换/后处理/可视化器/YOLO检测器/姿态检测器/静电接触分析器与叠加渲染/检测管道线程协议与ESD旁路/帧源/端到端含ESD降级/相机客户端与设备状态/日志门面与文件日志/UI坐标换算与框选状态机/UI构造）+ GUI 冒烟测试。触发场景：交付前验证、改动检测链路或线程代码后的回归、新增功能后补测试用例。脚本为主：人工运行 Run-AllTests.ps1 即可完成，AI 负责代跑、分析失败原因并修复。"
---

# 全量回归验证

## 背景

本项目无 xUnit/NUnit 等外部测试框架（坚持"克隆即离线编译"约定），稳定性验证由本 skill 承担：

1. **进程内回归 harness**（`harness\*.cs`）：微型断言框架 + 分区用例，编译输出到主项目 `bin\Debug\net472\`，就地使用现场配置与真实 ONNX 模型；
2. **GUI 冒烟**（`scripts\Invoke-SmokeTest.ps1`）：真进程启动 exe → 存活观察 → 关窗退出码校验 → 日志"程序启动/退出"配对。

## 模块覆盖对账表

| 源文件 | 测试分区 |
| --- | --- |
| Configuration/AppConfig.cs、CameraConfig.cs、YoloConfig.cs、EsdConfig.cs | ConfigTests（含损坏回退/模板替换/模型存在性/EsdConfig现场加载与ToOptions夹紧/UpdateRoiJson局部更新保留注释字段/ApplyNormalizedRoi就地夹紧） |
| Detection/MatExtensions.cs | MatExtensionsTests（像素级无损往返） |
| App/SkBitmapExtensions.cs | SkBitmapExtensionTests（Bgra8888 错位回归防线，v2.1 真实花屏 bug） |
| Detection/IDetectionResultProcessor.cs 三个处理器 + DetectionResult.cs | ProcessorTests（行为红线锁定） |
| Detection/Visualizers.cs、YoloBuiltinVisualizer.cs | VisualizerTests（不污染原帧/null 契约/工厂） |
| Detection/YoloV26Detector.cs | DetectorTests（真实模型推理契约） |
| Detection/YoloPoseDetector.cs、PoseResult.cs | PoseTests（真实模型推理契约 + bus真图端到端：检人→姿态→手腕落位） |
| Detection/EsdContactAnalyzer.cs、EsdAnalysisOptions.cs、EsdRoiRect.cs、EsdPersonStatus.cs | EsdAnalyzerTests（虚拟时钟驱动状态机全分支：Hold认定/宽限保持/超时退出/轨迹遗忘/快照隔离/Options热更新ROI夹紧且分析器立即可见） |
| Detection/EsdOverlayRenderer.cs、IEsdOverlayRenderer.cs | EsdAnalyzerTests 中 Overlay 契约用例（null参数安全/原地修改帧/空快照仍画ROI） |
| Detection/YoloDetectionService.cs 的 ESD 旁路 + EsdOverlayRenderer.cs | EsdAnalyzerTests 尾部管道集成用例（事件联动/姿态异常不拖垮主检测/未配置零事件） |
| Detection/ZoomMapping.cs、RoiSelectionState.cs（随类库迁移） | RoiSelectionTests（Zoom显示矩形居中计算/控件点映射归一化与黑边夹紧/拖拽端到端换算归一化ROI含贴边回收/框选状态机正常流·误触忽略·反向规范化·未按下忽略） |
| Detection/YoloDetectionService.cs | PipelineTests（线程协议，FakeDetector 驱动） |
| Detection/RtspFrameCapturer.cs | FrameSourceTests（视频文件流 + 拒绝连接） |
| App/VideoDetectionController.cs | EndToEndTests（端到端全链路 + ESD旁路装配/姿态模型缺失自动降级） |
| App/CameraController.cs、CameraApiFactory.cs | CameraControllerTests（未连接契约/工厂） |
| Cameras/AngehuaCameraApiClient.cs、DeviceStatus.cs | AngehuaClientTests（快速失败/有界超时/桩契约/使用率计算） |
| Detection/LogManager.cs | LogManagerTests（三通道独立开关） |
| Infrastructure/Logging/Logger.cs | LoggerTests（惰性初始化/Close 幂等与静默） |
| UI/MainForm.cs + Layout、Program.cs | UiSmokeTests（STA 构造冒烟）+ Invoke-SmokeTest.ps1（真进程 GUI 冒烟） |

未覆盖边界（说明原因）：MainForm 的 private 交互方法（按钮事件/日志裁剪）需真实 UI 交互驱动，
由进程级 GUI 冒烟整体兜底；CameraApiFactory 的 HIK/DAHUA 回退分支依赖静态现场配置无法注入，
当前仅验证激活品牌路径。

## 目录结构

```
全量回归验证/
├── SKILL.md                      本说明
├── scripts/
│   ├── Run-AllTests.ps1          ★ 总入口：一键全流程
│   ├── Invoke-Harness.ps1        构建+运行进程内 harness
│   ├── Invoke-SmokeTest.ps1      GUI 进程级冒烟
│   └── Invoke-RoiDragVisualCheck.ps1  ROI 拖拽标定目检探针（STA 反射驱动框选+截图，人工目检用）
└── harness/
    ├── YoloDetector.Tests.csproj net472/x64，输出重定向到主 bin
    ├── TestFramework.cs          微型断言框架 T + FakeDetector + TestUtil
    ├── ConfigTests.cs            配置加载/回退/RTSP模板/ESD ROI标定写回（UpdateRoiJson/ApplyNormalizedRoi）
    ├── MatExtensionsTests.cs     Mat↔SKBitmap 像素级无损往返
    ├── SkBitmapExtensionTests.cs 宿主边界 SKBitmap→Drawing.Bitmap（花屏回归防线）
    ├── ProcessorTests.cs         后处理器行为红线（边界裁剪/10x20过滤）+ 结果模型属性
    ├── VisualizerTests.cs        可视化器契约（不污染原帧/null契约）
    ├── DetectorTests.cs          YoloV26Detector 真实模型推理契约
    ├── assets/bus.jpg            官方多人街景基准图（姿态端到端用，构建时复制到 bin\assets）
    ├── PoseTests.cs              YoloPoseDetector 契约 + bus真图端到端 + FakePoseDetector
    ├── EsdAnalyzerTests.cs       静电接触状态机(虚拟时钟) + Options热更新ROI + 管道ESD旁路集成
    ├── PipelineTests.cs          检测管道线程协议（快照隔离/异常零逃逸/停止协议）
    ├── FrameSourceTests.cs       帧源生命周期（本地视频文件当流源）
    ├── EndToEndTests.cs          控制器端到端（视频文件流+真模型全链路）
    ├── CameraControllerTests.cs  相机控制器未连接契约/工厂
    ├── AngehuaClientTests.cs     安格华客户端契约 + DeviceStatus 计算
    ├── LogManagerTests.cs        日志门面三通道独立开关
    ├── LoggerTests.cs            文件日志契约（Close 后进程内日志静默，须在 UI 冒烟前）
    ├── RoiSelectionTests.cs      ROI 拖拽标定纯逻辑（Zoom 坐标换算 + 拖拽端到端换算 + 框选状态机，测的是类库 RoiSelectionState/ZoomMapping）
    └── UiSmokeTests.cs           MainForm 构造/显示/关闭（STA，必须最后跑）
```

## 执行

```powershell
# 一键全流程（推荐）：构建 → 全部用例 → GUI 冒烟，退出码 0 即全绿
powershell -ExecutionPolicy Bypass -File ".opencode\skill\全量回归验证\scripts\Run-AllTests.ps1"

# 只跑其中一类
powershell -ExecutionPolicy Bypass -File ".opencode\skill\全量回归验证\scripts\Invoke-Harness.ps1"
powershell -ExecutionPolicy Bypass -File ".opencode\skill\全量回归验证\scripts\Invoke-SmokeTest.ps1"
```

预期：harness 输出 `汇总: PASS=109 FAIL=0`；GUI 冒烟 `[SMOKE] 结果: 全部通过`；总退出码 0。
耗时参考：全程约 1~2 分钟（其中 RTSP 拒绝连接用例固定消耗约 30 秒，是 FFmpeg 内部超时的固有行为）。

## 新增测试用例的固定流程（AI 必须遵守）

**凡是为本项目新写的测试用例或冒烟步骤，一律沉淀到本 skill，禁止散落在仓库其他位置或只留在会话里：**

1. 在 `harness\` 对应分区的 `*.cs` 里加用例方法（用 T.Case 断言体系）；
2. 全新分区则新建 `XxxTests.cs`（含 `public static void RunAll()`），并在
   `TestFramework.cs` 的 `Program.Main` 里按分区顺序登记一行；
3. 更新本文件的用例总数描述与目录结构注释；
4. 跑一遍 `Run-AllTests.ps1` 确认全绿后才算完成；
5. 若用例暴露了产品 bug：先修 bug，再让用例作为回归防线保留。

## 编写用例的已知坑（实测踩坑沉淀）

- **管道类 fake 框必须造得够大且在画面内**：`YoloDetectionService` 默认挂了
  `DefaultResultProcessor`（最小 10x20 + 边界裁剪），小框会被正确过滤导致断言失败；
  同理 fake 框必须小于帧尺寸，喂 32x32 小图配大框会被边界裁剪过滤；
- **`Cv2.CountNonZero` 只支持单通道**：多通道像素比对必须 `Cv2.Split` 后逐通道统计，
  否则抛 OpenCVException(cn == 1)；
- **读日志文件必须用 FileShare.ReadWrite 共享打开**：Logger 的 StreamWriter 常驻持有
  写句柄（设计如此），File.ReadAllText 独占打开会抛 IOException；
- **UiSmokeTests 必须最后跑**：MainForm.Close 会触发 Logger.Close()，之后整个进程
  的文件日志静默失效（Logger 防退出期重开句柄的设计）；LoggerTests 自身也会 Close，
  故安排在 UI 冒烟之前、其余用例之后；
- **ConfigTests 会污染静态配置**：AppConfig 是静态组合根，污染后必须在 finally 里
  `AppConfig.Load()` 恢复现场配置，临时品牌文件用完即删（ZZTEST_ 前缀）；
- **harness 引用了主 exe**：主项目必须先构建；csproj 开启 AutoGenerateBindingRedirects
  消除传递依赖版本告警、AllowUnsafeBlocks 支持像素级断言（生成的
  YoloDetector.Tests.exe.config 与主程序 config 不同名，不会覆盖现场配置）；
- **含中文的 ps1 脚本必须 UTF-8 带 BOM**（PowerShell 5.1 无 BOM 按 ANSI 解析）；
  harness 的 .cs 中文注释同理建议带 BOM。
- **改了主项目代码只重编 harness 不生效**：harness 经 HintPath 引用 bin 下的
  Detection.dll/exe——必须先构建主项目（Run-AllTests 已保证顺序，手工单独跑
  Tests.exe 前务必先 dotnet build 主项目，否则跑的是旧逻辑，出现"修了还 FAIL"的假象；
  实际踩坑：EsdContactAnalyzer 改完只编 harness，结束事件时长断言持续失败）；
- **ESD/管道用例的人体框必须在帧内且分帧提交**：出界框被 DefaultResultProcessor
  过滤导致快照为空、Persons[0] 越界；紧挨着提交两帧会被单槽位缓冲合并成
  一帧（正常防积压语义），须 WaitFor 第 1 帧处理完再提交第 2 帧；
- **归一化坐标换算断言禁止 float 精确相等**：0.2f/0.3f 是二进制无限小数，
  乘帧宽后带尾差，会出现"打印值相同却断言失败"，用 Math.Abs(diff)<0.01 容差。
- **STA 目检探针（Invoke-RoiDragVisualCheck.ps1）三个坑**：
  ① write 工具产出的 ps1 是无 BOM UTF-8，含中文注释会被 PowerShell 5.1 按 ANSI 解析炸语法——探针脚本注释一律英文；
  ② `[Reflection.BindingFlags]"NonPublic|Instance"` 字符串解析失败，必须用 `::NonPublic -bor ::Instance`；
  ③ CopyFromScreen 截屏幕区域会被其他窗口遮挡，截图前须 `$form.TopMost=$true; $form.Activate()`；
  探针只触发 Press/Drag 不触发 MouseUp——避免把测试坐标写进现场 esdConfig.json。

## 常见问题

- **构建报找不到 YoloDetector.exe / Detection.dll**：主项目没先构建——Run-AllTests 已保证顺序；
- **端到端/文件流用例被跳过**：本机 OpenCV FFmpeg 后端写不了 MJPG avi 时相关用例自动降级跳过（打印"降级"提示，不影响判定）；
- **RTSP 拒绝连接用例耗时 30 秒**：正常现象（FFmpeg 内部超时），验证的是"不会永久阻塞、最终返回 false"。
</content>
