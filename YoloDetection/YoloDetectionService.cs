/*
 * 文件名: YoloDetectionService.cs
 * 作者: Auto Generated
 * 日期: 2026-07-16
 * 版本: 1.0
 * 
 * 功能说明:
 *     这个文件实现了YOLO检测服务，是整个检测模块的核心组件。
 *     它实现了IDetectionPipeline接口，整合了帧处理、检测、可视化等功能。
 *     
 *     设计特点:
 *     1. 独立线程处理检测，不阻塞UI线程
 *     2. 同步检测确保检测框与画面完全对齐
 *     3. 支持运行时切换检测器和可视化器（热插拔）
 *     4. 通过事件机制通知外部检测结果和处理后的帧
 *     5. 使用Mat格式传递帧数据，移除WinForms依赖
 *     
 *     v3.0 架构变更:
 *     - 实现IDetectionPipeline接口，支持接口抽象
 *     - 返回Mat格式而非Bitmap，移除WinForms依赖
 *     - 通过事件与帧源解耦（不再依赖RtspFrameCapturer）
 *     - 集成后处理器，支持灵活的结果处理
 */

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using OpenCvSharp;

namespace YoloDetector.YoloDetection
{
    /// <summary>
    /// YOLO检测服务（实现IDetectionPipeline接口）
    /// 
    /// 这个类是整个YOLO检测模块的核心组件，负责管理检测流程。
    /// 
    /// 核心职责:
    /// 1. 管理检测线程的生命周期（启动/停止）
    /// 2. 接收视频帧并执行YOLO检测
    /// 3. 管理检测结果的缓存和更新
    /// 4. 支持检测器和可视化器的运行时切换
    /// 5. 通过事件通知外部检测结果和处理后的帧
    /// 
    /// 检测流程:
    /// ProcessFrame(Mat) → 保存帧到缓冲区 → 通知检测线程 → YOLO推理 → 后处理 → 绘制检测框 → 触发事件
    /// 
    /// 使用示例:
    /// var detector = new YoloV26Detector();
    /// detector.Initialize("yolo26n.onnx");
    /// 
    /// var pipeline = new YoloDetectionService(detector);
    /// pipeline.DetectionsUpdated += OnDetectionsUpdated;
    /// pipeline.FrameProcessed += OnFrameProcessed;
    /// pipeline.Start();
    /// 
    /// pipeline.ProcessFrame(frame); // 传递帧进行检测
    /// </summary>
    public class YoloDetectionService : IDetectionPipeline
    {
        /// <summary>
        /// YOLO检测器实例
        /// 
        /// 负责执行实际的YOLO推理。
        /// </summary>
        private IYoloDetector _detector;

        /// <summary>
        /// 检测管道运行标志
        /// 
        /// 控制检测线程的启动和停止。
        /// </summary>
        private bool _isRunning = false;

        /// <summary>
        /// 检测线程
        /// 
        /// 在后台运行，持续处理帧并执行YOLO推理。
        /// </summary>
        private System.Threading.Thread _detectionThread;

        /// <summary>
        /// 帧就绪事件
        /// 
        /// 用于通知检测线程有新帧可用。
        /// </summary>
        private System.Threading.AutoResetEvent _frameReadyEvent = new System.Threading.AutoResetEvent(false);

        /// <summary>
        /// 当前帧（Mat格式）
        /// 
        /// 存储待处理的视频帧。
        /// </summary>
        private Mat _currentMatFrame;

        /// <summary>
        /// 帧宽度
        /// 
        /// 用于后处理时的边界计算。
        /// </summary>
        private int _frameWidth;

        /// <summary>
        /// 帧高度
        /// 
        /// 用于后处理时的边界计算。
        /// </summary>
        private int _frameHeight;

        /// <summary>
        /// 最新检测结果列表
        /// 
        /// 存储最近一次检测的结果，供外部查询。
        /// </summary>
        private List<DetectionResult> _lastDetections = new List<DetectionResult>();

        /// <summary>
        /// 锁对象（用于帧和检测结果的线程安全访问）
        /// 
        /// 保护_currentMatFrame、_frameWidth、_frameHeight、_lastDetections。
        /// </summary>
        private object _lockObj = new object();

