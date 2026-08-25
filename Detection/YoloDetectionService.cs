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

        /// <summary>
        /// 运行时热切换检测器。
        /// 说明：本方法假定由宿主单线程（UI线程）调用，不做跨线程互斥重入保护。
        /// </summary>
        public void SetDetector(IYoloDetector detector)
        {
            if (detector == null) throw new ArgumentNullException(nameof(detector));

            bool wasRunning = IsRunning;
            if (wasRunning)
            {
                Stop();
            }

            lock (_sync)
            {
                _detector = detector;
            }

            if (wasRunning && detector.IsInitialized)
            {
                Start();
            }
        }

        public void SetVisualizer(IDetectionVisualizer visualizer)
        {
            if (visualizer == null) throw new ArgumentNullException(nameof(visualizer));

            lock (_sync)
            {
                _visualizer = visualizer;
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

            // 3. 发布结果快照（外部拿到的是独立副本，与内部状态完全隔离：
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

            // 4. 可视化绘制（Draw 返回的新 Mat 所有权移交给订阅者）
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

            var frameHandler = FrameProcessed;
            if (frameHandler != null)
            {
                frameHandler(this, outputFrame);
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
