/*
 * 文件名: IDetectionPipeline.cs
 * 作者: Auto Generated
 * 日期: 2026-07-16
 * 版本: 1.0
 * 
 * 功能说明:
 *     这个文件定义了检测管道的核心接口，实现了检测流程的模块化和解耦。
 *     
 *     设计模式：管道模式 + 观察者模式
 *     - 管道模式：将检测流程分解为多个独立的组件（帧源、处理器、检测器）
 *     - 观察者模式：通过事件通知机制实现组件间的解耦通信
 *     
 *     架构设计：
 *     1. IFrameSource：帧源接口，负责获取视频帧（如RTSP流、本地视频文件）
 *     2. IFrameProcessor：帧处理器接口，负责处理单帧（如预处理、特征提取）
 *     3. IDetectionPipeline：检测管道接口，整合帧源、检测器、可视化器
 *     4. ProcessingResult：处理结果类，封装处理后的帧和检测结果
 *     
 *     模块化优势：
 *     - 帧源可以独立替换（RTSP → USB摄像头 → 本地视频）
 *     - 检测器可以独立替换（YOLOV26 → YOLOV8）
 *     - 可视化器可以独立替换（OpenCV → GDI+ → 自定义）
 *     - 各组件通过接口通信，互不依赖具体实现
 */

using System;
using System.Collections.Generic;
using OpenCvSharp;

namespace YoloDetector.YoloDetection
{
    /// <summary>
    /// 检测管道接口
    /// 
    /// 这个接口定义了完整的检测流程管理规范，是整个YOLO检测模块的核心接口。
    /// 它整合了帧处理、检测、结果更新和可视化等功能。
    /// 
    /// 核心职责：
    /// 1. 管理检测线程的生命周期（启动/停止）
    /// 2. 接收视频帧并执行检测
    /// 3. 管理检测结果的缓存和更新
    /// 4. 支持检测器和可视化器的运行时切换
    /// 5. 通过事件通知外部检测结果和处理后的帧
    /// 
    /// 实现示例：
    /// public class YoloDetectionService : IDetectionPipeline
    /// {
    ///     public void Start() { ... }
    ///     public void Stop() { ... }
    ///     public void ProcessFrame(Mat frame) { ... }
    ///     // ... 其他方法
    /// }
    /// </summary>
    public interface IDetectionPipeline : IDisposable
    {
        /// <summary>
        /// 检测管道是否正在运行
        /// 
        /// 返回:
        /// true 如果管道正在运行（检测线程已启动）
        /// false 如果管道已停止
        /// </summary>
        bool IsRunning { get; }

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
        event EventHandler<List<DetectionResult>> DetectionsUpdated;

        /// <summary>
        /// 帧处理完成事件
        /// 
        /// 当帧处理和检测框绘制完成后触发，通知外部显示处理后的帧。
        /// 事件参数是处理后的Mat图像（已绘制检测框）。
        /// 
        /// 订阅示例:
        /// pipeline.FrameProcessed += (sender, frame) =>
        /// {
        ///     // 将Mat转换为Bitmap并显示
        ///     var bitmap = MatExtensions.MatToBitmap(frame);
        ///     pictureBox.Image = bitmap;
        ///     frame.Dispose();
        /// };
        /// </summary>
        event EventHandler<Mat> FrameProcessed;

        /// <summary>
        /// 启动检测管道
        /// 
        /// 创建并启动检测线程，开始处理视频帧。
        /// 在调用Start之前，必须先初始化检测器（调用detector.Initialize）。
        /// 
        /// 异常:
        /// InvalidOperationException - 如果检测器尚未初始化
        /// 
        /// 示例:
        /// var detector = new YoloV26Detector();
        /// detector.Initialize(modelPath);
        /// 
        /// var pipeline = new YoloDetectionService(detector);
        /// pipeline.Start();
        /// </summary>
        void Start();

        /// <summary>
        /// 停止检测管道
        /// 
        /// 通知检测线程停止，并等待线程退出。
        /// 停止后，ProcessFrame方法将不再处理新的帧。
        /// 
        /// 示例:
        /// pipeline.Stop();
        /// </summary>
        void Stop();

        /// <summary>
        /// 处理视频帧
        /// 
        /// 将视频帧传递给检测管道，管道会：
        /// 1. 将帧保存到内部缓冲区
        /// 2. 通知检测线程有新帧可用
        /// 3. 检测线程执行YOLO推理并绘制检测框
        /// 
        /// 参数:
        /// frame - 要处理的视频帧（OpenCV Mat格式）
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
        void ProcessFrame(Mat frame);

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
        List<DetectionResult> GetLatestDetections();

        /// <summary>
        /// 设置新的检测器
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
        /// 示例:
        /// var newDetector = new YoloV8Detector();
        /// newDetector.Initialize(modelPath);
        /// pipeline.SetDetector(newDetector);
        /// </summary>
        /// <param name="detector">新的检测器实例</param>
        void SetDetector(IYoloDetector detector);

        /// <summary>
        /// 设置新的可视化器
        /// 
        /// 支持运行时切换可视化器，实现检测框绘制方案的热插拔。
        /// 
        /// 参数:
        /// visualizer - 新的可视化器实例，实现了IDetectionVisualizer接口
        /// 
        /// 示例:
        /// pipeline.SetVisualizer(new YoloBuiltinVisualizer());
        /// </summary>
        /// <param name="visualizer">新的可视化器实例</param>
        void SetVisualizer(IDetectionVisualizer visualizer);
    }

