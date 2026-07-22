/*
 * 文件名: RtspFrameCapturer.cs
 * 作者: Auto Generated
 * 日期: 2026-07-16
 * 版本: 1.0
 * 
 * 功能说明:
 *     这个文件实现了基于OpenCV的RTSP帧捕获器，负责从网络摄像头获取视频帧。
 *     
 *     设计特点:
 *     1. 独立于LibVLC播放模块，专门为YOLO检测提供帧数据
 *     2. 使用独立线程捕获帧，不阻塞UI线程
 *     3. 通过事件通知机制传递帧数据，实现与检测服务的解耦
 *     4. 自动处理不同格式的帧（单通道、三通道、四通道）
 *     5. 支持动态获取实际帧尺寸
 *     
 *     v3.0 架构变更:
 *     - 移除了对YoloDetectionService的直接依赖
 *     - 改为通过FrameReady事件传递帧数据
 *     - 实现了IFrameSource接口，支持热插拔
 */

using System;
using System.Threading;
using OpenCvSharp;

namespace YoloDetector.YoloDetection
{
    /// <summary>
    /// RTSP帧捕获器（实现IFrameSource接口）
    /// 
    /// 这个类负责通过RTSP协议从网络摄像头获取视频帧，并通过事件通知外部。
    /// 
    /// 核心职责:
    /// 1. 连接RTSP流并捕获帧
    /// 2. 将帧转换为统一的BGR格式（适合YOLO检测）
    /// 3. 通过FrameReady事件传递帧数据
    /// 4. 管理捕获线程的生命周期
    /// 
    /// 使用示例:
    /// var capturer = new RtspFrameCapturer();
    /// capturer.FrameReady += (sender, frame) =>
    /// {
    ///     pipeline.ProcessFrame(frame);
    /// };
    /// capturer.Start("rtsp://admin:123456@192.168.1.100:554/stream");
    /// </summary>
    public class RtspFrameCapturer : IFrameSource
    {
        /// <summary>
        /// OpenCV视频捕获对象
        /// 
        /// 负责连接RTSP流并读取帧数据。
        /// </summary>
        private VideoCapture _capture;

        /// <summary>
        /// 帧捕获线程
        /// 
        /// 在后台运行，持续从VideoCapture读取帧并触发事件。
        /// </summary>
        private Thread _captureThread;

        /// <summary>
        /// 捕获线程运行标志
        /// 
        /// 控制捕获线程的启动和停止。
        /// </summary>
        private bool _isRunning;

        /// <summary>
        /// RTSP流地址
        /// 
        /// 存储当前连接的RTSP地址，用于重新连接。
        /// </summary>
        private string _rtspUrl;

        /// <summary>
        /// 释放标志
        /// 
        /// 防止多次释放资源。
        /// </summary>
        private bool _disposed;

        /// <summary>
        /// 锁对象
        /// 
        /// 保护VideoCapture的线程安全访问。
        /// </summary>
        private object _lockObj = new object();

        /// <summary>
        /// 帧计数
        /// 
        /// 用于跟踪捕获的帧数，在第一帧时输出格式信息。
        /// </summary>
        private int _frameCount = 0;

        /// <summary>
        /// 帧捕获器是否正在运行
        /// 
        /// 返回:
        /// true 如果捕获线程正在运行
        /// false 如果已停止
        /// </summary>
        public bool IsRunning => _isRunning;

        /// <summary>
        /// 获取帧宽度
        /// 
        /// 在Start成功后，这个属性会被设置为实际的帧宽度。
        /// 如果初始获取失败，默认值为1920。
        /// </summary>
        public int FrameWidth { get; private set; }

        /// <summary>
        /// 获取帧高度
        /// 
        /// 在Start成功后，这个属性会被设置为实际的帧高度。
        /// 如果初始获取失败，默认值为1080。
        /// </summary>
        public int FrameHeight { get; private set; }

        /// <summary>
        /// 帧就绪事件
        /// 
        /// 当捕获到新帧时触发，通知外部有新的视频帧可用。
        /// 事件参数是捕获到的Mat图像（已转换为BGR格式）。
        /// 
        /// 重要:
        /// - 事件参数是帧的克隆副本，外部可以自由使用和释放
        /// - 触发事件后，原始帧会被释放
        /// 
        /// 订阅示例:
        /// capturer.FrameReady += (sender, frame) =>
        /// {
        ///     // 处理帧
        ///     pipeline.ProcessFrame(frame);
        /// };
        /// </summary>
        public event EventHandler<Mat> FrameReady;

