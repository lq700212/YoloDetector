using System;
using System.Threading;
using OpenCvSharp;

namespace YoloDetection
{
    /// <summary>
    /// 基于 OpenCV VideoCapture 的 RTSP 帧捕获器（IFrameSource 默认实现）。
    ///
    /// 线程安全设计要点：
    ///   1. 停止标志为 volatile，保证捕获线程及时看到停止请求
    ///   2. VideoCapture 的 Open/Read/Release 全部经由 _captureLock 串行化，
    ///      Stop 即使在 Read 阻塞期间被调用，也必须等当前 Read 结束后才会释放设备
    ///   3. Start 失败路径完整释放已创建的 VideoCapture，不留泄漏
    ///   4. 每一帧的处理路径均有 finally 兜底释放临时 Mat
    ///
    /// 帧所有权契约：
    ///   FrameReady 传出的 Mat 归订阅者所有，订阅者用完必须 Dispose。
    /// </summary>
    public class RtspFrameCapturer : IFrameSource
    {
        private readonly object _captureLock = new object();

        private VideoCapture _capture;
        private Thread _captureThread;
        private volatile bool _stopRequested;

        // 仅工作线程写、外部近似读的统计字段
        private long _frameCount;

        public bool IsRunning => !_stopRequested && _captureThread != null && _captureThread.IsAlive;

        public int FrameWidth { get; private set; }

        public int FrameHeight { get; private set; }

        public event EventHandler<Mat> FrameReady;

        /// <summary>
        /// 连接 RTSP 流并开始后台捕获线程。
        /// </summary>
        /// <param name="rtspUrl">RTSP 地址（含认证信息时格式 rtsp://user:pass@ip:port/...）</param>
        /// <returns>true=启动成功</returns>
        public bool Start(string rtspUrl)
        {
            if (string.IsNullOrEmpty(rtspUrl))
                throw new ArgumentException("RTSP地址不能为空", nameof(rtspUrl));

            if (IsRunning)
            {
                Stop();
            }

            lock (_captureLock)
            {
                _stopRequested = false;
                _frameCount = 0;

                VideoCapture capture = null;
                try
                {
                    capture = new VideoCapture();
                    capture.Set(VideoCaptureProperties.BufferSize, 1);

                    // 先尝试带缓冲参数连接（部分 FFmpeg 后端可降低延迟），失败再退回原始地址
                    if (!TryOpen(capture, rtspUrl + "?buffer_size=1024000") &&
                        !TryOpen(capture, rtspUrl))
                    {
                        capture.Dispose(); // 失败路径：必须释放，避免句柄/native内存泄漏
                        return false;
                    }

                    int w = (int)capture.Get(VideoCaptureProperties.FrameWidth);
                    int h = (int)capture.Get(VideoCaptureProperties.FrameHeight);
                    FrameWidth = w > 0 ? w : 1920;
                    FrameHeight = h > 0 ? h : 1080;

                    _capture = capture;
                }
                catch
                {
                    // 初始化中途异常：同样必须释放已创建的捕获对象
                    if (capture != null)
                    {
                        try { capture.Dispose(); } catch { }
                    }
                    return false;
                }
            }

            var thread = new Thread(CaptureLoop)
            {
                IsBackground = true,
                Name = "RtspCaptureLoop"
            };
            _captureThread = thread;
            thread.Start();

            return true;
        }

        public void Stop()
        {
            _stopRequested = true;

            Thread thread = _captureThread;
            if (thread != null && thread.IsAlive)
            {
                // 有界等待：Read 单次阻塞时间有限（一帧周期），正常瞬间退出；
                // 超时不影响安全——后续 Release 由锁保护，会等 Read 完成后才执行
                thread.Join(TimeSpan.FromSeconds(3));
            }
            _captureThread = null;

            lock (_captureLock)
            {
                if (_capture != null)
                {
                    try
                    {
                        _capture.Release();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine("[Capture] Release异常: " + ex.Message);
                    }
                    finally
                    {
                        _capture.Dispose();
                        _capture = null;
                    }
                }
            }
        }

        // ==================== 工作线程 ====================

        private void CaptureLoop()
        {
            while (!_stopRequested)
            {
                Mat frame = new Mat();
                Mat bgrFrame = null;

                try
                {
                    bool readOk;
                    lock (_captureLock)
                    {
                        if (_capture == null || _stopRequested)
                        {
                            break;
                        }
                        readOk = _capture.Read(frame);
                    }

                    if (!readOk)
                    {
                        Thread.Sleep(50); // 流暂时无数据（网络抖动），稍后重试
                        continue;
                    }

                    if (frame.Empty())
                    {
                        continue;
                    }

                    bgrFrame = ConvertToBgr(frame);
                    _frameCount++;

                    if (_frameCount == 1)
                    {
                        // 第一帧时修正实际分辨率（Open 属性可能不准）
                        if (bgrFrame.Cols > 0 && bgrFrame.Rows > 0)
                        {
                            FrameWidth = bgrFrame.Cols;
                            FrameHeight = bgrFrame.Rows;
                        }
                        System.Diagnostics.Debug.WriteLine(
                            $"[Capture] 实际帧尺寸: {FrameWidth}x{FrameHeight}");
                    }

                    // 克隆后发布；所有权移交给订阅者
                    Mat copy = bgrFrame.Clone();
                    var handler = FrameReady;
                    if (handler != null)
                    {
                        handler(this, copy);
                    }
                    else
                    {
                        copy.Dispose(); // 无订阅者时立即回收
                    }

                    if (_frameCount % 300 == 0)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"[Capture] 已捕获 {_frameCount} 帧, 尺寸: {FrameWidth}x{FrameHeight}");
                    }

                    // 轻微让步，避免空转过占 CPU
                    Thread.Sleep(1);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Capture] 捕获异常: {ex.Message}");
                    Thread.Sleep(100);
                }
                finally
                {
                    // ConvertToBgr 在已是 BGR8 时返回的是 frame 本身，避免重复释放
                    if (bgrFrame != null && !ReferenceEquals(bgrFrame, frame))
                    {
                        bgrFrame.Dispose();
                    }
                    frame.Dispose();
                }
            }
        }

        public void Dispose()
        {
            Stop();
            GC.SuppressFinalize(this);
        }

        // ==================== 内部辅助 ====================

        private static bool TryOpen(VideoCapture capture, string url)
        {
            try
            {
                return capture.Open(url);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 将任意格式帧统一转换为 BGR8（YOLO 推理输入格式）。
        /// 返回值可能是 src 本身（已是 BGR8）或新 Mat（调用方负责 Dispose）。
        /// </summary>
        private static Mat ConvertToBgr(Mat src)
        {
            if (src.Type() == MatType.CV_8UC3)
            {
                return src;
            }

            Mat bgr = new Mat();
            int channels = src.Channels();

            if (channels == 1)
            {
                Cv2.CvtColor(src, bgr, ColorConversionCodes.GRAY2BGR);
            }
            else if (channels == 4)
            {
                Cv2.CvtColor(src, bgr, ColorConversionCodes.RGBA2BGR);
            }
            else
            {
                src.ConvertTo(bgr, MatType.CV_8UC3);
            }

            return bgr;
        }
    }
}