        /// <summary>
        /// 释放标志
        /// 
        /// 防止多次释放资源。
        /// </summary>
        private bool _disposed = false;

        /// <summary>
        /// 检测计数
        /// 
        /// 用于统计检测次数，每10次输出一次调试信息。
        /// </summary>
        private int _detectCount = 0;

        /// <summary>
        /// 检测失败计数
        /// 
        /// 用于统计检测失败次数。
        /// </summary>
        private int _detectFailCount = 0;

        /// <summary>
        /// 当前可视化器实例
        /// 
        /// 负责绘制检测框到帧上。
        /// </summary>
        private IDetectionVisualizer _visualizer;

        /// <summary>
        /// 可视化器锁对象
        /// 
        /// 保护_visualizer的线程安全访问（支持运行时切换）。
        /// </summary>
        private object _visualizerLock = new object();

        /// <summary>
        /// 检测结果后处理器
        /// 
        /// 对YOLO检测结果进行二次处理（如裁剪、过滤）。
        /// </summary>
        private IDetectionResultProcessor _resultProcessor = new DefaultResultProcessor();

        /// <summary>
        /// 检测结果更新事件
        /// 
        /// 当检测完成后触发，通知外部有新的检测结果。
        /// 事件参数是检测结果列表（List<DetectionResult>）。
        /// 
        /// 订阅示例:
        /// pipeline.DetectionsUpdated += (sender, results) =>
        /// {
        ///     foreach (var result in results)
        ///     {
        ///         Console.WriteLine($"检测到: {result.ClassName}, 置信度: {result.Confidence}");
        ///     }
        /// };
        /// </summary>
        public event EventHandler<List<DetectionResult>> DetectionsUpdated;

        /// <summary>
        /// 帧处理完成事件
        /// 
        /// 当帧处理和检测框绘制完成后触发，通知外部显示处理后的帧。
        /// 事件参数是处理后的Mat图像（已绘制检测框）。
        /// 
        /// 重要:
        /// - 事件参数是帧的克隆副本，外部可以自由使用和释放
        /// - 外部使用完后必须调用frame.Dispose()释放资源
        /// 
        /// 订阅示例:
        /// pipeline.FrameProcessed += (sender, frame) =>
        /// {
        ///     var bitmap = MatExtensions.MatToBitmap(frame);
        ///     pictureBox.Image = bitmap;
        ///     frame.Dispose(); // 非常重要！释放Mat资源
        /// };
        /// </summary>
        public event EventHandler<Mat> FrameProcessed;

        /// <summary>
        /// 检测管道是否正在运行
        /// 
        /// 返回:
        /// true 如果检测线程正在运行
        /// false 如果已停止
        /// </summary>
        public bool IsRunning => _isRunning;

        /// <summary>
        /// 置信度阈值
        /// 
        /// 获取或设置YOLO检测器的置信度阈值。
        /// 低于此值的检测结果会被过滤掉。
        /// 
        /// 默认值: 0.5
        /// 范围: 0-1
        /// </summary>
        public float ConfidenceThreshold
        {
            get => _detector?.ConfidenceThreshold ?? 0.5f;
            set { if (_detector != null) _detector.ConfidenceThreshold = value; }
        }

        /// <summary>
        /// NMS阈值
        /// 
        /// 获取或设置YOLO检测器的NMS（非极大值抑制）阈值。
        /// 用于去除重叠的检测框。
        /// 
        /// 默认值: 0.45
        /// 范围: 0-1
        /// </summary>
        public float NmsThreshold
        {
            get => _detector?.NmsThreshold ?? 0.45f;
            set { if (_detector != null) _detector.NmsThreshold = value; }
        }