        /// <summary>
        /// 启动RTSP帧捕获
        /// 
        /// 连接到指定的RTSP流并开始捕获帧。
        /// 
        /// 参数:
        /// rtspUrl - RTSP流地址，格式: "rtsp://username:password@ip:port/stream"
        /// 
        /// 返回:
        /// true 如果启动成功
        /// false 如果启动失败（如地址无效、连接超时）
        /// 
        /// 启动流程:
        /// 1. 如果已在运行，先停止
        /// 2. 创建VideoCapture对象
        /// 3. 设置缓冲区大小为1（减少延迟）
        /// 4. 尝试连接RTSP流
        /// 5. 获取初始帧尺寸
        /// 6. 创建并启动捕获线程
        /// 
        /// 注意:
        /// - RTSP地址必须包含用户名和密码（如果摄像头启用了认证）
        /// - 启动时不会等待第一帧，第一帧会在捕获线程中获取
        /// </summary>
        /// <param name="rtspUrl">RTSP流地址</param>
        /// <returns>是否启动成功</returns>
        public bool Start(string rtspUrl)
        {
            // 1. 如果已在运行，先停止
            if (_isRunning)
            {
                Stop();
            }

            // 2. 保存RTSP地址
            _rtspUrl = rtspUrl;

            try
            {
                // 3. 创建VideoCapture对象
                _capture = new VideoCapture();

                // 4. 设置缓冲区大小为1（减少延迟）
                _capture.Set(VideoCaptureProperties.BufferSize, 1);

                // 5. 尝试连接RTSP流（带缓冲区参数）
                string rtspOptions = $"{rtspUrl}?buffer_size=1024000";

                if (!_capture.Open(rtspOptions))
                {
                    // 如果带参数连接失败，尝试不带参数
                    rtspOptions = rtspUrl;
                    if (!_capture.Open(rtspOptions))
                    {
                        return false;
                    }
                }

                // 6. 获取初始帧尺寸（可能不准确，后续会在捕获线程中更新）
                FrameWidth = (int)_capture.Get(VideoCaptureProperties.FrameWidth);
                FrameHeight = (int)_capture.Get(VideoCaptureProperties.FrameHeight);

                // 7. 设置默认尺寸（如果获取失败）
                if (FrameWidth <= 0) FrameWidth = 1920;
                if (FrameHeight <= 0) FrameHeight = 1080;

                System.Diagnostics.Debug.WriteLine($"[OpenCV] 初始帧尺寸: {FrameWidth}x{FrameHeight}");

                // 8. 设置运行标志
                _isRunning = true;

                // 9. 创建并启动捕获线程
                _captureThread = new Thread(CaptureLoop);
                _captureThread.IsBackground = true;
                _captureThread.Start();

                return true;
            }
            catch
            {
                // 10. 清理资源
                _capture?.Release();
                _capture = null;
                return false;
            }
        }

        /// <summary>
        /// 停止帧捕获
        /// 
        /// 停止捕获线程并释放VideoCapture资源。
        /// 
        /// 停止流程:
        /// 1. 设置_isRunning为false，通知线程退出
        /// 2. 等待捕获线程退出（最多3秒）
        /// 3. 释放VideoCapture资源
        /// </summary>
        public void Stop()
        {
            // 1. 设置停止标志
            _isRunning = false;

            // 2. 等待捕获线程退出（最多3秒）
            if (_captureThread != null && _captureThread.IsAlive)
            {
                _captureThread.Join(3000);
            }

            // 3. 释放VideoCapture资源
            lock (_lockObj)
            {
                _capture?.Release();
                _capture = null;
            }
        }

