using System;
using System.Collections.Generic;
using YoloDetector.Infrastructure.Logging;

namespace YoloDetector.App
{
    /// <summary>
    /// 视频检测控制器的启动参数。
    /// </summary>
    public sealed class DetectionStartupOptions
    {
        /// <summary>ONNX 模型路径</summary>
        public string ModelPath { get; set; }

        public float ConfidenceThreshold { get; set; } = 0.5f;

        public float NmsThreshold { get; set; } = 0.45f;

        /// <summary>是否开启 YOLO 详细调试日志</summary>
        public bool YoloDebugLog { get; set; }

        /// <summary>是否开启每帧检测结果日志</summary>
        public bool DetectionResultLog { get; set; }

        /// <summary>
        /// 检测模块日志的 UI 回调（宿主注入）。
        /// 检测线程的异常/过程日志除写文件外，经此回调同步显示到界面日志面板；
        /// 回调在后台线程触发，宿主实现必须自行做线程调度（如 SafeBeginInvoke）。可为 null。
        /// </summary>
        public Action<string> LogSink { get; set; }

        public YoloDetection.VisualizerType VisualizerType { get; set; } = YoloDetection.VisualizerType.YoloBuiltin;

        /// <summary>RTSP 流地址</summary>
        public string RtspUrl { get; set; }

        /// <summary>
        /// 姿态模型路径（YOLO-pose onnx）。与 EsdOptions 同时提供且 EsdOptions.Enabled=true 时，
        /// 启动静电杆触摸检测旁路；任一缺失则维持纯人员检测行为。
        /// </summary>
        public string PoseModelPath { get; set; }

        /// <summary>
        /// 静电接触检测参数（null 或 Enabled=false 时禁用）。
        /// 由宿主从 Detection/esdConfig.json 转换而来。
        /// </summary>
        public YoloDetection.EsdAnalysisOptions EsdOptions { get; set; }

        /// <summary>
        /// 门状态监测参数（null 时禁用门监测旁路）。
        /// 由宿主从 Detection/doorConfig.json 转换而来。
        /// </summary>
        public YoloDetection.DoorMonitorOptions DoorOptions { get; set; }
    }

    /// <summary>
    /// 视频检测编排器：统一管理"帧源(RTSP捕获) → 检测管道 → 结果输出"的完整生命周期。
    ///
    /// 设计要点：
    ///   1. 帧所有权完全收拢在本类内部：FrameReady 的 Mat 由本类负责 Dispose，
    ///      FrameProcessed 的 Mat 由本类转成 Bitmap 后立即 Dispose，UI 层零 Mat 接触，
    ///      从根本上杜绝 UI 层忘记释放导致的内存泄漏
    ///   2. ONNX 模型实例跨会话复用（首次加载耗时秒级，重复启停预览无需重新加载）
    ///   3. UI 通过构造注入的两个回调接收结果：
    ///      - previewSink(Bitmap)：Bitmap 所有权移交 UI（UI 替换显示时应 Dispose 旧图）
    ///      - detectionSink(快照列表)：结果为不可变快照，可自由持有
    ///      注意：两个回调均在后台线程触发，UI 侧实现必须自行做线程调度与防崩溃保护
    /// </summary>
    public sealed class VideoDetectionController : IDisposable
    {
        private readonly Action<System.Drawing.Bitmap> _previewSink;
        private readonly Action<List<YoloDetection.DetectionResult>> _detectionSink;

        private YoloDetection.IYoloDetector _detector;
        private YoloDetection.IPoseDetector _poseDetector;
        private YoloDetection.IDetectionPipeline _pipeline;
        private YoloDetection.IFrameSource _frameSource;

        /// <summary>
        /// 某人触摸静电杆状态翻转事件 (trackId, 是否开始触摸, 累计毫秒)。
        /// 仅在状态变化帧触发一次；后台线程触发，订阅方自行调度。
        /// </summary>
        public event EventHandler<YoloDetection.EsdContactChangedEventArgs> EsdContactChanged;

        /// <summary>
        /// 门状态翻转事件：true=门被打开，false=门已关闭。
        /// 仅在状态变化帧触发一次（分析器内部防抖）；后台线程触发，订阅方自行调度。
        /// </summary>
        public event EventHandler<bool> DoorStateChanged;

        private void HandlePipelineDoorChanged(object sender, bool isOpen)
        {
            var handler = DoorStateChanged;
            if (handler != null)
            {
                handler(this, isOpen);
            }
        }