        /// <summary>
        /// 检测结果后处理器
        /// 
        /// 获取或设置检测结果后处理器。
        /// 后处理器可以对原始检测结果进行裁剪、过滤等操作。
        /// 
        /// 默认值: DefaultResultProcessor（裁剪边界）
        /// 
        /// 示例:
        /// // 使用组合处理器
        /// var composite = new CompositeResultProcessor();
        /// composite.AddProcessor(new DefaultResultProcessor());
        /// composite.AddProcessor(new SizeFilterProcessor());
        /// pipeline.ResultProcessor = composite;
        /// </summary>
        public IDetectionResultProcessor ResultProcessor
        {
            get => _resultProcessor;
            set => _resultProcessor = value ?? new DefaultResultProcessor();
        }

        /// <summary>
        /// 最新检测结果（只读属性）
        /// 
        /// 返回最近一次检测的结果列表。
        /// 返回的是副本，外部修改不会影响内部状态。
        /// 
        /// 返回:
        /// 检测结果列表（如果尚未检测过，返回空列表）
        /// </summary>
        public List<DetectionResult> LastDetections
        {
            get
            {
                lock (_lockObj)
                {
                    return new List<DetectionResult>(_lastDetections);
                }
            }
        }

        /// <summary>
        /// 当前可视化器
        /// 
        /// 获取或设置当前的可视化器实例。
        /// 
        /// 示例:
        /// pipeline.Visualizer = new YoloBuiltinVisualizer();
        /// </summary>
        public IDetectionVisualizer Visualizer
        {
            get => _visualizer;
            set => SetVisualizer(value);
        }

        /// <summary>
        /// 当前可视化器类型
        /// 
        /// 判断当前使用的可视化器类型。
        /// 
        /// 返回:
        /// VisualizerType.YoloBuiltin 如果使用YoloBuiltinVisualizer
        /// VisualizerType.OpenCV 如果使用OpenCVVisualizer
        /// </summary>
        public VisualizerType CurrentVisualizerType
        {
            get
            {
                if (_visualizer is YoloBuiltinVisualizer)
                    return VisualizerType.YoloBuiltin;
                if (_visualizer is OpenCVVisualizer)
                    return VisualizerType.OpenCV;
                return VisualizerType.OpenCV;
            }
        }

        /// <summary>
        /// 构造函数（指定检测器）
        /// 
        /// 参数:
        /// detector - YOLO检测器实例，必须已初始化
        /// 
        /// 异常:
        /// ArgumentNullException - 如果detector为null
        /// 
        /// 示例:
        /// var detector = new YoloV26Detector();
        /// detector.Initialize("yolo26n.onnx");
        /// var pipeline = new YoloDetectionService(detector);
        /// </summary>
        /// <param name="detector">YOLO检测器实例</param>
        public YoloDetectionService(IYoloDetector detector)
        {
            _detector = detector ?? throw new ArgumentNullException(nameof(detector));
            _visualizer = new OpenCVVisualizer(); // 默认使用OpenCV可视化器
        }

        /// <summary>
        /// 构造函数（指定检测器和可视化器）
        /// 
        /// 参数:
        /// detector - YOLO检测器实例，必须已初始化
        /// visualizer - 可视化器实例
        /// 
        /// 异常:
        /// ArgumentNullException - 如果detector为null
        /// 
        /// 示例:
        /// var detector = new YoloV26Detector();
        /// detector.Initialize("yolo26n.onnx");
        /// var visualizer = new YoloBuiltinVisualizer();
        /// var pipeline = new YoloDetectionService(detector, visualizer);
        /// </summary>
        /// <param name="detector">YOLO检测器实例</param>
        /// <param name="visualizer">可视化器实例</param>
        public YoloDetectionService(IYoloDetector detector, IDetectionVisualizer visualizer)
        {
            _detector = detector ?? throw new ArgumentNullException(nameof(detector));
            _visualizer = visualizer ?? new OpenCVVisualizer();
        }

        /// <summary>
        /// 构造函数（指定检测器和可视化器类型）
        /// 
        /// 参数:
        /// detector - YOLO检测器实例，必须已初始化
        /// visualizerType - 可视化器类型（通过VisualizerFactory创建）
        /// 
        /// 异常:
        /// ArgumentNullException - 如果detector为null
        /// 
        /// 示例:
        /// var detector = new YoloV26Detector();
        /// detector.Initialize("yolo26n.onnx");
        /// var pipeline = new YoloDetectionService(detector, VisualizerType.OpenCV);
        /// </summary>
        /// <param name="detector">YOLO检测器实例</param>
        /// <param name="visualizerType">可视化器类型</param>
        public YoloDetectionService(IYoloDetector detector, VisualizerType visualizerType)
        {
            _detector = detector ?? throw new ArgumentNullException(nameof(detector));
            _visualizer = VisualizerFactory.Create(visualizerType);
        }