        /// <summary>
        /// 帧捕获循环（在捕获线程中运行）
        /// 
        /// 持续从VideoCapture读取帧，转换为BGR格式，并通过事件通知外部。
        /// 
        /// 循环流程:
        /// 1. 检查_isRunning标志
        /// 2. 从VideoCapture读取帧
        /// 3. 转换为BGR格式
        /// 4. 更新帧尺寸（仅第一帧）
        /// 5. 克隆帧并触发FrameReady事件
        /// 6. 释放临时资源
        /// 7. 短暂休眠（1ms），避免占用过多CPU
        /// 
        /// 注意:
        /// - 使用using语句确保Mat对象被正确释放
        /// - 使用锁保护VideoCapture的读取操作
        /// - 帧克隆后才触发事件，避免外部使用时帧被释放
        /// </summary>
        private void CaptureLoop()
        {
            while (_isRunning)
            {
                try
                {
                    // 1. 创建临时Mat对象（使用using确保释放）
                    using (var frame = new Mat())
                    {
                        Mat bgrFrame = null;
                        bool needDisposeBgr = false;

                        // 2. 使用锁保护VideoCapture读取
                        lock (_lockObj)
                        {
                            if (_capture == null)
                                break;

                            if (!_capture.Read(frame))
                            {
                                Thread.Sleep(50);
                                continue;
                            }
                        }

                        // 3. 检查帧是否为空
                        if (frame.Empty())
                        {
                            Thread.Sleep(50);
                            continue;
                        }

                        // 4. 转换为BGR格式
                        bgrFrame = ConvertToBgr(frame);
                        needDisposeBgr = (bgrFrame != frame);

                        // 5. 递增帧计数
                        _frameCount++;

                        // 6. 第一帧时更新实际帧尺寸
                        if (_frameCount == 1)
                        {
                            int actualWidth = bgrFrame.Cols;
                            int actualHeight = bgrFrame.Rows;
                            if (actualWidth > 0 && actualHeight > 0)
                            {
                                FrameWidth = actualWidth;
                                FrameHeight = actualHeight;
                                System.Diagnostics.Debug.WriteLine(
                                    $"[OpenCV] 获取到实际帧尺寸: {FrameWidth}x{FrameHeight}");
                            }
                        }

                        // 7. 克隆帧（传递给外部使用）
                        var matCopy = bgrFrame.Clone();

                        // 8. 触发FrameReady事件
                        FrameReady?.Invoke(this, matCopy);

                        // 9. 释放BGR帧（如果是转换后的）
                        if (needDisposeBgr && bgrFrame != null)
                        {
                            bgrFrame.Dispose();
                        }

                        // 10. 每60帧输出一次调试信息
                        if (_frameCount % 60 == 0)
                        {
                            System.Diagnostics.Debug.WriteLine(
                                $"[OpenCV] 已捕获 {_frameCount} 帧, 尺寸: {frame.Cols}x{frame.Rows}");
                        }

                        // 11. 短暂休眠，避免占用过多CPU
                        Thread.Sleep(1);
                    }
                }
                catch (Exception ex)
                {
                    // 12. 捕获异常并输出调试信息
                    System.Diagnostics.Debug.WriteLine($"[OpenCV] 捕获异常: {ex.Message}");
                    Thread.Sleep(100);
                }
            }
        }

        /// <summary>
        /// 释放资源（实现IDisposable接口）
        /// 
        /// 调用Stop方法释放所有资源。
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
                Stop();
            }

            _disposed = true;
        }

        /// <summary>
        /// 析构函数
        /// 
        /// 确保在对象被垃圾回收时释放资源。
        /// </summary>
        ~RtspFrameCapturer()
        {
            Dispose(false);
        }

        /// <summary>
        /// 将帧转换为BGR格式
        /// 
        /// YOLO模型要求输入图像为BGR格式（OpenCV默认格式），
        /// 这个方法确保无论原始帧是什么格式，都转换为BGR格式。
        /// 
        /// 支持的格式转换:
        /// - CV_8UC3（3通道8位）: 直接返回，已经是BGR格式
        /// - 单通道（灰度）: 使用GRAY2BGR转换
        /// - 四通道（RGBA）: 使用RGBA2BGR转换
        /// - 其他格式: 转换为CV_8UC3
        /// 
        /// 参数:
        /// src - 原始帧（任意格式）
        /// 
        /// 返回:
        /// 转换后的BGR格式帧
        /// 
        /// 注意:
        /// - 如果原始帧已经是BGR格式，返回的是同一个对象（不需要释放）
        /// - 如果进行了转换，返回的是新对象（需要释放）
        /// </summary>
        /// <param name="src">原始帧</param>
        /// <returns>BGR格式帧</returns>
        private Mat ConvertToBgr(Mat src)
        {
            // 1. 获取帧格式信息
            int channels = src.Channels();
            MatType type = src.Type();

            // 2. 第一帧时输出格式信息（用于调试）
            if (_frameCount == 1)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[OpenCV] 帧格式信息: Type={type}, Channels={channels}, Size={src.Cols}x{src.Rows}");
            }

            // 3. 如果已经是CV_8UC3格式，直接返回
            if (type == MatType.CV_8UC3)
            {
                return src;
            }

            // 4. 创建BGR帧对象
            Mat bgrFrame = new Mat();

            // 5. 根据通道数进行转换
            if (channels == 1)
            {
                // 单通道（灰度）→ BGR
                Cv2.CvtColor(src, bgrFrame, ColorConversionCodes.GRAY2BGR);
            }
            else if (channels == 4)
            {
                // 四通道（RGBA）→ BGR
                Cv2.CvtColor(src, bgrFrame, ColorConversionCodes.RGBA2BGR);
            }
            else if (channels == 3)
            {
                // 三通道但不是CV_8UC3 → 转换为CV_8UC3
                Mat temp8u = new Mat();
                src.ConvertTo(temp8u, MatType.CV_8UC3);
                bgrFrame = temp8u;
            }
            else
            {
                // 其他格式 → 创建空的BGR帧并复制
                bgrFrame = new Mat(src.Rows, src.Cols, MatType.CV_8UC3, new Scalar(0, 0, 0));
                src.CopyTo(bgrFrame);
            }

            // 6. 返回转换后的BGR帧
            return bgrFrame;
        }
    }
}