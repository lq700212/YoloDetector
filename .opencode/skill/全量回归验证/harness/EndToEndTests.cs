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
            T.Case("端到端-姿态模型缺失自动降级为纯人员检测", EsdMissingPoseModelDegrades);
            T.Case("端到端-带ESD旁路视频流全链路", FullChainWithEsd);
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

        /// <summary>
        /// v2.2 关键降级行为：姿态模型缺失时 Start 必须成功（降级为纯人员检测 +
        /// 日志告警），绝不允许 ESD 附加功能把主业务拖死。
        /// </summary>
        private static void EsdMissingPoseModelDegrades()
        {
            string videoPath = FrameSourceTests.CreateTestVideo();
            if (videoPath == null)
            {
                return;
            }

            try
            {
                int resultEvents = 0;
                using (var controller = new VideoDetectionController(
                    previewSink: bmp => bmp.Dispose(),
                    detectionSink: list => Interlocked.Increment(ref resultEvents)))
                {
                    var options = MakeOptions(videoPath);
                    options.PoseModelPath = TestUtil.BinPath("Detection", "model", "no_such_pose.onnx");
                    options.EsdOptions = new YoloDetection.EsdAnalysisOptions(); // 参数齐但模型缺

                    // 不应抛 FileNotFoundException——内部 catch 后降级
                    controller.Start(options);
                    T.True(controller.IsRunning, "姿态模型缺失时预览必须照常启动");

                    T.True(T.WaitFor(() => Volatile.Read(ref resultEvents) > 0, 20000),
                        "降级后人员检测结果必须照常发布");
                    controller.Stop();
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

        /// <summary>带 ESD 旁路的完整链路：真姿态模型装配 + 预览/结果事件照常</summary>
        private static void FullChainWithEsd()
        {
            string videoPath = FrameSourceTests.CreateTestVideo();
            if (videoPath == null)
            {
                return;
            }

            try
            {
                int bitmaps = 0, resultEvents = 0, contactEvents = 0;

                using (var controller = new VideoDetectionController(
                    previewSink: bmp => { Interlocked.Increment(ref bitmaps); bmp.Dispose(); },
                    detectionSink: list => Interlocked.Increment(ref resultEvents)))
                {
                    // 触摸翻转事件挂计数（视频里无人摸杆，预期不触发——验证"不触发也不崩"）
                    controller.EsdContactChanged += (s, e) => Interlocked.Increment(ref contactEvents);

                    var options = MakeOptions(videoPath);
                    options.PoseModelPath = TestUtil.BinPath("Detection", "model", "yolo11n-pose.onnx");
                    options.EsdOptions = new YoloDetection.EsdAnalysisOptions
                    {
                        RoiX = 0.4f, RoiY = 0.2f, RoiW = 0.2f, RoiH = 0.3f,
                        HoldDurationMs = 1500,
                        DrawOverlay = true // 叠加渲染器也一并装配，验证绘制路径
                    };

                    controller.Start(options);
                    T.True(controller.IsRunning, "带ESD旁路启动应成功（姿态模型已加载）");

                    T.True(T.WaitFor(() => Volatile.Read(ref resultEvents) > 0, 20000),
                        "检测结果事件应正常到达");
                    T.True(T.WaitFor(() => Volatile.Read(ref bitmaps) >= 3, 20000),
                        "预览 Bitmap 应持续输出(含叠加绘制路径), 实际=" + bitmaps);

                    controller.Stop();
                    T.False(controller.IsRunning, "Stop 后应停止");
                }

                T.Info("ESD端到端: Bitmap=" + bitmaps + " 结果=" + resultEvents +
                       " 触摸翻转=" + contactEvents + "(合成图无人,0属正常)");
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