        public VideoDetectionController(
            Action<System.Drawing.Bitmap> previewSink,
            Action<List<YoloDetection.DetectionResult>> detectionSink)
        {
            _previewSink = previewSink ?? throw new ArgumentNullException(nameof(previewSink));
            _detectionSink = detectionSink ?? throw new ArgumentNullException(nameof(detectionSink));
        }

        /// <summary>检测管道是否正在运行</summary>
        public bool IsRunning
        {
            get { return _pipeline != null && _pipeline.IsRunning; }
        }

        /// <summary>检测器是否已完成初始化（模型已加载）</summary>
        public bool IsDetectorReady
        {
            get { return _detector != null && _detector.IsInitialized; }
        }

        /// <summary>启动完整检测链路。失败时抛异常并保证内部状态干净。</summary>
        public void Start(DetectionStartupOptions options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (string.IsNullOrEmpty(options.RtspUrl))
                throw new ArgumentException("RTSP地址不能为空", nameof(options));

            // 先停掉可能存在的旧会话，保证幂等
            Stop();

            // 1. 日志开关与输出通道（模块化约定：检测模块本身不落地日志，
            //    文件通道由宿主注入 Logger.Write，UI 通道经 options.LogSink 注入）
            YoloDetection.LogManager.Initialize(
                enableYoloLog: options.YoloDebugLog,
                enableGeneralLog: true,
                enableDetectionResultLog: options.DetectionResultLog,
                outputSink: Logger.Write,
                uiSink: options.LogSink);

            // 2. 检测器（跨会话复用已初始化的实例，避免重复加载模型）
            EnsureDetector(options);

            // 3. 可视化器 + 检测管道
            var visualizer = YoloDetection.VisualizerFactory.Create(options.VisualizerType);
            var pipeline = new YoloDetection.YoloDetectionService(_detector, visualizer)
            {
                ConfidenceThreshold = options.ConfidenceThreshold,
                NmsThreshold = options.NmsThreshold
            };

            // 3.1 静电接触(ESD)旁路装配：参数与姿态模型路径齐备才启用
            //     （Enabled 开关由宿主处理——关闭时宿主直接传 null）。
            //     装配失败（如姿态模型文件缺失）只降级为纯人员检测并告警，
            //     不让整个预览启动失败——人员检测是主业务，不能被附加功能拖死
            bool esdEnabled = false;
            if (options.EsdOptions != null && !string.IsNullOrEmpty(options.PoseModelPath))
            {
                try
                {
                    EnsurePoseDetector(options);
                    pipeline.PoseDetector = _poseDetector;
                    pipeline.EsdAnalyzer = new YoloDetection.EsdContactAnalyzer(options.EsdOptions);
                    if (options.EsdOptions.DrawOverlay)
                    {
                        pipeline.EsdOverlay = new YoloDetection.EsdOverlayRenderer();
                    }
                    pipeline.EsdProcessEveryNFrames = 1; // 节流由宿主配置预留，当前每帧分析
                    esdEnabled = true;
                }
                catch (Exception ex)
                {
                    YoloDetection.LogManager.GeneralLog(
                        $"[ESD] 静电接触检测启用失败，已降级为纯人员检测: {ex.Message}");
                }
            }

            if (esdEnabled)
            {
                pipeline.EsdContactChanged += HandlePipelineEsdChanged;
            }

            // 3.2 门状态监测旁路装配（与 ESD 同一模式：null 禁用，故障只降级）
            if (options.DoorOptions != null)
            {
                try
                {
                    pipeline.DoorAnalyzer = new YoloDetection.DoorMonitorAnalyzer(options.DoorOptions);
                    if (options.DoorOptions.DrawOverlay)
                    {
                        pipeline.DoorOverlay = new YoloDetection.DoorOverlayRenderer();
                    }
                    pipeline.DoorStateChanged += HandlePipelineDoorChanged;
                }
                catch (Exception ex)
                {
                    YoloDetection.LogManager.GeneralLog(
                        $"[Door] 门状态监测启用失败，已降级为无门监测: {ex.Message}");
                }
            }

            pipeline.DetectionsUpdated += OnDetectionsUpdated;
            pipeline.FrameProcessed += OnFrameProcessed;
            pipeline.Start();

            // 4. 帧源最后启动（管道已就绪，避免开头几帧因管道未运行被丢弃）
            var frameSource = new YoloDetection.RtspFrameCapturer();
            frameSource.FrameReady += OnFrameReady;

            if (!frameSource.Start(options.RtspUrl))
            {
                frameSource.Dispose();
                pipeline.DetectionsUpdated -= OnDetectionsUpdated;
                pipeline.FrameProcessed -= OnFrameProcessed;
                pipeline.Dispose();
                throw new InvalidOperationException("RTSP流连接失败，请检查流地址与相机配置");
            }

            _pipeline = pipeline;
            _frameSource = frameSource;
        }

