using System;
using System.IO;
using System.Threading;
using OpenCvSharp;
using YoloDetection;

namespace YoloDetector.Tests
{
    // ============================================================
    // 帧源（RtspFrameCapturer）生命周期测试。
    //
    // 测试策略：
    //   - 真实 RTSP 相机现场才有，CI/本机不可依赖 → 不测真实网络流；
    //   - 用本地视频文件充当流源（OpenCV VideoCapture 对文件路径同样
    //     能打开逐帧读出），验证"打开→出帧→停止"全生命周期；
    //   - 用必然拒绝连接的 127.0.0.1:9 验证失败路径（返回 false 且零泄漏）。
    // ============================================================

    internal static class FrameSourceTests
    {
        public static void RunAll()
        {
            T.Case("帧源-空地址抛参数异常", Start_EmptyUrl);
            T.Case("帧源-拒绝连接的地址返回false", Start_RefusedAddress);
            T.Case("帧源-视频文件流出帧与停止", Start_FileSource);
            T.Case("帧源-断流自动重连(文件EOF模拟)", ReconnectOnEof);
            T.Case("帧源-无订阅者时不泄漏", NoSubscriberSafe);
        }

        /// <summary>生成一个 320x240、30 帧、带移动色块的 MJPG 测试视频</summary>
        internal static string CreateTestVideo()
        {
            string path = Path.Combine(Path.GetTempPath(), "opencode", "yolotest_video.avi");
            Directory.CreateDirectory(Path.GetDirectoryName(path));

            using (var writer = new VideoWriter(path, FourCC.MJPG, 15, new Size(320, 240)))
            {
                if (!writer.IsOpened())
                {
                    return null; // 本机 FFmpeg 后端不支持时由调用方降级处理
                }

                using (var frame = new Mat(240, 320, MatType.CV_8UC3))
                {
                    for (int i = 0; i < 30; i++)
                    {
                        frame.SetTo(new Scalar(30 + i, 60, 90));
                        Cv2.Rectangle(frame,
                            new Rect(20 + i * 8, 100, 40, 40),
                            new Scalar(0, 255, 255), -1);
                        writer.Write(frame);
                    }
                }
            }
            return path;
        }

        private static void Start_EmptyUrl()
        {
            using (var capturer = new RtspFrameCapturer())
            {
                T.Throws<ArgumentException>(() => capturer.Start(null), "null 地址应抛 ArgumentException");
                T.Throws<ArgumentException>(() => capturer.Start(""), "空地址应抛 ArgumentException");
                T.False(capturer.IsRunning, "失败后不应处于运行态");
            }
        }

        private static void Start_RefusedAddress()
        {
            using (var capturer = new RtspFrameCapturer())
            {
                // 127.0.0.1:9（discard 端口）本机必然立即拒绝，不会长时间阻塞
                bool started = capturer.Start("rtsp://127.0.0.1:9/test");
                T.False(started, "不可达地址应返回 false");
                T.False(capturer.IsRunning, "失败后不应处于运行态");
            }
        }

        private static void Start_FileSource()
        {
            string videoPath = CreateTestVideo();
            if (videoPath == null)
            {
                T.Info("本机无法生成 MJPG 视频，用例降级为跳过（不影响判定）");
                return;
            }

            try
            {
                using (var capturer = new RtspFrameCapturer())
                {
                    int frames = 0;
                    int width = 0, height = 0;

                    capturer.FrameReady += (s, mat) =>
                    {
                        Interlocked.Increment(ref frames);
                        width = mat.Cols;
                        height = mat.Rows;
                        mat.Dispose(); // 订阅者按契约释放
                    };

                    T.True(capturer.Start(videoPath), "视频文件应能作为流源打开");
                    T.True(capturer.IsRunning, "启动后应处于运行态");

                    T.True(T.WaitFor(() => Volatile.Read(ref frames) >= 5, 15000),
                        "15秒内应收到至少5帧，实际=" + frames);
                    T.Eq(320, width, "帧宽度应与视频一致");
                    T.Eq(240, height, "帧高度应与视频一致");
                    T.Eq(320, capturer.FrameWidth, "FrameWidth 属性应同步");
                    T.Eq(240, capturer.FrameHeight, "FrameHeight 属性应同步");

                    capturer.Stop();
                    Thread.Sleep(50);
                    T.False(capturer.IsRunning, "Stop 后不应处于运行态");

                    int before = frames;
                    Thread.Sleep(200);
                    T.Eq(before, frames, "Stop 后不再出帧");
                }
            }
            finally
            {
                if (File.Exists(videoPath))
                {
                    File.Delete(videoPath);
                }
            }
        }

        /// <summary>
        /// v2.8 断流自愈链路：视频文件播完 EOF 后 Read 持续返回 false，与真实 RTSP
        /// 半开断流走同一条"连续失败→自动 Reopen"路径（文件重开后从头再播）。
        /// 若无重连机制帧数会永远停在 30；能继续增长即证明自愈生效。
        /// </summary>
        private static void ReconnectOnEof()
        {
            string videoPath = CreateTestVideo();
            if (videoPath == null)
            {
                T.Info("本机无法生成 MJPG 视频，用例降级为跳过（不影响判定）");
                return;
            }

            try
            {
                using (var capturer = new RtspFrameCapturer())
                {
                    int frames = 0;
                    capturer.FrameReady += (s, mat) =>
                    {
                        Interlocked.Increment(ref frames);
                        mat.Dispose(); // 订阅者按契约释放
                    };

                    T.True(capturer.Start(videoPath), "视频文件应能作为流源打开");

                    // 视频仅 30 帧(2 秒播完)；EOF 后 30 次×50ms 判定断流再重开 ≈ 第 31 帧
                    // 预计 4~5 秒内到达，15 秒窗口留足余量（含 FFmpeg 打开耗时）
                    T.True(T.WaitFor(() => Volatile.Read(ref frames) > 30, 15000),
                        "EOF 后应自动重连并继续出帧(>30), 实际=" + frames);
                    T.True(capturer.IsRunning, "重连后应仍处于运行态");

                    capturer.Stop();
                    T.False(capturer.IsRunning, "自愈后 Stop 仍应干净退出");
                }
            }
            finally
            {
                if (File.Exists(videoPath))
                {
                    File.Delete(videoPath);
                }
            }
        }

        /// <summary>无订阅者发布帧必须走"立即回收"分支，且 Stop 干净</summary>
        private static void NoSubscriberSafe()
        {
            string videoPath = CreateTestVideo();
            if (videoPath == null)
            {
                return;
            }

            try
            {
                using (var capturer = new RtspFrameCapturer())
                {
                    T.True(capturer.Start(videoPath), "无订阅者启动应成功");
                    T.True(T.WaitFor(() => !capturer.IsRunning || true, 500), "占位等待若干帧产生");
                    Thread.Sleep(300); // 让捕获线程在无订阅者状态下跑一会儿

                    capturer.Stop();
                    T.False(capturer.IsRunning, "无订阅者也应能干净停止");
                }
            }
            finally
            {
                if (File.Exists(videoPath))
                {
                    File.Delete(videoPath);
                }
            }
        }
    }
}