        /// <summary>
        /// 设置可视化器（运行时切换）
        /// 
        /// 参数:
        /// visualizer - 新的可视化器实例
        /// 
        /// 异常:
        /// ArgumentNullException - 如果visualizer为null
        /// 
        /// 示例:
        /// pipeline.SetVisualizer(new YoloBuiltinVisualizer());
        /// </summary>
        /// <param name="visualizer">新的可视化器实例</param>
        public void SetVisualizer(IDetectionVisualizer visualizer)
        {
            if (visualizer == null)
                throw new ArgumentNullException(nameof(visualizer));

            lock (_visualizerLock)
            {
                _visualizer = visualizer;
            }
        }

        /// <summary>
        /// 切换可视化器（通过类型）
        /// 
        /// 参数:
        /// type - 可视化器类型
        /// 
        /// 示例:
        /// pipeline.SwitchVisualizer(VisualizerType.YoloBuiltin);
        /// </summary>
        /// <param name="type">可视化器类型</param>
        public void SwitchVisualizer(VisualizerType type)
        {
            lock (_visualizerLock)
            {
                _visualizer = VisualizerFactory.Create(type);
            }
        }

        /// <summary>
        /// 启动检测管道
        /// 
        /// 创建并启动检测线程，开始处理视频帧。
        /// 
        /// 启动流程:
        /// 1. 检查检测器是否已初始化
        /// 2. 如果已在运行，直接返回
        /// 3. 设置运行标志
        /// 4. 创建并启动检测线程
        /// 
        /// 异常:
        /// InvalidOperationException - 如果检测器尚未初始化
        /// 
        /// 示例:
        /// pipeline.Start();
        /// </summary>
        public void Start()
        {
            // 1. 检查检测器是否已初始化
            if (!_detector.IsInitialized)
            {
                throw new InvalidOperationException("YOLO检测器尚未初始化");
            }

            // 2. 如果已在运行，直接返回
            if (_isRunning)
            {
                return;
            }

            // 3. 设置运行标志
            _isRunning = true;

            // 4. 创建并启动检测线程
            _detectionThread = new System.Threading.Thread(DetectionLoop);
            _detectionThread.IsBackground = true;
            _detectionThread.Start();
        }

        /// <summary>
        /// 停止检测管道
        /// 
        /// 通知检测线程停止，并等待线程退出。
        /// 
        /// 停止流程:
        /// 1. 设置_isRunning为false
        /// 2. 触发_frameReadyEvent（唤醒检测线程）
        /// 3. 等待检测线程退出（最多3秒）
        /// 4. 清空检测结果缓存
        /// 
        /// 示例:
        /// pipeline.Stop();
        /// </summary>
        public void Stop()
        {
            // 1. 设置停止标志
            _isRunning = false;

            // 2. 唤醒检测线程（使其能退出等待状态）
            _frameReadyEvent.Set();

            // 3. 等待检测线程退出（最多3秒）
            if (_detectionThread != null && _detectionThread.IsAlive)
            {
                _detectionThread.Join(3000);
            }

            // 4. 清空检测结果缓存
            lock (_lockObj)
            {
                _lastDetections.Clear();
            }
        }

