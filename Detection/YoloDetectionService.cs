using System;
using System.Collections.Generic;
using System.Threading;
using OpenCvSharp;

namespace YoloDetection
{
    /// <summary>
    /// YOLO 检测服务（IDetectionPipeline 默认实现）。
    ///
    /// 架构：
    ///   ProcessFrame(任意线程) ──克隆──▶ 单槽位帧缓冲 ──Monitor信号──▶ 检测线程
    ///   检测线程：推理 → 后处理 → 发布结果快照 → 可视化 → 触发 FrameProcessed
    ///
    /// 线程安全设计要点：
    ///   1. 所有共享状态由单一 _sync 锁保护；停止标志由 Monitor.Wait/Pulse 协议传递，
    ///      不使用 AutoResetEvent 等 WaitHandle，从根本上消除"句柄已释放"类竞态
    ///   2. 单槽位缓冲：新帧到达时若旧帧尚未被取走则直接丢弃旧帧，保证低延迟且不积压内存
    ///   3. Stop 采用"置位 → PulseAll → 有界 Join"协议；即使极端情况下线程超时未退出，
    ///      也不释放其依赖的托管资源，线程稍后必然自行退出，绝不崩溃
    ///   4. DetectionsUpdated 传出的列表是不可变快照；FrameProcessed 传出的 Mat 归订阅者所有
    /// </summary>
    public class YoloDetectionService : IDetectionPipeline
    {
        private enum PipelineState { Idle, Running, StopPending }

        private readonly object _sync = new object();

        private IYoloDetector _detector;
        private IDetectionVisualizer _visualizer;
        private IDetectionResultProcessor _resultProcessor;

        private Mat _pendingFrame;
        private int _frameWidth;
        private int _frameHeight;
        private List<DetectionResult> _lastDetections = new List<DetectionResult>();

        private PipelineState _state = PipelineState.Idle;
        private Thread _detectionThread;

        // 以下计数器仅在工作线程上读写（单写者），无需同步
        private long _detectCount;
        private long _detectFailCount;

        // ESD 旁路状态：均只在检测线程上读写（单写者），与上面的计数器同一约定
        private long _esdFrameCounter;
        private EsdFrameSnapshot _lastEsdSnapshot;

        public event EventHandler<List<DetectionResult>> DetectionsUpdated;

        public event EventHandler<Mat> FrameProcessed;

        public bool IsRunning
        {
            get
            {
                lock (_sync)
                {
                    return _state != PipelineState.Idle;
                }
            }
        }

        public float ConfidenceThreshold
        {
            get { return _detector != null ? _detector.ConfidenceThreshold : 0.5f; }
            set { if (_detector != null) _detector.ConfidenceThreshold = value; }
        }

        public float NmsThreshold
        {
            get { return _detector != null ? _detector.NmsThreshold : 0.45f; }
            set { if (_detector != null) _detector.NmsThreshold = value; }
        }

        /// <summary>检测结果后处理器（默认：边界裁剪 + 最小尺寸过滤）</summary>
        public IDetectionResultProcessor ResultProcessor
        {
            get { return _resultProcessor; }
            set { _resultProcessor = value ?? new DefaultResultProcessor(); }
        }

        // ==================== 静电接触(ESD)旁路（可选） ====================
        //
        // 三件套均为 null 时完全旁路（零开销，行为与旧版一致）；
        // 全部就位时，检测线程在 YOLO 检测之后顺路执行"姿态推理 → 接触状态机 → 叠加绘制"。
        // ESD 步骤整体 try/catch 兜底：姿态模型/规则引擎出任何问题只记日志，
        // 绝不影响人员检测主链路（现场可以放心开关）。

        /// <summary>姿态检测器（null = 禁用 ESD 旁路；须已完成 Initialize）</summary>
        public IPoseDetector PoseDetector { get; set; }

        /// <summary>静电杆接触分析器（与 PoseDetector 同时配置才生效）</summary>
        public EsdContactAnalyzer EsdAnalyzer { get; set; }

        /// <summary>静电接触叠加渲染器（null = 只算不画；Draw 为原地绘制）</summary>
        public IEsdOverlayRenderer EsdOverlay { get; set; }

        /// <summary>
        /// ESD 分析帧间隔 N：每 N 帧做一次姿态推理（1=每帧）。
        /// CPU 环境下姿态推理较慢时可调大到 2~3 降低占用；
        /// 接触判定基于时间累计（毫秒），降频不影响时长语义。
        /// </summary>
        public int EsdProcessEveryNFrames { get; set; } = 1;

        /// <summary>每帧静电接触快照更新事件（参数为不可变快照；仅在 ESD 启用时触发）</summary>
        public event EventHandler<EsdFrameSnapshot> EsdStatusUpdated;

