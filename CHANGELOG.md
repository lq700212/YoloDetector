# CHANGELOG — YoloDetector 版本改动记录

> 格式约定：最新版本在最前；写清「改了什么 / 为什么」，重要改动才展开细节。

## v2.7（2026-08-26）未接触灰框默认隐藏（新增 DrawNoContactBoxes 开关）

### 改动范围

**界面优化：预览画面上"未触摸静电杆"人员的灰色 NO GND 整身框改为默认不画**，做成配置开关可随时恢复：

- `Detection/EsdOverlayRenderer.cs`：`DrawNoContactBoxes=false`（新默认）时跳过未接触人员的整身框、"NO GND"标签与手腕落点徽标；接触中的绿色 `ESD OK` 粗框、黄色 ROI 标定框、左下角统计行**不受开关影响始终绘制**
- `Detection/EsdAnalysisOptions.cs` + `Configuration/EsdConfig.cs`：新增 `DrawNoContactBoxes` 选项（默认 false），经 `ToOptions()` 注入模块
- `Detection/esdConfig.json`：新增 `"DrawNoContactBoxes": false` 字段与 `_显示` 说明注释

### 为什么这么改

用户反馈画面"时不时有很多灰色边框的框框，看上去怪怪的"。排查确认这是 ESD 叠加层对每个被跟踪人员画的未接触状态框——但"未触摸"是常态，常驻灰框叠在 YOLO 红框上是纯视觉噪音；且 ESD 快照包含"暂时丢失但仍跟踪中"的人（宽限期内），人离开后灰框还会原地停留约 2 秒，更易被误读成画面多出一个人。ESD 的轨迹跟踪能力本身是判定必需的（Hold 计时/宽限保持都锚定在 TrackId 上），本次只隐藏其可视化，逻辑零改动。需要目检轨迹跟踪效果或核对 ROI 手腕落区时，把 `DrawNoContactBoxes` 改为 true 即恢复旧显示。

### 优化点

- 新增 3 个回归用例（109→112 全绿）：渲染器像素级断言（默认隐藏时未接触框边像素保持背景色、接触绿框不受影响、开关打开恢复灰框）+ 配置缺字段默认 false 与 ToOptions 透传；
- GUI 冒烟通过，检测/判定行为零变化。

## v2.6（2026-08-26）彻底移除 NuGet 依赖（换机编译失败修复）

### 改动范围

**修复：新电脑 `git clone` 后 Visual Studio 编译报"缺少依赖"的问题——主项目最后两个 NuGet 包也 vendor 进仓库，实现真正的零 NuGet 编译**：

