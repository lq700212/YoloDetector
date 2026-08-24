# YoloDetector — 摄像头实时人员检测系统

基于 **WinForms (.NET Framework 4.7.2) + OpenCvSharp4 + ONNX Runtime** 的 RTSP 视频流实时目标检测工具：接入网络摄像头，使用 YOLO ONNX 模型对画面逐帧推理，检测结果（检测框 + 类别 + 置信度）实时叠加显示在预览窗口。**推理全程在本机完成，不依赖 Python 环境**。

## 功能特性

- 📹 **RTSP 流接入**：OpenCV VideoCapture 逐帧捕获，单槽位缓冲自动丢帧防积压
- 🎯 **YOLO 实时推理**：ONNX Runtime 加载模型，独立检测线程，UI 不卡顿
- 🖼️ **双可视化方案**：GDI+ 红框（YoloBuiltin）/ OpenCV 绿框（OpenCV），配置文件一键切换
- 🔌 **多品牌相机解耦**：`ICameraApi` 接口 + 工厂模式，当前内置安格华（ANGEHUA）实现，海康/大华可按同一模式扩展
- 🎛️ **设备管理**：连接测试（TCP 探测带超时）、设备状态轮询（防重入）、RTSP 拉流 / RTMP 推流开关
- 📋 **分级日志**：文件日志（logs/log_日期.txt）+ UI 日志面板；YOLO 详细日志与每帧结果日志独立开关，默认关闭防刷屏
- ⚙️ **全配置化**：阈值、IP、地址模板、通道数等全部外置于 JSON 配置，改现场行为不动代码

## 技术栈

| 组件 | 用途 |
| --- | --- |
| .NET Framework 4.7.2 (C# 7.3, x64) | WinForms 桌面应用 |
| [OpenCvSharp4](https://github.com/shimat/opencvsharp) 4.10 | RTSP 捕获、图像处理、绘制 |
| [Microsoft.ML.OnnxRuntime](https://github.com/microsoft/onnxruntime) 1.20 | YOLO 模型推理 |
| Newtonsoft.Json | 配置序列化 |

## 项目结构

```
YoloDetector/
├── Program.cs              入口
├── UI/                     视图层（MainForm 纯交互 + Layout 布局 partial）
├── App/                    编排层（视频检测控制器 / 相机连接控制器）
├── Detection/              检测域：帧源 / 检测管道 / YOLO检测器 / 可视化器 / 后处理器
├── Cameras/                相机域：ICameraApi 抽象 + 品牌实现 + 工厂
├── Configuration/          配置层：AppConfig 加载器 + 配置模型
├── Infrastructure/         基础设施：文件日志
├── appsettings.json        主配置（激活相机品牌）
├── cameraConfigs/*.json    各品牌相机参数
├── Detection/yoloConfig.json      YOLO 运行参数
├── Detection/model/*.onnx         YOLO 模型文件
└── docs/ARCHITECTURE.md    架构与技术要点（开发者必读）
```

依赖方向严格单向：`UI → App → Detection/Cameras → Infrastructure/Configuration`。

## 快速开始

### 环境要求

- Windows 10/11 **x64**
- [.NET Framework 4.7.2 运行时](https://dotnet.microsoft.com/download/dotnet-framework/net472)（Win10 1809+ 通常已内置）
- 与本机网络互通的 RTSP 摄像头

### 构建

```powershell
dotnet build YoloDetector.csproj -v q
```

产物输出至 `bin\Debug\net472\YoloDetector.exe`。

### 使用步骤

1. 启动 `YoloDetector.exe`
2. 左侧输入**相机 IP** → 点击【连接相机】（TCP 探测 RTSP 端口，10 秒超时）
3. 确认流地址正确（默认模板 `rtsp://{ip}:554/stream{channel}`，可在 `cameraConfigs\ANGEHUA.json` 修改）
4. 点击【开始预览】→ 右侧窗口显示带检测框的实时画面；【停止预览】结束
5. 【开启拉流】/【开启推流】用于控制相机端的流开关（依相机固件支持而定）

## 配置说明

| 文件 | 作用 |
| --- | --- |
| `appsettings.json` | 全局配置，`ActiveCameraConfig` 字段切换激活的品牌 |
| `cameraConfigs\{品牌}.json` | 该品牌的 IP 默认值、账号密码、API 路径、RTSP 地址模板、最大通道数等 |
| `Detection\yoloConfig.json` | 模型路径、置信度/NMS 阈值、可视化方案、调试日志开关 |

常用调参（`Detection\yoloConfig.json`）：

```jsonc
{
  "ModelPath": "Detection/model/yolo26n.onnx",   // 更换模型只改这里
  "ConfidenceThreshold": 0.2,   // 低→灵敏但误检多；高→精准但可能漏检
  "NmsThreshold": 0.5,          // 检测框去重强度
  "VisualizerType": "YoloBuiltin", // YoloBuiltin=红框(GDI+) / OpenCV=绿框
  "Enabled": true               // 总开关
}
```

> 检测不到人时的排查顺序：① 确认画面中有人且足够大 ② 调低 `ConfidenceThreshold` ③ 临时打开 `YoloDebugLog` 查看 logs 目录下的推理过程日志。

## 更换检测模型

1. 将导出的 ONNX 模型放入 `Detection\model\`（模型获取与 pt→onnx 转换见 `YOLOTest\doc\YOLO V26 ONNX 模型获取与验证完全指南.md`）
2. 修改 `Detection\yoloConfig.json` 的 `ModelPath`
3. 重启程序或重新开始预览即可生效（模型实例跨预览会话复用，重复启停不会重复加载）

## 新增相机品牌

1. 在 `Cameras\` 下新建实现类实现 `ICameraApi`（构造函数注入 IP，方法不带 ip 参数）
2. 在 `Cameras\CameraApiFactory.Create()` 中注册品牌分支
3. 在 `cameraConfigs\` 下新建 `{品牌}.json`，并在 `appsettings.json` 切换激活品牌

## 开发者文档

| 文档 | 内容 |
| --- | --- |
| [AGENTS.md](AGENTS.md) | AI/维护者协作规范：铁律、分层边界、并发红线、构建验证命令 |
| [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) | 架构分层图、线程模型、Mat 所有权链路、YOLO 推理实现细节 |
| [CHANGELOG.md](CHANGELOG.md) | 版本改动记录 |

## 已知限制

- RTSP 断流后画面冻结（捕获线程持续重试），暂未实现自动重连
- 网络假死时停止预览可能有数秒等待（锁保护保证不崩溃）
- HIK / DAHUA 品牌客户端尚未实现，工厂会回退到安格华默认实现