        /// <summary>
        /// 某人触摸状态翻转事件 (trackId, 是否开始触摸, 累计毫秒)。
        /// 仅在状态变化的那一帧触发一次——UI 日志/报警挂这里，不会刷屏。
        /// </summary>
        public event EventHandler<EsdContactChangedEventArgs> EsdContactChanged;

        // ==================== 门状态监测旁路（可选） ====================
        // 与 ESD 旁路同一模式：分析器+渲染器配齐才启用；整体 try/catch，
        // 任何故障只记日志，绝不影响人员检测主链路。

        /// <summary>门状态监测分析器（null = 禁用门监测旁路）</summary>
        public DoorMonitorAnalyzer DoorAnalyzer { get; set; }

        /// <summary>门状态叠加渲染器（null = 只算不画）</summary>
        public IDoorOverlayRenderer DoorOverlay { get; set; }

        /// <summary>
        /// 门状态翻转事件：true=门被打开，false=门已关闭。
        /// 仅在状态翻转帧触发一次（分析器内部有防抖）。
        /// </summary>
        public event EventHandler<bool> DoorStateChanged;

        /// <summary>每帧门监测快照更新事件（含差异值/遮挡标志；仅在门监测启用时触发）</summary>
        public event EventHandler<DoorFrameSnapshot> DoorStatusUpdated;

        /// <param name="detector">YOLO 检测器实例（必须已完成 Initialize）</param>
        public YoloDetectionService(IYoloDetector detector)
            : this(detector, new OpenCVVisualizer())
        {
        }

        public YoloDetectionService(IYoloDetector detector, IDetectionVisualizer visualizer)
        {
            if (detector == null) throw new ArgumentNullException(nameof(detector));

            _detector = detector;
            _visualizer = visualizer ?? new OpenCVVisualizer();
            _resultProcessor = new DefaultResultProcessor();
        }

        public void Start()
        {
            if (!_detector.IsInitialized)
            {
                throw new InvalidOperationException("YOLO检测器尚未初始化，请先调用 Initialize(modelPath)");
            }

            // 防御：若上一次 Stop 因极端情况超时（StopPending 残留），
            // 先有界等待旧工作线程退出，确保任何时刻最多只有一个检测线程
            Thread previousThread;
            lock (_sync)
            {
                if (_state == PipelineState.Running)
                {
                    return;
                }
                previousThread = _detectionThread;
            }

            if (previousThread != null && previousThread.IsAlive)
            {
                previousThread.Join(TimeSpan.FromSeconds(10));
            }

            Thread thread;
            lock (_sync)
            {
                if (_state == PipelineState.Running)
                {
                    return; // 双重检查（极端并发下防重复启动）
                }

                _state = PipelineState.Running;
                _detectionThread = null;
                thread = new Thread(DetectionLoop)
                {
                    IsBackground = true,
                    Name = "YoloDetectionLoop"
                };
                _detectionThread = thread;
            }

            thread.Start();
        }

        public void Stop()
        {
            Thread thread;
            lock (_sync)
            {
                _state = PipelineState.StopPending;
                Monitor.PulseAll(_sync);
                thread = _detectionThread;
            }

            if (thread != null && thread.IsAlive)
            {
                // 检测循环每次迭代耗时有限（推理通常几十毫秒），正常情况瞬间退出；
                // 10 秒上限仅兜底 native 推理挂死等极端场景。
                // 即使超时也不释放线程依赖的资源，线程稍后必然自行退出，不会崩溃。
                if (!thread.Join(TimeSpan.FromSeconds(10)))
                {
                    LogManager.GeneralLog("[Pipeline] 警告: 检测线程未能在限定时间内退出，将在后台自行结束");
                    return;
                }
            }

            CleanupAfterStopped(thread);
        }

        public void ProcessFrame(Mat frame)
        {
            if (frame == null || frame.Empty())
            {
                return;
            }

            lock (_sync)
            {
                if (_state != PipelineState.Running)
                {
                    return;
                }

                // 单槽位缓冲：覆盖旧帧（若有），保证只处理最新画面且不积压内存
                Mat stale = _pendingFrame;
                _pendingFrame = frame.Clone();
                _frameWidth = frame.Cols;
                _frameHeight = frame.Rows;

                if (stale != null)
                {
                    stale.Dispose();
                }

                Monitor.Pulse(_sync);
            }
        }

        public List<DetectionResult> GetLatestDetections()
        {
            lock (_sync)
            {
                return new List<DetectionResult>(_lastDetections);
            }
        }

        // ==================== 工作线程 ====================