        /// <summary>
        /// 处理视频帧（核心方法）
        /// 
        /// 将视频帧传递给检测管道进行处理。
        /// 
        /// 参数:
        /// frame - 要处理的视频帧（OpenCV Mat格式）
        /// 
        /// 处理流程:
        /// 1. 检查管道是否正在运行
        /// 2. 检查帧是否有效（非空）
        /// 3. 将帧克隆到内部缓冲区
        /// 4. 更新帧尺寸
        /// 5. 触发_frameReadyEvent通知检测线程
        /// 
        /// 注意:
        /// - frame参数会被克隆一份，原始帧可以立即释放
        /// - 如果管道未运行，帧会被忽略
        /// 
        /// 示例:
        /// using (var frame = new Mat())
        /// {
        ///     capture.Read(frame);
        ///     pipeline.ProcessFrame(frame);
        /// }
        /// </summary>
        /// <param name="frame">视频帧（Mat格式）</param>
        public void ProcessFrame(Mat frame)
        {
            // 1. 检查管道是否正在运行，帧是否有效
            if (!_isRunning || frame == null || frame.Empty())
            {
                return;
            }

            // 2. 使用锁保护帧的存储
            lock (_lockObj)
            {
                _currentMatFrame = frame.Clone(); // 克隆帧，避免外部释放
                _frameWidth = frame.Cols;
                _frameHeight = frame.Rows;
            }

            // 3. 通知检测线程有新帧可用
            _frameReadyEvent.Set();
        }

        /// <summary>
        /// 获取最新的检测结果
        /// 
        /// 返回最近一次检测的结果列表，用于外部查询。
        /// 返回的是副本，外部修改不会影响内部状态。
        /// 
        /// 返回:
        /// 检测结果列表（如果尚未检测过，返回空列表）
        /// 
        /// 示例:
        /// var results = pipeline.GetLatestDetections();
        /// foreach (var result in results)
        /// {
        ///     Console.WriteLine(result.ClassName);
        /// }
        /// </summary>
        /// <returns>最新的检测结果列表</returns>
        public List<DetectionResult> GetLatestDetections()
        {
            lock (_lockObj)
            {
                if (_lastDetections == null || _lastDetections.Count == 0)
                {
                    return new List<DetectionResult>();
                }
                return new List<DetectionResult>(_lastDetections);
            }
        }

        /// <summary>
        /// 设置新的检测器（热插拔）
        /// 
        /// 支持运行时切换检测器，实现热插拔。
        /// 如果管道正在运行，会先停止再切换，然后自动重启。
        /// 
        /// 参数:
        /// detector - 新的检测器实例，必须已初始化
        /// 
        /// 异常:
        /// ArgumentNullException - 如果detector为null
        /// 
        /// 切换流程:
        /// 1. 记录当前运行状态
        /// 2. 如果正在运行，先停止
        /// 3. 替换检测器引用
        /// 4. 如果之前在运行且新检测器已初始化，重新启动
        /// 
        /// 示例:
        /// var newDetector = new YoloV26Detector();
        /// newDetector.Initialize("yolo26s.onnx");
        /// pipeline.SetDetector(newDetector);
        /// </summary>
        /// <param name="detector">新的检测器实例</param>
        public void SetDetector(IYoloDetector detector)
        {
            if (detector == null)
                throw new ArgumentNullException(nameof(detector));

            // 1. 记录当前运行状态
            var wasRunning = _isRunning;

            // 2. 如果正在运行，先停止
            if (wasRunning)
            {
                Stop();
            }

            // 3. 替换检测器引用
            _detector = detector;

            // 4. 如果之前在运行且新检测器已初始化，重新启动
            if (wasRunning && _detector.IsInitialized)
            {
                Start();
            }
        }