- `Detection\libs\` 新增 3 个 DLL（从 NuGet 缓存按对应目标框架复制）：
  - `SunnyUI.dll`（net472）、`SunnyUI.Common.dll`（net472）——界面控件库及其基础库；
  - `Newtonsoft.Json.dll`（net45 目标，net45 组无传递依赖）——配置序列化。
- `YoloDetector.csproj`：删除 `PackageReference Include="Newtonsoft.Json"` 与 `"SunnyUI"` 两条，改为与 OpenCvSharp/OnnxRuntime 相同的 `Reference + HintPath` 模式指向 `Detection\libs\`。

### 为什么这么改

v2.5 及之前虽然检测类库已离线（OpenCvSharp/OnnxRuntime/SkiaSharp 均 HintPath），但宿主仍剩 SunnyUI/Newtonsoft.Json 两个 PackageReference。换电脑克隆代码后，若 NuGet 还原失败（工厂无网/代理拦截/VS 未自动还原），VS 打开即报缺依赖、编译不过。本次把依赖链收干净：SunnyUI→SunnyUI.Common 链条无更深层依赖，Newtonsoft.Json(net45) 无传递依赖，共 3 个 DLL 即全覆盖。已实测验证：**清空 obj/bin 并把全局 NuGet 缓存中这三个包移走后，`dotnet build` 依然 0 警告 0 错误**——任何新机器克隆即编译，与网络和 NuGet 缓存彻底无关。

### 优化点

- 全量回归 109/109 全绿 + GUI 冒烟通过（退出码 0），行为零变化；
- README 技术栈表补全 vendor 状态说明；AGENTS.md 技术栈小节同步。

## v2.5（2026-08-26）ROI 拖拽标定下沉类库（模块迁移零重做）

### 改动范围

**重构：把 ROI 拖拽标定的全部纯逻辑从宿主 UI 层下沉进 Detection 类库**，接入方把库搬到其他项目时标定能力随库走，不再需要重写交互代码：

- **Detection 类库新增**（命名空间 `YoloDetection`，零 System.Drawing/WinForms 依赖，跨平台）：
  - `RoiSelectionState`：拖拽框选状态机（原宿主 `RoiSelectionInteraction` 迁移改造，Point/Rectangle 参数改为 float + 复用 `EsdRoiRect` 通用浮点矩形载体；Press/Drag/Release 契约与误触 <5px 忽略语义不变）
  - `ZoomMapping`：Zoom(letterbox) 坐标换算（原宿主 `PictureBoxZoomMath` 迁移改造），并新增 `TryMapDragToNormalizedRoi`——一次拖拽端到端换算归一化 ROI（两端点映射→包围盒→最小面积→贴边回收），把宿主原来散在 MouseUp 里的业务换算也收编为可测纯函数
- **宿主新增** `UI/RoiSelectionPictureBox.cs`：自带标定能力的预览控件（WinForms 薄壳，override 封装鼠标接线/虚线框绘制/坐标换算），接入方**拖上窗体 + 订阅 `RoiSelected` 事件**即完成接入（事件吐归一化 ROI，热更新/持久化由订阅方决定）
- **宿主删除** `UI/PictureBoxZoomMath.cs`、`UI/RoiSelectionInteraction.cs`（逻辑已下沉）；MainForm 删掉 4 个鼠标/Paint 处理器与状态机字段，改为订阅 `RoiSelected` 一行接线；Layout 的 videoPictureBox 换用新控件类型
- **文档**：`docs/MODULE.md` 文件清单补全 ESD/姿态/标定套件，新增"3.1 静电杆 ROI 拖拽标定（WinForms 傻瓜接入，约 5 行）"章节（含非 WinForms 宿主用纯逻辑类自接线的说明）；AGENTS.md 导航表同步
- **测试**：RoiSelectionTests 分区改为直接测类库类型（引用 YoloDetector.UI → YoloDetection），新增拖拽端到端换算用例（正常/贴边回收/反向/无效图像），97→109 用例全绿；SKILL.md 对账表同步

### 为什么这么改

用户反馈：后续要把检测库接入其他项目，ROI 拖拽标定如果留在宿主 UI 层就得每个项目重做一遍。本次把"能随库走的"（状态机、坐标换算——纯逻辑零依赖）全部下沉类库，宿主只留一个必须依赖 WinForms 的薄壳控件（单文件可复制）；非 WinForms 宿主也能直接用类库纯逻辑自行接线。接入成本从"重写整套交互"降到"复制一个控件文件 + 订阅一个事件"。

### 优化点

- `TryMapDragToNormalizedRoi` 收编了宿主 MouseUp 里的换算业务（包围盒/最小 0.01 面积/贴边回收），该逻辑此前无测试覆盖，现在被 4 个断言锁定；
- MainForm 视图层进一步变薄（删约 90 行交互代码），符合"主文件只放业务逻辑"约定；
- 全量回归 109/109 全绿 + GUI 冒烟通过。

## v2.4（2026-08-26）静电杆 ROI 拖拽标定（所见即所得）

### 改动范围

**新功能：预览画面上按住鼠标左键拖拽框选静电杆区域，松手即完成 ROI 标定**——运行链路下一帧立即生效 + 自动写回 `Detection/esdConfig.json`，取代旧版"手改 JSON 四个比例值 → 重启预览 → 目检黄框 → 再改"的反复试错流程：

- **UI 层新增两个纯逻辑类（可单测，不碰控件）**：
  - `UI/PictureBoxZoomMath.cs`：PictureBox Zoom 模式坐标换算（显示区 letterbox 计算 + 控件点→图像归一化坐标，黑边/出界夹紧到 [0,1]）；
  - `UI/RoiSelectionInteraction.cs`：拖拽框选状态机（Press/Drag/Release，反向拖拽规范化，<5px 判误触忽略）；
- **UI 接线**：`MainForm` 四个事件处理器（MouseDown/Move/Up/Paint）只做一行转发；拖拽中 Paint 叠加黄色虚线框（与运行期 ESD 黄框同色系）；松手后先 `TryUpdateEsdRoi` 热更新再 `SaveEsdRoi` 落盘，日志面板报告结果；`videoPictureBox` 光标改 Cross 提示可框选；启动提示语补充标定说明
- **App 层**：`VideoDetectionController.TryUpdateEsdRoi`——找到运行中管道的 `EsdAnalyzer.Options` 就地夹紧写入（分析器与叠加层每帧读同一实例 → 下一帧生效，无需重建链路）；ESD 未启用时返回 false，调用方仍保存配置供下次启用
- **Configuration 层**：`EsdConfig.ApplyNormalizedRoi`（内存单例热更新）+ `EsdConfig.UpdateRoiJson`（**JObject 局部更新四个字段，保留 "_说明"/"_现场标定" 等模型外中文注释字段与字段顺序**——整体序列化会抹掉现场依赖的调参指南）；`AppConfig.SaveEsdRoi` 双通道更新（内存单例 + 文件写回，UTF-8 无 BOM；文件缺失/损坏退化为整对象序列化重建）
- **Detection 层**：`EsdAnalysisOptions.ApplyNormalizedRoi`（模块侧安全更新入口，夹紧语义与 ToOptions 一致；注释写明 UI 线程写/检测线程读的弱一致取舍）
- **配置说明**：`Detection/esdConfig.json` 的 `_现场标定` 说明更新为拖拽标定优先（参数值未动）
- **回归体系 97→108 用例**：新增 RoiSelectionTests 分区（Zoom 显示矩形居中/点映射与黑边夹紧/无效尺寸/框选状态机正常流·误触·反向·未按下忽略，7 例）；ConfigTests 补 UpdateRoiJson 保留注释/缺失字段补建与坏 JSON 兜底/ApplyNormalizedRoi 夹紧 3 例；EsdAnalyzerTests 补 Options 热更新 ROI 夹紧且分析器立即可见 1 例；SKILL.md 对账表同步；新增 STA 目检探针 `Invoke-RoiDragVisualCheck.ps1`（反射驱动框选 + 截图目检虚线框）

### 为什么这么改

用户反馈手改 JSON 标定 ROI 需要反复试错（改值→重启→目检→再改）。归一化 ROI 本质是"画面上的一块矩形"，直接在画面上拖出来是最自然的交互；热更新通道（分析器每帧读同一 Options 实例）让标定结果即时可见，配合运行期 ESD 黄框形成"拖→看→微调"的闭环。JSON 写回坚持局部更新是为了保住现场依赖的中文调参指南注释。

### 优化点

- 坐标换算与框选状态机抽成纯函数类，MainForm 事件层零逻辑（GUI 冒烟兜底整体链路，单测锁定数学）；
- `Release` 契约收紧：返回 false（误触）时输出矩形一律为 Empty，调用方免判幅度；
- 开发中被用例当场暴露 2 处笔误（夹紧后贴边断言期望值写错、框选宽高断言写反），修复后全绿。

## v2.3（2026-08-26）技术分享文档《人员检测与人手动作检测实现详解》

### 改动范围

新增 `docs/技术分享-人员检测与人手动作检测实现详解.md`（约 340 行，面向小白的完整原理讲解，作为技术分享会学习材料）。内容全部取材于现有代码与既有文档，无任何代码改动：

- **人员检测五步流水线**：letterbox 预处理（含 ASCII 图示与坐标还原公式）、ONNX 推理、双格式输出解析、过滤去重链（类别→置信度→边界→NMS→Top5），附"逐像素 P/Invoke 30~80ms → 整块拷贝 1~3ms"性能对比；
- **人手动作检测**：技术选型对比表（为何不用动作分类模型而用"手腕关键点+区域规则"）、COCO 17 关键点 ASCII 骨架图、逐人裁剪扩边推理的取舍原因、最近邻跨帧跟踪、接触状态机四阶段图解（Hold/Grace/Forget/Margin 各自防什么干扰）；
- **串联关系**：检测线程内一帧的七步完整时序图、"三级接力"数据流解释、ESD 旁路三道防线（零开销/启动降级/运行跳帧）、事件出口一览；
- **界面绘制**：两层叠加绘制（Skia 红框层 + OpenCV 原地 ESD 层）及分层理由、Mat→SKBitmap→Bitmap→PictureBox 显示链路与资源交接、标签英文的原因、单槽位丢帧保实时的原理；
- **工程细节**：三线程模型、停止协议、Mat 所有权生死簿、性能优化清单表、配置体系、分层架构图、已知限制；
- **附录**：文件索引、分享会 FAQ 六问预演（含"CPU 能跑吗""内存会不会越用越多"等高频问题）。

### 为什么这么改

用户要开技术分享会，需要一份小白能看懂、又能支撑深度提问的讲解材料。现有 `ARCHITECTURE.md` 是面向维护者的浓缩速查，不适合从零学起的人；本文按"全景→名词→人的检测→手部动作→怎么串起来→框怎么画→工程细节"的递进结构重讲一遍，所有数字/公式/行为均核对源码后撰写，并预置 FAQ 供分享会答辩使用。

### 优化点

同步更新 README.md（开发者文档表格 + 目录结构 docs 描述）与 AGENTS.md（docs 文档清单）；编码自查通过（UTF-8 中文完整命中）。

## v2.2（2026-08-26）静电杆触摸检测（姿态关键点 + 区域规则）

### 改动范围

**新功能：在人员检测基础上叠加"手部动作识别"——判定画面中的人是否正在触摸静电杆**（工厂防静电场景）。技术路线为「YOLO-pose 人体关键点 + 静电杆 ROI 几何规则」，不引入第二个黑盒模型做动作分类，判定完全可解释、阈值全部可配置：

- **Detection 类库新增**（命名空间 `YoloDetection`，零宿主依赖，可随模块整体迁移）：
  - `YoloPoseDetector` / `IPoseDetector`：对已检出的人体框逐人裁剪扩边 → letterbox → 推理 → 解析 COCO 17 关键点（含左右手腕 idx 9/10）→ 坐标还原原图。自动兼容 `[1,C,N]`/`[1,N,C]` 双输出布局与动态维度探测；单帧最多推理 `MaxPersonsPerFrame`(默认8) 人防拥挤卡顿
  - `EsdContactAnalyzer`：接触状态机——手腕落 ROI(含 MarginPx 容差) 持续 ≥`HoldDurationMs` 判定"正在触摸"；短暂丢失 `ReleaseGraceMs` 内不断开（防遮挡抖动）；跨帧按人体框中心贪心最近邻跟踪并分配自增 TrackId；人离场超时轨迹遗忘、重新计时。纯逻辑无 IO，虚拟时钟可单测
  - `EsdOverlayRenderer` / `IEsdOverlayRenderer`：OpenCV 原地绘制 ROI 黄框("ESD POLE")、接触者绿框("ESD OK")/未接触灰框、手腕落点徽标、底部统计行（英文标签规避 PutText 中文乱码）
  - 配套模型类：`PoseResult`/`PoseKeypoint`/`CocoKeyPointIndexes`、`EsdAnalysisOptions`、`EsdPersonStatus`/`EsdFrameSnapshot`（不可变快照）、`EsdContactChangedEventArgs`、`EsdRoiRect`
- **检测管道可选旁路**：`YoloDetectionService` 新增 `PoseDetector`/`EsdAnalyzer`/`EsdOverlay` 三件套属性与 `EsdStatusUpdated`/`EsdContactChanged` 事件——三件套未配齐时**完全旁路零开销**（行为与旧版一致）；配齐后在 YOLO 检测后顺路执行"姿态→状态机"，整段 try/catch 兜底，ESD 故障绝不影响人员检测主链路；支持 `EsdProcessEveryNFrames` 降频（时长判定基于毫秒时间戳，降频不影响精度）
- **编排层**：`VideoDetectionController` 姿态检测器跨会话复用（同主检测器的 Ensure 模式）；启动装配失败只降级为纯人员检测并告警，不让预览启动失败；`EsdContactChanged` 转发给宿主
- **宿主配置**：新增 `Configuration/EsdConfig.cs` + `Detection/esdConfig.json`（ROI 归一化坐标、Hold/Grace 时长、容差、置信度阈值等全配置化，`ToOptions()` 就地夹紧非法值），AppConfig 同模式加载
- **UI**：触摸状态翻转时日志面板提示一次（"人员#N 正在/结束触摸静电杆"），不刷屏
- **模型获取脚本** `tools/download_pose_model.py`：多级回退（ONNX 直链候选 → 官方 .pt 权重 + ultralytics 官方 API 导出），自动读取 Windows 系统代理（VPN"系统代理"模式下 Python urllib 默认不走代理会全部超时的坑已内置处理）；产出 `Detection/model/yolo11n-pose.onnx`(11.3MB, 输入640x640/输出[1,56,8400]/17关键点) 已入 git，克隆即用
- **回归体系扩充 70→97 用例**：新增 PoseTests 分区（契约 + 合成图 + **bus.jpg 官方真图端到端对照：检人4人→4人完整17点→手腕坐标落位，与 Python ultralytics 基准一致**）；EsdAnalyzerTests 分区（虚拟时钟驱动状态机全分支 11 例 + 叠加渲染器契约 2 例 + 管道 ESD 旁路集成 3 例）；ConfigTests 补 EsdConfig 现场加载与 ToOptions 非法值夹紧 2 例；EndToEndTests 补"姿态模型缺失自动降级为纯人员检测"与"带 ESD 旁路视频流全链路"2 例；SKILL.md 对账表同步

### 为什么这么改

用户需求："后续要在识别到人的基础上对手部动作进行识别，场景是工厂里，识别有没有摸静电杆的动作"。选型说明：时序动作识别模型（ST-GCN 等）部署重、不可解释、无现成 ONNX；工业防静电监测的成熟做法就是"手腕关键点 + 静电杆区域 + 持续时长"规则引擎——复用现有人体检测链路（pose 只对人框小图推理，省算力且天然归属到人），现场只需按摄像头视野标定一次 ROI 黄框即可使用。模型下载按用户要求做成 Python 脚本全自动完成（官方 .pt 正源 + 官方 API 导出，不用来路不明的第三方 onnx）。

### 修复（开发中被新用例当场暴露，均已修）

- **EsdContactAnalyzer 轨迹遗忘顺序 bug**：原实现"先匹配后遗忘"，长时间离场的人一回来就被旧轨迹近邻吸走，永远走不到遗忘分支，"重新计时"语义失效——改为先遗忘后匹配
- **dt 异常钳制过紧**：上限 1000ms 会把"降频 × 低帧率 RTSP"叠加出的合法帧间隔误判为异常清零接触累计——放宽至 5 秒并注释原因
- **结束触摸事件时长恒为 0**：事件在累计清零后才触发，日志只能打出"持续0秒"——改为携带清零前的最终时长

### 优化点

- 姿态预处理与主检测器同一套高性能套路（Marshal.Copy 整块拷贝 + 单层循环填 CHW），刻意不抽公共类避免触碰已验证代码
- ESD 叠加走 OpenCV 原地矢量绘制（微秒级），不走 Skia 位图往返（约 10ms），预览帧零额外拷贝
- 测试沉淀新增踩坑：harness 引用 bin 下 DLL——改主项目代码必须先重建主项目再跑 Tests.exe，否则跑旧逻辑出现"修了还 FAIL"假象；归一化坐标换算断言禁用 float 精确相等（0.3f×800≠240f 的尾差坑）

## v2.1（2026-08-25）全量回归验证体系（skill 化）

### 改动范围

- 新增项目专属 skill `.opencode/skill/全量回归验证/`：一键回归验证体系，`scripts\Run-AllTests.ps1` 总入口 = 构建主项目 + 70 个进程内回归用例 + GUI 进程级冒烟，退出码 0 即全绿
  - **进程内 harness**（`harness\*.cs`，自研微型断言框架、零外部测试依赖）：配置加载/损坏回退、Mat↔SKBitmap 像素级无损往返（含 1080P/奇数尺寸）、宿主位图转换 SkBitmapExtensions（Bgra8888 错位回归防线）、后处理器行为红线锁定（边界裁剪公式/10×20 最小尺寸）、可视化器契约（不污染原帧/null 契约/工厂类型）、YoloV26Detector 真实模型推理契约（异常路径/坐标合法性/确定性/Dispose 协议）、检测管道线程协议（快照隔离/异常零逃逸/单槽位缓冲/停止协议）、帧源生命周期（本地视频文件充当流源 + RTSP 拒绝连接失败路径）、VideoDetectionController 端到端全链路、相机控制器未连接契约与工厂、安格华客户端契约（快速失败/有界超时）与 DeviceStatus 计算、日志门面三通道独立开关、文件日志契约、MainForm STA 构造冒烟
  - **GUI 冒烟脚本**：启动 exe → 存活观察 → 关窗退出码校验 → 日志"程序启动/退出"配对检查
  - SKILL.md 内含**模块↔用例对账表**：14 个源文件模块逐一核对覆盖，未覆盖边界（UI 私有交互方法/工厂品牌回退分支）注明原因
- AGENTS.md 新增「测试沉淀红线」：今后凡新写测试用例/冒烟步骤/调试探针，必须沉淀进该 skill 并跑通全绿才算完成（AI 自觉执行，无需用户提醒）

### 为什么这么改

用户要求软件绝对稳定、0 Bug，且测试资产要可复用、不重复造轮子。本项目坚持"克隆即离线编译"，故不引入 xUnit/NUnit，采用自研轻量 harness（编译产物输出到主 bin，就地使用现场配置与真实 ONNX 模型）；端到端用本地生成的 MJPG 视频文件充当流源，无相机环境也能跑通全链路。所有测试资产集中沉淀在 skill 目录，后续新增用例有固定归档流程。

### 修复（均由本回归体系首轮运行暴露）

- **`App/SkBitmapExtensions.ToDrawingBitmap` 预览花屏（P0，用户可见）**：上游 `MatToSKBitmap` 产出的是 Bgra8888(32bpp)（Skia 根本没有 24bpp 格式），旧实现误按 24bpp 逐行拷贝，源图从第 2 个像素起整体错位 → 视频预览画面必然花屏。现按 Bgra8888 输入做压缩拷贝（跳过 alpha + 行对齐处理），像素级回归防线锁定。1080P 约 2~5ms，仍远低于帧间隔
- **`Detection/YoloV26Detector.Initialize`**：已释放检查（ThrowIfDisposed）从文件存在性校验之后提前到方法入口——此前 Dispose 后调用会误抛 FileNotFoundException 而非语义正确的 ObjectDisposedException，误导排障方向；与 Detect 方法保持同一模式。正常使用路径行为不变

### 优化点

- 首轮 harness 运行即暴露上述两个真实缺陷并修复；用例编写踩坑（CountNonZero 单通道限制、fake 框尺寸需过最小尺寸过滤、读日志须共享打开、UiSmokeTests 必须最后跑等）已沉淀进 skill 的 SKILL.md

## v2.0（2026-08-24）架构重构 + 界面改版 + 走查修复

相对初始功能版的完整变化，分三部分：

### 架构重构与稳定性治理

- 目录重组为五层架构：`UI`（视图）/ `App`（编排）/ `Detection`（检测域）/ `Cameras`（相机域）/ `Configuration` + `Infrastructure`（配置与基础设施），依赖方向单向
- **检测模块拆分为独立类库** `YoloDetector.Detection.dll`（命名空间 `YoloDetection`，工程 `Detection/YoloDetector.Detection.csproj`）：不依赖宿主业务代码，日志/配置全部委托注入，整个目录复制 + 项目引用即可迁移到其他项目（接入指南见 docs/MODULE.md）
- **类库多目标跨平台**：`net472` + `netstandard2.0` 两目标能力完全一致（无条件编译差异）——位图后端统一 SkiaSharp（Google Skia 跨平台封装），MatToSKBitmap/SKBitmapToMat 无损互转（往返像素差=0，1080P 约 5ms）、YoloBuiltin 可视化器 SKCanvas 绘制效果与原 GDI+ 版一致，Windows/Linux/macOS 使用方式与效果完全相同
- **离线编译与部署**：托管依赖与 native 运行库（Windows + Linux 双平台共约 201MB）全部 vendor 入 git——克隆即完整、编译运行零网络依赖，Windows/Linux 双平台开箱可用，部署机只需 bin 目录整包拷贝（工厂无网环境友好）；`tools/collect-native.ps1` 仅在更换依赖版本后重新收集时使用
- **Linux 兼容收尾**：清理 VisualizerFactory 的条件编译残留（旧方案会让 netstandard2.0 目标抛 NotSupportedException，与"全平台能力一致"矛盾）；补齐 Linux native（libOpenCvSharpExtern.so 72MB、libonnxruntime.so 16MB，OpenCV Linux 采用与托管层同版本的 unofficial 构建保证 ABI 匹配）
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