        private void DetectionLoop()
        {
            while (true)
            {
                Mat frame;
                int width, height;

                lock (_sync)
                {
                    // 无帧且未要求停止时挂起等待，零 CPU 占用
                    while (_pendingFrame == null && _state != PipelineState.StopPending)
                    {
                        Monitor.Wait(_sync);
                    }

                    if (_pendingFrame == null)
                    {
                        break; // 收到停止信号且无遗留帧
                    }

                    frame = _pendingFrame;
                    _pendingFrame = null;
                    width = _frameWidth;
                    height = _frameHeight;
                }

                if (frame == null)
                {
                    continue;
                }

                try
                {
                    ProcessSingleFrame(frame, width, height);
                }
                catch (Exception ex)
                {
                    _detectFailCount++;
                    // 异常绝不允许逃逸出工作线程：吞掉并记录，保证管道持续存活
                    LogManager.GeneralLog(
                        $"[Pipeline] 第{_detectCount + 1}帧处理异常(累计失败{_detectFailCount}): {ex.Message}");
                }
                finally
                {
                    frame.Dispose();
                }
            }

            // 退出前清理可能残留的缓冲帧
            lock (_sync)
            {
                if (_pendingFrame != null)
                {
                    _pendingFrame.Dispose();
                    _pendingFrame = null;
                }
            }
        }

        private void ProcessSingleFrame(Mat frame, int width, int height)
        {
            IYoloDetector detector = _detector;

            // 1. 推理
            List<DetectionResult> detections = detector.Detect(frame) ?? new List<DetectionResult>();

            // 2. 后处理
            IDetectionResultProcessor processor = _resultProcessor;
            if (processor != null)
            {
                detections = processor.Process(detections, width, height) ?? new List<DetectionResult>();
            }

            // 3. 静电接触(ESD)旁路（可选）：姿态推理 → 接触状态机。
            //    三件套未配齐或到达降频间隔时跳过；整段 try/catch——
            //    ESD 是附加能力，任何故障只记日志，绝不拖垮人员检测主链路。
            RunEsdAnalysisIfEnabled(frame, detections, width, height);

            // 3.1 门状态监测旁路（可选）：基准比对 → 门开/关状态机。
            //     人员框一并传入用于排除"人走过遮挡门区域"。
            RunDoorMonitorIfEnabled(frame, detections, width, height);

            // 4. 发布结果快照（外部拿到的是独立副本，与内部状态完全隔离：
            //    _lastDetections 与事件列表必须是不同实例，否则外部修改事件参数会污染内部状态）
            var snapshot = new List<DetectionResult>(detections);
            lock (_sync)
            {
                _lastDetections = snapshot;
            }

            _detectCount++;
            if (_detectCount % 100 == 0)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[Pipeline] 已检测 {_detectCount} 帧, 当前{snapshot.Count}个目标, 失败{_detectFailCount}次");
            }

            var handler = DetectionsUpdated;
            if (handler != null)
            {
                handler(this, new List<DetectionResult>(snapshot));
            }

            // 5. 可视化绘制（Draw 返回的新 Mat 所有权移交给订阅者）
            IDetectionVisualizer visualizer;
            lock (_sync)
            {
                visualizer = _visualizer;
            }

            Mat outputFrame = visualizer != null ? visualizer.Draw(frame, detections) : null;
            if (outputFrame == null)
            {
                outputFrame = frame.Clone(); // 绘制失败时回退为原始帧副本
            }

            // 6. ESD 叠加层：在可视化结果之上原地绘制 ROI/状态标签（不改所有权）
            DrawEsdOverlay(outputFrame);

            // 6.1 门状态叠加层：门区域框 + DOOR OPEN/CLOSED 标签
            DrawDoorOverlay(outputFrame);

            var frameHandler = FrameProcessed;
            if (frameHandler != null)
            {
                frameHandler(this, outputFrame);
            }
        }

        /// <summary>
        /// 执行静电接触旁路分析（仅检测线程调用）。
        /// 节流策略：每 EsdProcessEveryNFrames 帧推理一次；跳过帧沿用上次快照供叠加层绘制，
        /// 接触时长语义基于毫秒时间戳，降频不影响判定精度。
        /// </summary>
        private void RunEsdAnalysisIfEnabled(Mat frame, List<DetectionResult> detections, int width, int height)
        {
            IPoseDetector poseDetector = PoseDetector;
            EsdContactAnalyzer analyzer = EsdAnalyzer;

            if (poseDetector == null || !poseDetector.IsInitialized || analyzer == null)
            {
                return;
            }

            _esdFrameCounter++;
            if (EsdProcessEveryNFrames > 1 && _esdFrameCounter % EsdProcessEveryNFrames != 0)
            {
                return; // 降频跳过帧：_lastEsdSnapshot 保持不变，叠加层继续显示上次结论
            }

            try
            {
                List<PoseResult> poses = poseDetector.Detect(frame, detections);

                // 状态翻转先于快照事件发出（UI 先收到"开始触摸"再刷新统计，顺序更符合直觉）
                analyzer.ContactChanged -= OnEsdContactChanged;
                analyzer.ContactChanged += OnEsdContactChanged;

                EsdFrameSnapshot snapshot = analyzer.Update(detections, poses, width, height);

                lock (_sync)
                {
                    _lastEsdSnapshot = snapshot; // 仅供本线程的 DrawEsdOverlay 使用，加锁防读撕裂
                }

                var statusHandler = EsdStatusUpdated;
                if (statusHandler != null)
                {
                    statusHandler(this, snapshot);
                }
            }
            catch (Exception ex)
            {
                LogManager.GeneralLog($"[ESD] 静电接触分析异常(已跳过本帧): {ex.Message}");
            }
        }