    /// <summary>
    /// 帧源接口
    /// 
    /// 这个接口定义了获取视频帧的规范，所有帧源都必须实现这个接口。
    /// 
    /// 实现示例:
    /// public class UsbCameraCapturer : IFrameSource
    /// {
    ///     public bool Start(string deviceId) { ... }
    ///     public void Stop() { ... }
    ///     // ... 其他方法
    /// }
    /// 
    /// 现有实现:
    /// - RtspFrameCapturer: 通过RTSP协议获取网络摄像头帧
    /// 
    /// 可能的扩展实现:
    /// - UsbCameraCapturer: 获取USB摄像头帧
    /// - FileFrameSource: 从本地视频文件读取帧
    /// - ImageFolderSource: 从图像文件夹读取帧（用于测试）
    /// </summary>
    public interface IFrameSource : IDisposable
    {
        /// <summary>
        /// 帧源是否正在运行
        /// </summary>
        bool IsRunning { get; }

        /// <summary>
        /// 获取帧宽度
        /// 
        /// 在Start成功后，这个属性会被设置为实际的帧宽度。
        /// </summary>
        int FrameWidth { get; }

        /// <summary>
        /// 获取帧高度
        /// 
        /// 在Start成功后，这个属性会被设置为实际的帧高度。
        /// </summary>
        int FrameHeight { get; }

        /// <summary>
        /// 帧就绪事件
        /// 
        /// 当捕获到新帧时触发，通知外部有新的视频帧可用。
        /// 事件参数是捕获到的Mat图像。
        /// 
        /// 订阅示例:
        /// frameSource.FrameReady += (sender, frame) =>
        /// {
        ///     pipeline.ProcessFrame(frame);
        /// };
        /// </summary>
        event EventHandler<Mat> FrameReady;

        /// <summary>
        /// 启动帧捕获
        /// 
        /// 连接到帧源并开始捕获帧。
        /// 
        /// 参数:
        /// sourceUrl - 帧源地址，格式取决于具体实现：
        ///             - RTSP流: "rtsp://username:password@ip:port/stream"
        ///             - USB摄像头: "0"（设备索引）
        ///             - 本地文件: "video.mp4"
        /// 
        /// 返回:
        /// true 如果启动成功
        /// false 如果启动失败（如地址无效、连接超时）
        /// 
        /// 示例:
        /// var frameSource = new RtspFrameCapturer();
        /// frameSource.FrameReady += OnFrameReady;
        /// frameSource.Start("rtsp://admin:123456@192.168.1.100:554/stream");
        /// </summary>
        /// <param name="sourceUrl">帧源地址</param>
        /// <returns>是否启动成功</returns>
        bool Start(string sourceUrl);

        /// <summary>
        /// 停止帧捕获
        /// 
        /// 停止捕获线程并释放资源。
        /// </summary>
        void Stop();
    }

    /// <summary>
    /// 帧处理器接口
    /// 
    /// 这个接口定义了单帧处理的规范，用于在检测之前对帧进行预处理。
    /// 
    /// 典型用途:
    /// - 图像预处理（缩放、裁剪、归一化）
    /// - 特征提取
    /// - 图像增强
    /// - 帧过滤（如跳过模糊帧）
    /// 
    /// 实现示例:
    /// public class FramePreprocessor : IFrameProcessor
    /// {
    ///     public void Process(Mat frame)
    ///     {
    ///         // 缩放图像到640x640
    ///         Cv2.Resize(frame, frame, new Size(640, 640));
    ///         // 归一化到0-1范围
    ///         frame.ConvertTo(frame, MatType.CV_32FC3, 1.0 / 255.0);
    ///     }
    /// }
    /// </summary>
    public interface IFrameProcessor : IDisposable
    {
        /// <summary>
        /// 处理单帧图像
        /// 
        /// 参数:
        /// frame - 要处理的帧（Mat格式）
        /// 
        /// 注意:
        /// 实现类可以直接修改传入的frame，也可以创建新的Mat返回
        /// </summary>
        /// <param name="frame">要处理的帧</param>
        void Process(Mat frame);

        /// <summary>
        /// 处理完成事件
        /// 
        /// 当帧处理完成后触发，通知外部处理结果。
        /// 事件参数是ProcessingResult对象，包含处理后的帧和检测结果。
        /// </summary>
        event EventHandler<ProcessingResult> Processed;
    }

    /// <summary>
    /// 处理结果类
    /// 
    /// 封装帧处理的结果，包括处理后的帧和检测结果。
    /// 
    /// 使用场景:
    /// - 帧处理器完成处理后，通过这个类传递结果
    /// - 检测管道内部使用，在FrameProcessed事件中传递给外部
    /// </summary>
    public class ProcessingResult
    {
        /// <summary>
        /// 处理后的帧
        /// 
        /// 可能已经绘制了检测框，或者经过了预处理。
        /// </summary>
        public Mat Frame { get; set; }

        /// <summary>
        /// 检测结果列表
        /// 
        /// 如果还没有进行检测，可能为空列表。
        /// </summary>
        public List<DetectionResult> Detections { get; set; }

        /// <summary>
        /// 处理耗时（毫秒）
        /// 
        /// 用于性能统计和调试。
        /// </summary>
        public long ProcessingTimeMs { get; set; }
    }
}