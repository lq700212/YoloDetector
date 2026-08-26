# YoloDetector — 摄像头实时人员检测系统

基于 **WinForms (.NET Framework 4.7.2) + OpenCvSharp4 + ONNX Runtime** 的 RTSP 视频流实时目标检测工具：接入网络摄像头，使用 YOLO ONNX 模型对画面逐帧推理，检测结果（检测框 + 类别 + 置信度）实时叠加显示在预览窗口。**推理全程在本机完成，不依赖 Python 环境**。

在人员检测之上还内置了**静电杆触摸检测**（工厂防静电场景）：对人体做姿态估计取手腕关键点，判定是否落入静电杆区域并持续触摸——详见[功能特性](#功能特性)与[静电杆触摸检测配置](#静电杆触摸检测配置esdconfigjson)。

## 功能特性

- 📹 **RTSP 流接入**：OpenCV VideoCapture 逐帧捕获，单槽位缓冲自动丢帧防积压
- 🎯 **YOLO 实时推理**：ONNX Runtime 加载模型，独立检测线程，UI 不卡顿
- ⚡ **静电杆触摸检测**（可选旁路）：YOLO-pose 人体关键点 + 静电杆 ROI 区域规则——手腕进入标定区域并持续达到阈值时长即判定"正在触摸"，预览画面叠加黄色 ROI 框/接触状态/手腕落点，日志面板提示状态翻转；**ROI 支持预览画面上鼠标拖拽框选标定（实时生效并自动保存）**；判定纯几何规则完全可解释，ROI/时长/容差全配置化，关闭后零开销
- 🖼️ **双可视化方案**：Skia 红框（YoloBuiltin）/ OpenCV 绿框（OpenCV），配置文件一键切换；绘制后端跨平台（Windows/Linux 效果一致）
- 🧩 **检测模块可整体迁移**：`Detection/` 为独立类库（net472 + netstandard2.0 双目标），托管与 native 依赖（Windows + Linux）全部内置仓库，离线编译、整目录复制即接入，Linux 上检测能力开箱可用（见 `docs/MODULE.md`）
- 🔌 **多品牌相机解耦**：`ICameraApi` 接口 + 工厂模式，当前内置安格华（ANGEHUA）实现，海康/大华可按同一模式扩展
- 🎛️ **设备管理**：连接测试（TCP 探测带超时）、设备状态轮询（防重入）、RTSP 拉流 / RTMP 推流开关
- 📋 **分级日志**：文件日志（logs/log_日期.txt）+ UI 日志面板；YOLO 详细日志与每帧结果日志独立开关，默认关闭防刷屏
- ⚙️ **全配置化**：阈值、IP、地址模板、通道数等全部外置于 JSON 配置，改现场行为不动代码

## 技术栈

| 组件 | 用途 |
| --- | --- |
| .NET Framework 4.7.2 (C# 7.3, x64) | WinForms 桌面应用 |
| [OpenCvSharp4](https://github.com/shimat/opencvsharp) 4.10 | RTSP 捕获、图像处理、绘制（已 vendor，离线编译） |
| [Microsoft.ML.OnnxRuntime](https://github.com/microsoft/onnxruntime) 1.20 | YOLO 模型推理（已 vendor，离线编译） |
| [SkiaSharp](https://github.com/mono/SkiaSharp) 2.88 | 跨平台位图与绘制后端（检测模块统一使用） |
| Newtonsoft.Json | 配置序列化 |

## 项目结构

```
YoloDetector/
├── Program.cs              入口
├── UI/                     视图层（MainForm 纯交互 + Layout 布局 partial）
├── App/                    编排层（视频检测控制器 / 相机连接控制器 / SKBitmap 显示转换）
├── Detection/              检测域【独立类库】：帧源 / 检测管道 / YOLO检测器 / 姿态检测器 / 静电接触分析 / 可视化器 / 后处理器
│   └── libs/               离线托管依赖（已入 git）+ libs/native/ 运行库（collect 脚本收集）
├── Cameras/                相机域：ICameraApi 抽象 + 品牌实现 + 工厂
├── Configuration/          配置层：AppConfig 加载器 + 配置模型
├── Infrastructure/         基础设施：文件日志
├── tools/                  collect-native.ps1（native 收集）/ download_pose_model.py（姿态模型下载）
├── appsettings.json        主配置（激活相机品牌）
├── cameraConfigs/*.json    各品牌相机参数
├── Detection/yoloConfig.json      YOLO 运行参数
├── Detection/esdConfig.json       静电杆触摸检测参数
├── Detection/model/*.onnx         模型文件（yolo26n 人员检测 + yolo11n-pose 姿态）
└── docs/                   ARCHITECTURE（架构）/ MODULE（模块接入）/ ONNX模型获取指南 / 技术分享-实现详解
```

依赖方向严格单向：`UI → App → Detection/Cameras → Infrastructure/Configuration`。

## 快速开始

### 环境要求

- Windows 10/11 **x64**
- [.NET Framework 4.7.2 运行时](https://dotnet.microsoft.com/download/dotnet-framework/net472)（Win10 1809+ 通常已内置）
- 与本机网络互通的 RTSP 摄像头

### 构建与运行（离线可用）

```powershell
# 托管依赖与 native 运行库均已入 git，克隆即完整，无需联网
dotnet build YoloDetector.csproj -v q
```

产物输出至 `bin\Debug\net472\YoloDetector.exe`；之后整个 bin 目录可拷贝到无网现场直接运行。

### 使用步骤

1. 启动 `YoloDetector.exe`
2. 左侧输入**相机 IP** → 点击【连接相机】（TCP 探测 RTSP 端口，10 秒超时）
3. 确认流地址正确（默认模板见 `cameraConfigs\ANGEHUA.json` 的 `RtspUrlFormat`，如 `rtsp://{ip}:{port}/ch01.264`）
4. 点击【开始预览】→ 右侧窗口显示带检测框的实时画面；【停止预览】结束
5. 【开启拉流】/【开启推流】用于控制相机端的流开关（依相机固件支持而定）

## 配置说明

| 文件 | 作用 |
| --- | --- |
| `appsettings.json` | 全局配置，`ActiveCameraConfig` 字段切换激活的品牌 |
| `cameraConfigs\{品牌}.json` | 该品牌的 IP 默认值、连接超时、RTSP 端口/地址模板、最大通道数 |
| `Detection\yoloConfig.json` | 模型路径、置信度/NMS 阈值、可视化方案、调试日志开关 |
| `Detection\esdConfig.json` | 静电杆触摸检测开关、姿态模型路径、ROI 标定、判定时序（见下） |

常用调参（`Detection\yoloConfig.json`）：

```jsonc
{
  "ModelPath": "Detection/model/yolo26n.onnx",   // 更换模型只改这里
  "ConfidenceThreshold": 0.2,   // 低→灵敏但误检多；高→精准但可能漏检
  "NmsThreshold": 0.5,          // 检测框去重强度
  "VisualizerType": "YoloBuiltin" // YoloBuiltin=红框(Skia) / OpenCV=绿框
}
```

> 检测不到人时的排查顺序：① 确认画面中有人且足够大 ② 调低 `ConfidenceThreshold` ③ 临时打开 `YoloDebugLog` 查看 logs 目录下的推理过程日志。

### 静电杆触摸检测配置（esdConfig.json）

工作原理：对检出的人员逐人做姿态推理取**手腕关键点**，手腕落入静电杆 ROI 并持续 `HoldDurationMs` 毫秒即判定"正在触摸"；短暂遮挡在 `ReleaseGraceMs` 内不断开。

现场标定（推荐拖拽，一次到位）：

1. 启动预览 → **在画面上按住鼠标左键拖拽，框住静电杆操作部位后松手**——ROI 立即生效（黄色 ESD POLE 框即时移动到新位置）并自动保存回 `esdConfig.json`，无需重启
2. 不满意可重复拖拽修正；也可手动改 `RoiX/RoiY/RoiW/RoiH`（0~1 归一化比例坐标，改完需重启预览）
3. 按需微调：路人扫过也报 → 调大 `HoldDurationMs`；摸了不报 → 加大 `MarginPx` 或调低 `WristConfidenceThreshold`

```jsonc
{
  "Enabled": true,                      // false=关闭(管道零开销，等同纯人员检测)
  "PoseModelPath": "Detection/model/yolo11n-pose.onnx",
  "RoiX": 0.40, "RoiY": 0.25,           // ROI 左上角(归一化)
  "RoiW": 0.20, "RoiH": 0.35,           // ROI 宽高(归一化)
  "MarginPx": 20,                       // 判定容差(像素)，贴边微调用
  "HoldDurationMs": 1500,               // 持续命中多久才算"正在触摸"
  "ReleaseGraceMs": 2000,               // 短暂丢失的宽限期
  "ProcessEveryNFrames": 1,             // CPU 慢可调 2~3(每N帧分析一次)
  "DrawOverlay": true                   // 预览画面叠加 ROI/状态
}
```

> 姿态模型缺失或加载失败时自动降级为纯人员检测并写日志告警，不影响预览使用。

## 更换检测模型

1. 将导出的 ONNX 模型放入 `Detection\model\`（模型获取与 pt→onnx 转换见 `docs\ONNX模型获取指南.md`）
2. 修改 `Detection\yoloConfig.json` 的 `ModelPath`
3. 重启程序或重新开始预览即可生效（模型实例跨预览会话复用，重复启停不会重复加载）

### 重新下载姿态模型（静电触摸检测用）

`Detection\model\yolo11n-pose.onnx` 已入 git，克隆即有；损坏或丢失时一键重取：

```powershell
python tools\download_pose_model.py --export
```

脚本自动走正规渠道（Ultralytics 官方 .pt 权重 + 官方 API 导出 ONNX），支持多直链回退、自动挂 Windows 系统代理（VPN 环境无需额外配置）、onnxruntime 加载校验。

## 新增相机品牌

1. 在 `Cameras\` 下新建实现类实现 `ICameraApi`（构造函数注入 IP，方法不带 ip 参数）
2. 在 `Cameras\CameraApiFactory.Create()` 中注册品牌分支
3. 在 `cameraConfigs\` 下新建 `{品牌}.json`，并在 `appsettings.json` 切换激活品牌

## 开发者文档

| 文档 | 内容 |
| --- | --- |
| [AGENTS.md](AGENTS.md) | AI/维护者协作规范：铁律、分层边界、并发红线、构建验证命令、测试沉淀红线 |
| [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) | 架构分层图、线程模型、Mat 所有权链路、YOLO 推理实现细节 |
| [docs/技术分享-人员检测与人手动作检测实现详解.md](docs/技术分享-人员检测与人手动作检测实现详解.md) | 面向小白的原理讲解：人员检测五步流水线、姿态关键点+接触状态机、两级叠加绘制、多线程/内存/性能工程细节、FAQ（技术分享会材料） |
| [docs/MODULE.md](docs/MODULE.md) | 检测模块接入指南：最小示例、静电杆 ROI 拖拽标定傻瓜接入、接口扩展点、离线部署清单 |
| [docs/ONNX模型获取指南.md](docs/ONNX模型获取指南.md) | 换模型时的下载与 pt→onnx 转换操作手册 |
| [.opencode/skill/全量回归验证/](.opencode/skill/全量回归验证/SKILL.md) | 一键回归验证 skill：构建 + 97 个进程内回归用例 + GUI 冒烟（`Run-AllTests.ps1`），含模块↔用例对账表 |
| [CHANGELOG.md](CHANGELOG.md) | 版本改动记录 |

## 已知限制

- RTSP 断流后画面冻结（捕获线程持续重试），暂未实现自动重连
- 网络假死时停止预览可能有数秒等待（锁保护保证不崩溃）
- HIK / DAHUA 品牌客户端尚未实现，工厂会回退到安格华默认实现
- 静电杆触摸检测为单 ROI 单杆判定（多人同时触摸各自独立跟踪）；跨摄像头联动、报警外发（HTTP/MQTT）尚未实现，可基于 `EsdContactChanged` 事件扩展
- 姿态推理在纯 CPU 上约 100~200ms/人（1080P 多人场景建议开启 `ProcessEveryNFrames=2~3` 或使用带 GPU 的机器）