        private void OnEsdContactChanged(int trackId, bool inContact, double elapsedMs)
        {
            var handler = EsdContactChanged;
            if (handler != null)
            {
                handler(this, new EsdContactChangedEventArgs(trackId, inContact, elapsedMs));
            }
        }

        /// <summary>
        /// 执行门状态监测旁路（仅检测线程调用）。
        /// 人员框一并传入：门区域被人遮挡时跳过判定（人挡门 ≠ 门开）。
        /// StateChanged 与 ESD 的 ContactChanged 同一接线模式：分析器事件
        /// 经本类转发为 DoorStateChanged（订阅/退订成对，防重复挂接）。
        /// </summary>
        private void RunDoorMonitorIfEnabled(Mat frame, List<DetectionResult> detections, int width, int height)
        {
            DoorMonitorAnalyzer analyzer = DoorAnalyzer;
            if (analyzer == null)
            {
                return;
            }

            try
            {
                analyzer.StateChanged -= OnDoorStateChanged;
                analyzer.StateChanged += OnDoorStateChanged;

                DoorFrameSnapshot snapshot = analyzer.Update(frame, detections, EsdContactAnalyzer.NowMs());

                var statusHandler = DoorStatusUpdated;
                if (statusHandler != null)
                {
                    statusHandler(this, snapshot);
                }
            }
            catch (Exception ex)
            {
                LogManager.GeneralLog($"[Door] 门状态分析异常(已跳过本帧): {ex.Message}");
            }
        }

        private void OnDoorStateChanged(bool isOpen)
        {
            var handler = DoorStateChanged;
            if (handler != null)
            {
                handler(this, isOpen);
            }
        }

        /// <summary>把门状态画到输出帧上（原地绘制；未启用则跳过）。状态直接取分析器当前结论。</summary>
        private void DrawDoorOverlay(Mat outputFrame)
        {
            IDoorOverlayRenderer overlay = DoorOverlay;
            DoorMonitorAnalyzer analyzer = DoorAnalyzer;
            if (overlay == null || analyzer == null)
            {
                return;
            }

            try
            {
                overlay.Draw(outputFrame,
                    new DoorFrameSnapshot { IsOpen = analyzer.IsOpen, HasBaseline = analyzer.HasBaseline },
                    analyzer.Options);
            }
            catch (Exception ex)
            {
                LogManager.GeneralLog($"[Door] 叠加绘制异常: {ex.Message}");
            }
        }

        /// <summary>把最近一次 ESD 快照画到输出帧上（原地绘制；未启用或无快照则跳过）。</summary>
        private void DrawEsdOverlay(Mat outputFrame)
        {
            IEsdOverlayRenderer overlay = EsdOverlay;
            if (overlay == null)
            {
                return;
            }

            EsdFrameSnapshot snapshot;
            lock (_sync)
            {
                snapshot = _lastEsdSnapshot;
            }

            try
            {
                overlay.Draw(outputFrame, snapshot, EsdAnalyzer != null ? EsdAnalyzer.Options : null);
            }
            catch (Exception ex)
            {
                // 绘制失败不影响预览帧继续下发（下一帧还会重试）
                LogManager.GeneralLog($"[ESD] 叠加绘制异常: {ex.Message}");
            }
        }

        private void CleanupAfterStopped(Thread expectedThread)
        {
            lock (_sync)
            {
                if (expectedThread != null && !ReferenceEquals(_detectionThread, expectedThread))
                {
                    // Stop 与新一轮 Start 并发的防御：不要动新线程的状态
                    return;
                }

                if (_state == PipelineState.StopPending || _state == PipelineState.Idle)
                {
                    _state = PipelineState.Idle;
                    _detectionThread = null;

                if (_pendingFrame != null)
                {
                    _pendingFrame.Dispose();
                    _pendingFrame = null;
                }

                _lastDetections.Clear();
                _lastEsdSnapshot = null; // ESD 快照随会话清空，避免下次启动显示旧画面结论
                _esdFrameCounter = 0;
            }
        }
        }

        public void Dispose()
        {
            Stop();
            GC.SuppressFinalize(this);
        }
    }
}