        /// <summary>
        /// 检测循环（在检测线程中运行）
        /// 
        /// 持续等待新帧，执行YOLO推理，处理结果，并触发事件。
        /// 
        /// 循环流程:
        /// 1. 等待_frameReadyEvent（有新帧可用）
        /// 2. 检查_isRunning标志（是否需要退出）
        /// 3. 获取当前帧和尺寸
        /// 4. 执行YOLO推理
        /// 5. 应用后处理器
        /// 6. 更新检测结果缓存
        /// 7. 触发DetectionsUpdated事件
        /// 8. 绘制检测框到帧上
        /// 9. 触发FrameProcessed事件
        /// 10. 释放帧资源
        /// 
        /// 注意:
        /// - 使用using语句确保Mat对象被正确释放
        /// - 使用锁保护共享资源的访问
        /// - 检测框绘制在帧的克隆副本上，不修改原始帧
        /// </summary>
        private void DetectionLoop()
        {
            while (_isRunning)
            {
                // 1. 等待新帧可用
                _frameReadyEvent.WaitOne();

                // 2. 检查是否需要退出
                if (!_isRunning)
                {
                    break;
                }

                Mat matFrame = null;
                int width, height;

                // 3. 获取当前帧和尺寸（使用锁保护）
                lock (_lockObj)
                {
                    matFrame = _currentMatFrame;
                    width = _frameWidth;
                    height = _frameHeight;
                    _currentMatFrame = null; // 清空缓冲区，准备接收下一帧
                }

                // 4. 检查帧是否有效
                if (matFrame == null)
                {
                    continue;
                }

                try
                {
                    // 5. 执行YOLO推理
                    List<DetectionResult> detections = _detector.Detect(matFrame);

                    // 6. 应用后处理器（裁剪边界、过滤等）
                    if (_resultProcessor != null)
                    {
                        detections = _resultProcessor.Process(detections, width, height);
                    }

                    // 7. 递增检测计数
                    _detectCount++;

                    // 8. 更新检测结果缓存（使用锁保护）
                    lock (_lockObj)
                    {
                        _lastDetections = detections ?? new List<DetectionResult>();
                    }

                    // 9. 每10次检测输出一次调试信息
                    if (_detectCount % 10 == 0)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"[YOLO] 第{_detectCount}次检测, {_lastDetections.Count}个目标, " +
                            $"失败{_detectFailCount}次");
                    }

                    // 10. 触发检测结果更新事件
                    DetectionsUpdated?.Invoke(this, _lastDetections ?? new List<DetectionResult>());

                    // 11. 获取当前可视化器（使用锁保护）
                    IDetectionVisualizer currentVisualizer;
                    lock (_visualizerLock)
                    {
                        currentVisualizer = _visualizer;
                    }

                    // 12. 使用可视化器绘制检测框并触发帧处理完成事件
                    if (currentVisualizer != null)
                    {
                        // 调用可视化器的VisualizeDetectionMat方法绘制检测框
                        // 返回的是已绘制好检测框的Mat（已克隆，需要外部释放）
                        var drawFrame = currentVisualizer.VisualizeDetectionMat(matFrame, _lastDetections);

                        if (drawFrame != null && !drawFrame.Empty())
                        {
                            // 触发帧处理完成事件（传递绘制好的帧）
                            FrameProcessed?.Invoke(this, drawFrame);
                        }
                        else
                        {
                            // 如果可视化器返回空，直接传递原始帧
                            FrameProcessed?.Invoke(this, matFrame.Clone());
                        }
                    }
                    else
                    {
                        // 如果没有可视化器，直接传递原始帧
                        FrameProcessed?.Invoke(this, matFrame.Clone());
                    }

                    // 13. 释放帧资源
                    matFrame.Dispose();
                }
                catch (Exception ex)
                {
                    // 14. 处理异常
                    _detectFailCount++;
                    System.Diagnostics.Debug.WriteLine($"[YOLO] 检测异常(#{_detectFailCount}): {ex.Message}");

                    // 释放帧资源
                    if (matFrame != null)
                    {
                        matFrame.Dispose();
                    }
                }
            }
        }

        /// <summary>
        /// 释放资源（实现IDisposable接口）
        /// 
        /// 调用Stop方法停止检测线程，并释放AutoResetEvent资源。
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// 释放资源（受保护的方法）
        /// 
        /// 参数:
        /// disposing - 如果为true，表示是显式调用Dispose()
        ///             如果为false，表示是由析构函数调用
        /// </summary>
        /// <param name="disposing">是否由Dispose()调用</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;

            if (disposing)
            {
                // 停止检测线程
                Stop();

                // 释放AutoResetEvent资源
                _frameReadyEvent.Dispose();
            }

            _disposed = true;
        }

        /// <summary>
        /// 析构函数
        /// 
        /// 确保在对象被垃圾回收时释放资源。
        /// </summary>
        ~YoloDetectionService()
        {
            Dispose(false);
        }
    }
}