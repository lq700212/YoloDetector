# CHANGELOG — YoloDetector 版本改动记录

> 格式约定：最新版本在最前；写清「改了什么 / 为什么」，重要改动才展开细节。

## v2.0（2026-08-24）架构重构 + 界面改版 + 走查修复

相对初始功能版的完整变化，分三部分：

### 架构重构与稳定性治理

- 目录重组为五层架构：`UI`（视图）/ `App`（编排）/ `Detection`（检测域）/ `Cameras`（相机域）/ `Configuration` + `Infrastructure`（配置与基础设施），依赖方向单向
- **检测模块拆分为独立类库** `YoloDetector.Detection.dll`（命名空间 `YoloDetection`，工程 `Detection/YoloDetector.Detection.csproj`）：不依赖宿主业务代码，日志/配置全部委托注入，整个目录复制 + 项目引用即可迁移到其他项目（接入指南见 docs/MODULE.md）
- **类库多目标跨平台**：`net472` + `netstandard2.0` 两目标能力完全一致（无条件编译差异）——位图后端统一 SkiaSharp（Google Skia 跨平台封装），MatToSKBitmap/SKBitmapToMat 无损互转（往返像素差=0，1080P 约 5ms）、YoloBuiltin 可视化器 SKCanvas 绘制效果与原 GDI+ 版一致，Windows/Linux/macOS 使用方式与效果完全相同
- **离线编译与部署**：托管依赖与 native 运行库（含 Windows .dll 与 Linux .so，约 113MB）全部 vendor 入 git——克隆即完整、编译运行零网络依赖，部署机只需 bin 目录整包拷贝（工厂无网环境友好）；`tools/collect-native.ps1` 仅在更换依赖版本后重新收集时使用
- 修复并发与资源问题：WaitHandle 释放竞态（改用 Monitor 信号协议）、帧所有权泄漏、状态轮询防重入、捕获循环 double-dispose、日志句柄关闭后重开等
- MainForm 拆分为纯视图 + 布局 partial；新增 VideoDetectionController（帧流转与 Mat 所有权终结）、CameraController（连接状态机）
- 删除死代码与 LibVLC 依赖；ONNX 模型跨预览会话复用（免去重复加载的秒级等待）
- 建立项目文档：AGENTS.md（协作规范）、docs/ARCHITECTURE.md（架构与技术要点）、docs/MODULE.md（模块接入指南）、docs/ONNX模型获取指南.md（换模型）、README.md

### 界面改版（SunnyUI 小清新风格）

- 引入 SunnyUI 3.9.8：顶部天蓝色菜单栏、左侧圆角卡片分组、圆角彩色按钮、带水印输入框
- 日志面板增加 500 行自动裁剪，修复长期运行内存增长与渲染变卡
- 控件名与事件处理器不变，业务逻辑零改动

### 代码走查修复

- 检测模块日志接入界面面板（此前只写文件，现场排查不便）
- 模型路径代码默认值与实际打包路径对齐（配置损坏回退时不再报"模型不存在"）
- 删除无调用的检测器工厂注册表、配置保存方法等死代码；简化 ICameraApi.GetVideoStreamUrl 签名
- **77 条临时自测用例全部通过**（互转/绘制/工厂/后处理器/检测器/管道生命周期与并发压测/RTSP 失败路径/日志门面/相机客户端/端到端推理），自测发现并修复 2 个问题：
  - `YoloDetectionService` 事件快照与内部 `_lastDetections` 共享同一 List 实例，外部修改事件参数会污染内部状态（违反不可变快照契约）→ 事件改传独立副本
  - `YoloV26Detector.Detect` 在 Dispose 后抛出语义不准的"尚未初始化"（InvalidOperationException）→ 已释放检查前置，正确抛 ObjectDisposedException

### 冗余清理（项目未上线，不做旧版兼容）

- 删除 `YOLOTest\` 历史实验区（Python 测试脚本/重复模型/测试图片）；模型获取指南迁至 `docs/ONNX模型获取指南.md`
- 删除空资源文件 `UI/MainForm.resx` 与 `System.Net.Http` 包引用
- 删除死配置：`YoloConfig.Enabled`（无代码消费）、`ApiConfig` 全套 HTTP 路径与签名密钥、连接账号密码/UserAgent、预览页面路径（安格华为纯 TCP 探测，均无消费者），三份品牌配置文件同步清理
- 删除"测试视频流"按钮（HttpClient 不支持 rtsp 协议必然失败，功能与"连接相机"的 TCP 探测重复）
- 删除 `DeviceStatus` 带宽死字段、`SafeInvokeAction` 冗余方法
- 清理 `RtspFrameCapturer` 的 FFmpeg URL 拼参 hack（`?buffer_size=1024000` 双重连接尝试——对 TCP 传输无效且 URL 带查询串时会产生畸形地址），改为直接连接原始地址

### 验证

构建 0 警告 0 错误；冒烟测试通过（正常启动/退出，日志配对标记完整）。