        /// <summary>停止检测链路（幂等）。检测器保留以供下次复用。</summary>
        public void Stop()
        {
            if (_frameSource != null)
            {
                _frameSource.FrameReady -= OnFrameReady;
                _frameSource.Dispose();
                _frameSource = null;
            }

            DisposeLastFrameForDoor();

            if (_pipeline != null)
            {
                _pipeline.DetectionsUpdated -= OnDetectionsUpdated;
                _pipeline.FrameProcessed -= OnFrameProcessed;

                var esdService = _pipeline as YoloDetection.YoloDetectionService;
                if (esdService != null)
                {
                    esdService.EsdContactChanged -= HandlePipelineEsdChanged;
                    esdService.DoorStateChanged -= HandlePipelineDoorChanged;
                }

                _pipeline.Dispose();
                _pipeline = null;
            }
        }

        public void Dispose()
        {
            Stop();

            if (_detector != null)
            {
                _detector.Dispose();
                _detector = null;
            }

            // 姿态检测器同样跨会话复用，仅在控制器整体销毁时释放
            if (_poseDetector != null)
            {
                _poseDetector.Dispose();
                _poseDetector = null;
            }
        }

        // ==================== 内部数据流转 ====================

        private void EnsureDetector(DetectionStartupOptions options)
        {
            if (!System.IO.File.Exists(options.ModelPath))
            {
                throw new System.IO.FileNotFoundException("YOLO模型文件不存在", options.ModelPath);
            }

            if (_detector == null || !_detector.IsInitialized)
            {
                if (_detector != null)
                {
                    _detector.Dispose();
                }

                var detector = new YoloDetection.YoloV26Detector();
                try
                {
                    detector.Initialize(options.ModelPath);
                }
                catch
                {
                    detector.Dispose();
                    throw;
                }
                _detector = detector;
            }

            _detector.ConfidenceThreshold = options.ConfidenceThreshold;
            _detector.NmsThreshold = options.NmsThreshold;
        }

        /// <summary>
        /// 姿态检测器的创建与跨会话复用（与 EnsureDetector 同一模式：
        /// 模型加载秒级，反复启停预览不重复加载）。
        /// 模型文件不存在时抛 FileNotFoundException，由 Start 的 try/catch 降级处理。
        /// </summary>
        private void EnsurePoseDetector(DetectionStartupOptions options)
        {
            if (!System.IO.File.Exists(options.PoseModelPath))
            {
                throw new System.IO.FileNotFoundException("姿态模型文件不存在", options.PoseModelPath);
            }

            if (_poseDetector == null || !_poseDetector.IsInitialized)
            {
                if (_poseDetector != null)
                {
                    _poseDetector.Dispose();
                }

                var poseDetector = new YoloDetection.YoloPoseDetector();
                try
                {
                    poseDetector.Initialize(options.PoseModelPath);
                }
                catch
                {
                    poseDetector.Dispose();
                    throw;
                }
                _poseDetector = poseDetector;
            }

        }

        /// <summary>帧源出帧 → 提交管道检测。Mat 所有权在本方法内终结。</summary>
        private void OnFrameReady(object sender, OpenCvSharp.Mat frame)
        {
            try
            {
                var pipeline = _pipeline;
                if (pipeline != null && pipeline.IsRunning)
                {
                    // 门基准采集缓存：每 10 帧克隆一份最新帧（约 2ms，低频无感），
                    // 供 UI"重设门基准"按钮使用（点击时原帧早已释放，必须有缓存）
                    _doorFrameCounter++;
                    if (_doorFrameCounter >= 10)
                    {
                        _doorFrameCounter = 0;
                        UpdateLastFrameForDoor(frame);
                    }

                    pipeline.ProcessFrame(frame); // 管道内部克隆，此处原帧随后释放
                }
            }
            finally
            {
                frame.Dispose();
            }
        }

        // 门基准采集帧缓存计数（仅帧源线程读写，单写者无需同步）
        private int _doorFrameCounter;

        /// <summary>克隆最新帧到门基准缓存（仅当门监测链路存在时；否则清掉缓存省内存）。</summary>
        private void UpdateLastFrameForDoor(OpenCvSharp.Mat frame)
        {
            var esdService = _pipeline as YoloDetection.YoloDetectionService;
            if (esdService == null || esdService.DoorAnalyzer == null)
            {
                DisposeLastFrameForDoor();
                return;
            }

            OpenCvSharp.Mat old = _lastFrameForDoor;
            _lastFrameForDoor = frame.Clone();
            if (old != null)
            {
                old.Dispose();
            }
        }

