using System;
using System.Collections.Generic;

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

        public Detection.VisualizerType VisualizerType { get; set; } = Detection.VisualizerType.YoloBuiltin;

        /// <summary>RTSP 流地址</summary>
        public string RtspUrl { get; set; }
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
        private readonly Action<List<Detection.DetectionResult>> _detectionSink;

        private Detection.IYoloDetector _detector;
        private Detection.IDetectionPipeline _pipeline;
        private Detection.IFrameSource _frameSource;

        public VideoDetectionController(
            Action<System.Drawing.Bitmap> previewSink,
            Action<List<Detection.DetectionResult>> detectionSink)
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

            // 1. 日志开关
            Detection.LogManager.Initialize(
                enableYoloLog: options.YoloDebugLog,
                enableGeneralLog: true,
                enableDetectionResultLog: options.DetectionResultLog);

            // 2. 检测器（跨会话复用已初始化的实例，避免重复加载模型）
            EnsureDetector(options);

            // 3. 可视化器 + 检测管道
            var visualizer = Detection.VisualizerFactory.Create(options.VisualizerType);
            var pipeline = new Detection.YoloDetectionService(_detector, visualizer)
            {
                ConfidenceThreshold = options.ConfidenceThreshold,
                NmsThreshold = options.NmsThreshold
            };

            pipeline.DetectionsUpdated += OnDetectionsUpdated;
            pipeline.FrameProcessed += OnFrameProcessed;
            pipeline.Start();

            // 4. 帧源最后启动（管道已就绪，避免开头几帧因管道未运行被丢弃）
            var frameSource = new Detection.RtspFrameCapturer();
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

            if (_pipeline != null)
            {
                _pipeline.DetectionsUpdated -= OnDetectionsUpdated;
                _pipeline.FrameProcessed -= OnFrameProcessed;
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

                var detector = new Detection.YoloV26Detector();
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

        /// <summary>帧源出帧 → 提交管道检测。Mat 所有权在本方法内终结。</summary>
        private void OnFrameReady(object sender, OpenCvSharp.Mat frame)
        {
            try
            {
                var pipeline = _pipeline;
                if (pipeline != null && pipeline.IsRunning)
                {
                    pipeline.ProcessFrame(frame); // 管道内部克隆，此处原帧随后释放
                }
            }
            finally
            {
                frame.Dispose();
            }
        }

        /// <summary>管道出帧 → 转 Bitmap 后交 UI 显示。Mat 在此立即释放。</summary>
        private void OnFrameProcessed(object sender, OpenCvSharp.Mat frame)
        {
            System.Drawing.Bitmap bitmap = null;
            try
            {
                bitmap = Detection.MatExtensions.MatToBitmap(frame);
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

        private void OnDetectionsUpdated(object sender, List<Detection.DetectionResult> detections)
        {
            _detectionSink(detections);
        }
    }
}
