using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using YoloDetector.App;

namespace YoloDetector.Tests
{
    // ============================================================
    // VideoDetectionController 端到端测试。
    //
    // 用"视频文件路径当 RTSP 地址 + 现场真实模型"跑通完整链路：
    //   帧源 → 管道(真推理) → 可视化 → previewSink(Bitmap) / detectionSink(快照)
    // 这是除 GUI 外最接近现场真实运行的验证。
    // ============================================================

    internal static class EndToEndTests
    {
        public static void RunAll()
        {
            T.Case("端到端-options与地址参数校验", ArgumentValidation);
            T.Case("端到端-模型不存在抛异常且状态干净", MissingModelRecovers);
            T.Case("端到端-全链路视频文件流跑通", FullChainWithFileSource);
        }

        private static DetectionStartupOptions MakeOptions(string url)
        {
            return new DetectionStartupOptions
            {
                ModelPath = TestUtil.BinPath("Detection", "model", "yolo26n.onnx"),
                ConfidenceThreshold = 0.25f,
                NmsThreshold = 0.5f,
                RtspUrl = url,
                VisualizerType = YoloDetection.VisualizerType.YoloBuiltin
            };
        }

        private static void ArgumentValidation()
        {
            using (var controller = new VideoDetectionController(bmp => bmp.Dispose(), list => { }))
            {
                T.Throws<ArgumentNullException>(() => controller.Start(null), "null options 应抛 ArgumentNullException");

                var badUrl = MakeOptions("");
                T.Throws<ArgumentException>(() => controller.Start(badUrl), "空 RtspUrl 应抛 ArgumentException");
                T.False(controller.IsRunning, "失败后不应处于运行态");
            }
        }

        /// <summary>
        /// 回归防线：Start 失败（模型缺失）后内部状态必须干净，
        /// 修正参数后再次 Start 必须能成功——不允许"一次失败永远失败"。
        /// </summary>
        private static void MissingModelRecovers()
        {
            string videoPath = FrameSourceTests.CreateTestVideo();
            if (videoPath == null)
            {
                T.Info("本机无法生成测试视频，用例降级为跳过（不影响判定）");
                return;
            }

            try
            {
                using (var controller = new VideoDetectionController(bmp => bmp.Dispose(), list => { }))
                {
                    var badModel = MakeOptions(videoPath);
                    badModel.ModelPath = TestUtil.BinPath("Detection", "model", "no_such.onnx");

                    T.Throws<FileNotFoundException>(() => controller.Start(badModel),
                        "模型不存在应抛 FileNotFoundException");
                    T.False(controller.IsRunning, "失败后不应处于运行态");

                    // 修正模型路径重试：必须成功
                    controller.Start(MakeOptions(videoPath));
                    T.True(controller.IsRunning, "失败恢复后重新 Start 应成功");
                    controller.Stop();
                    T.False(controller.IsRunning, "Stop 后应停止");
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

        /// <summary>完整链路：收到预览 Bitmap、收到检测快照，Stop/Dispose 幂等</summary>
        private static void FullChainWithFileSource()
        {
            string videoPath = FrameSourceTests.CreateTestVideo();
            if (videoPath == null)
            {
                return;
            }

            try
            {
                int bitmaps = 0;
                int lastBitmapW = 0, lastBitmapH = 0;
                int resultEvents = 0;
                List<long> disposeAfterUseErrors = new List<long>();

                using (var controller = new VideoDetectionController(
                    previewSink: bmp =>
                    {
                        // 模拟 UI 层：拿到即记录尺寸再释放（所有权归 sink）
                        Interlocked.Increment(ref bitmaps);
                        lastBitmapW = bmp.Width;
                        lastBitmapH = bmp.Height;
                        bmp.Dispose();
                    },
                    detectionSink: list => { Interlocked.Increment(ref resultEvents); }))
                {
                    controller.Start(MakeOptions(videoPath));
                    T.True(controller.IsRunning, "启动后应处于运行态");
                    T.True(controller.IsDetectorReady, "模型应已就绪（跨会话复用前提）");

                    T.True(T.WaitFor(() => Volatile.Read(ref resultEvents) > 0, 20000),
                        "20秒内应收到检测结果事件");
                    T.True(T.WaitFor(() => Volatile.Read(ref bitmaps) >= 3, 20000),
                        "20秒内应收至少3帧预览 Bitmap，实际=" + bitmaps);

                    T.Eq(320, lastBitmapW, "预览帧宽度=320");
                    T.Eq(240, lastBitmapH, "预览帧高度=240");

                    // 幂等：连续 Stop 不崩；Dispose 内部也调 Stop
                    controller.Stop();
                    controller.Stop();
                    T.False(controller.IsRunning, "Stop 后应停止");
                }

                T.Info("端到端共收到 Bitmap=" + bitmaps + " 结果事件=" + resultEvents);
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