        /// <summary>管道出帧 → 转 Bitmap 后交 UI 显示。Mat 在此立即释放。</summary>
        private void OnFrameProcessed(object sender, OpenCvSharp.Mat frame)
        {
            System.Drawing.Bitmap bitmap = null;
            try
            {
                // 类库统一输出 SKBitmap（跨平台）；Windows 宿主在此边界转换为
                // System.Drawing.Bitmap 供 PictureBox 显示（24bpp BGR 整块拷贝，约 0.5ms）
                using (var skb = YoloDetection.MatExtensions.MatToSKBitmap(frame))
                {
                    if (skb != null)
                    {
                        bitmap = skb.ToDrawingBitmap();
                    }
                }
            }
            finally
            {
                frame.Dispose();
            }

            if (bitmap != null)
            {
                _previewSink(bitmap); // 所有权移交给 UI
            }
        }

        private void OnDetectionsUpdated(object sender, List<YoloDetection.DetectionResult> detections)
        {
            _detectionSink(detections);
        }

        /// <summary>管道 ESD 翻转事件 → 直接转发给宿主订阅者（快照参数不可变，可安全透传）。</summary>
        private void HandlePipelineEsdChanged(object sender, YoloDetection.EsdContactChangedEventArgs e)
        {
            var handler = EsdContactChanged;
            if (handler != null)
            {
                handler(this, e);
            }
        }

        /// <summary>
        /// 运行期热更新静电杆 ROI（UI 拖拽标定调用）：归一化坐标就地夹紧，
        /// 下一帧立即生效（分析器与叠加层每帧读取同一 Options 实例）。
        ///
        /// 返回 false 表示当前没有可更新的 ESD 链路（预览未启动，或静电触摸检测
        /// 未启用/装配降级）——此时调用方仍应把值保存到配置文件，下次启用即生效。
        /// </summary>
        public bool TryUpdateEsdRoi(float roiX, float roiY, float roiW, float roiH)
        {
            var esdService = _pipeline as YoloDetection.YoloDetectionService;
            YoloDetection.EsdAnalysisOptions options =
                (esdService != null && esdService.EsdAnalyzer != null)
                    ? esdService.EsdAnalyzer.Options
                    : null;

            if (options == null)
            {
                return false;
            }

            options.ApplyNormalizedRoi(roiX, roiY, roiW, roiH);
            return true;
        }

        /// <summary>
        /// 运行期热更新门区域 ROI（UI 拖拽标定调用）：语义与 TryUpdateEsdRoi 相同。
        /// 返回 false 表示当前没有可更新的门监测链路——调用方仍应保存配置。
        /// 注意：门区域 ROI 变化后**基准图尺寸/内容不再匹配**，调用方应在标定后
        /// 提示用户重新采集关门基准（门关着时点"重设门基准"）。
        /// </summary>
        public bool TryUpdateDoorRoi(float roiX, float roiY, float roiW, float roiH)
        {
            var esdService = _pipeline as YoloDetection.YoloDetectionService;
            YoloDetection.DoorMonitorOptions options =
                (esdService != null && esdService.DoorAnalyzer != null)
                    ? esdService.DoorAnalyzer.Options
                    : null;

            if (options == null)
            {
                return false;
            }

            options.ApplyNormalizedRoi(roiX, roiY, roiW, roiH);
            return true;
        }

        /// <summary>
        /// 重设关门基准：用**最近一帧**预览画面采集（调用时机：门关着的时候）。
        /// 基准内存生效并自动落盘 PNG，重启后仍有效。
        /// 返回 false 表示当前没有运行中的门监测链路（预览未启动/未启用/降级）。
        /// </summary>
        public bool SetDoorBaselineFromLatestFrame()
        {
            var esdService = _pipeline as YoloDetection.YoloDetectionService;
            YoloDetection.DoorMonitorAnalyzer analyzer = esdService != null ? esdService.DoorAnalyzer : null;

            OpenCvSharp.Mat latest = _lastFrameForDoor;
            if (analyzer == null || latest == null || latest.Empty())
            {
                return false;
            }

            analyzer.SetBaselineFromFrame(latest);
            return true;
        }

        // 门基准采集用的最近一帧（OnFrameReady 里更新；与主管道克隆并行，独立持有）
        private OpenCvSharp.Mat _lastFrameForDoor;

        /// <summary>清理门基准采集用的最近帧缓存。</summary>
        private void DisposeLastFrameForDoor()
        {
            if (_lastFrameForDoor != null)
            {
                _lastFrameForDoor.Dispose();
                _lastFrameForDoor = null;
            }
        }
    }
}
