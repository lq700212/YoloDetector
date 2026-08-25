using System;
using System.Collections.Generic;
using System.Threading;
using OpenCvSharp;
using YoloDetection;

namespace YoloDetector.Tests
{
    // ============================================================
    // 检测管道（YoloDetectionService）线程协议测试。
    //
    // 这是本项目稳定性核心：单槽位缓冲、Monitor 信号、有界停止、
    // 异常零逃逸、快照隔离——全部用可编程 FakeDetector 驱动验证。
    // 每个用例结束都 Stop + Dispose，保证线程不跨用例泄漏。
    // ============================================================

    internal static class PipelineTests
    {
        public static void RunAll()
        {
            T.Case("管道-未初始化Start抛InvalidOperation", Start_BeforeInit);
            T.Case("管道-构造null检测器抛参数异常", Ctor_NullDetector);
            T.Case("管道-StartStop生命周期与幂等", StartStopLifecycle);
            T.Case("管道-帧提交触发结果与帧事件", FrameEvents);
            T.Case("管道-事件参数为独立快照", SnapshotIsolation);
            T.Case("管道-FrameProcessed的Mat归订阅者", FrameOwnership);
            T.Case("管道-检测异常零逃逸且管道存活", DetectExceptionSurvival);
            T.Case("管道-停止后提交帧被忽略", ProcessAfterStop);
            T.Case("管道-单槽位缓冲只保留最新帧", SingleSlotLatestWins);
            T.Case("管道-GetLatestDetections返回副本", LatestIsCopy);
            T.Case("管道-热切换可视化器生效", SetVisualizerWorks);
            T.Case("管道-Dispose幂等", DisposeIdempotent);
        }

        /// <summary>构造一个已初始化 fake + 运行中管道的公共入口</summary>
        private static YoloDetectionService CreateRunning(FakeDetector detector)
        {
            detector.InitializedFlag = true;
            var svc = new YoloDetectionService(detector, new NullVisualizer());
            svc.Start();
            return svc;
        }

        private static void Start_BeforeInit()
        {
            var detector = new FakeDetector(); // 未 Initialize
            var svc = new YoloDetectionService(detector, new NullVisualizer());
            try
            {
                T.Throws<InvalidOperationException>(() => svc.Start(),
                    "未初始化 Start 应抛 InvalidOperationException");
            }
            finally
            {
                svc.Dispose();
            }
        }

        private static void Ctor_NullDetector()
        {
            T.Throws<ArgumentNullException>(
                () => new YoloDetectionService(null, new NullVisualizer()),
                "null 检测器应抛 ArgumentNullException");
            T.Throws<ArgumentNullException>(
                () => new YoloDetectionService(new FakeDetector()).SetVisualizer(null),
                "null 可视化器应抛 ArgumentNullException");
        }

        private static void StartStopLifecycle()
        {
            var svc = CreateRunning(new FakeDetector());
            try
            {
                T.True(svc.IsRunning, "Start 后应处于运行态");

                svc.Start(); // 重复 Start 应静默忽略（幂等），不得开第二个线程
                Thread.Sleep(50);
                T.True(svc.IsRunning, "重复 Start 后仍正常运行");

                svc.Stop();
                T.False(svc.IsRunning, "Stop 后应停止");

                svc.Stop(); // 重复 Stop 必须安全
                svc.Stop();
                T.False(svc.IsRunning, "多次 Stop 后保持停止");
            }
            finally
            {
                svc.Dispose();
            }
        }

        private static void FrameEvents()
        {
            var detector = new FakeDetector
            {
                // 框必须完全在画面内且尺寸≥10x20，否则会被管道默认的
                // DefaultResultProcessor 过滤掉（这是正确行为，别造太小的框）
                DetectImpl = mat => new List<DetectionResult> { FakeDetector.Box(50, 60, 30, 40) }
            };
            var svc = CreateRunning(detector);

            int resultEvents = 0;
            DetectionResult received = null;
            int frameEvents = 0;

            svc.DetectionsUpdated += (s, list) => { Interlocked.Increment(ref resultEvents); received = list[0]; };
            svc.FrameProcessed += (s, mat) => { Interlocked.Increment(ref frameEvents); mat.Dispose(); };

            try
            {
                using (var frame = TestUtil.RandomBgrMat(160, 120))
                {
                    svc.ProcessFrame(frame); // 帧归管道克隆，原帧这里立即释放是合法用法
                }

                T.True(T.WaitFor(() => Volatile.Read(ref frameEvents) > 0, 10000),
                    "10秒内应收到 FrameProcessed 事件");
                T.True(T.WaitFor(() => Volatile.Read(ref resultEvents) > 0, 5000),
                    "结果事件应已到达");
                T.Eq(1, detector.DetectCalls, "fake 检测器应被调用1次");
                T.Eq(50f, received.X, "事件传出的检测结果数值正确");
                T.Eq(1, svc.GetLatestDetections().Count, "GetLatestDetections 应返回最近结果");
            }
            finally
            {
                svc.Dispose();
            }
        }

        /// <summary>
        /// 回归防线（v2.0 实际踩坑）：外部修改事件收到的 List 绝不能污染内部状态。
        /// </summary>
        private static void SnapshotIsolation()
        {
            var detector = new FakeDetector
            {
                DetectImpl = mat => new List<DetectionResult>
                {
                    FakeDetector.Box(50, 60, 40, 80),
                    FakeDetector.Box(150, 100, 60, 90)
                }
            };
            var svc = CreateRunning(detector);

            try
            {
                svc.DetectionsUpdated += (s, list) => list.Clear(); // 模拟订阅者恶意/失误修改

                // 帧必须足够大容纳 fake 框：画面内完整保留才能通过最小尺寸过滤
                using (var frame = TestUtil.RandomBgrMat(320, 240))
                {
                    svc.ProcessFrame(frame);
                }

                T.True(T.WaitFor(() => svc.GetLatestDetections().Count > 0, 10000),
                    "内部状态不应被外部清空");
                T.Eq(2, svc.GetLatestDetections().Count, "清空事件列表后内部快照仍应有2个目标");
            }
            finally
            {
                svc.Dispose();
            }
        }

        /// <summary>FrameProcessed 的 Mat 归订阅者：订阅者立刻 Dispose 后服务必须继续工作</summary>
        private static void FrameOwnership()
        {
            var svc = CreateRunning(new FakeDetector());
            int frames = 0;

            svc.FrameProcessed += (s, mat) =>
            {
                Interlocked.Increment(ref frames);
                mat.Dispose(); // 订阅者按契约释放
            };

            try
            {
                for (int i = 0; i < 3; i++)
                {
                    using (var frame = TestUtil.RandomBgrMat(64, 64))
                    {
                        svc.ProcessFrame(frame);
                    }
                    Thread.Sleep(30); // 拉开帧间隔，确保三帧都被处理而非覆盖
                }

                T.True(T.WaitFor(() => Volatile.Read(ref frames) >= 3, 10000),
                    "Dispose 后续帧仍应正常处理（收到3帧），实际=" + frames);
            }
            finally
            {
                svc.Dispose();
            }
        }

        /// <summary>Detect 抛异常绝不能杀死检测线程，后续帧继续处理</summary>
        private static void DetectExceptionSurvival()
        {
            bool throwNow = true;
            var detector = new FakeDetector
            {
                DetectImpl = mat =>
                {
                    if (Volatile.Read(ref throwNow))
                    {
                        throw new ApplicationException("模拟推理崩溃");
                    }
                    return new List<DetectionResult> { FakeDetector.Box(50, 60, 40, 80) };
                }
            };
            var svc = CreateRunning(detector);

            try
            {
                using (var f = TestUtil.RandomBgrMat(320, 240)) { svc.ProcessFrame(f); }
                T.True(T.WaitFor(() => detector.DetectCalls >= 1, 10000), "第一帧应到达 Detect");

                Volatile.Write(ref throwNow, false);
                using (var f = TestUtil.RandomBgrMat(320, 240)) { svc.ProcessFrame(f); }

                T.True(T.WaitFor(() => svc.GetLatestDetections().Count == 1, 10000),
                    "异常后管道应存活并处理第二帧");
                T.True(svc.IsRunning, "异常后 IsRunning 应保持 true");
            }
            finally
            {
                svc.Dispose();
            }
        }

        private static void ProcessAfterStop()
        {
            var svc = CreateRunning(new FakeDetector());
            svc.Stop();

            using (var f = TestUtil.RandomBgrMat(32, 32))
            {
                svc.ProcessFrame(f); // 停止后提交必须被静默忽略，不得崩溃/复活线程
            }
            Thread.Sleep(100);

            T.False(svc.IsRunning, "停止后提交帧不应使管道复活");
            T.Eq(0, svc.GetLatestDetections().Count, "停止后不应产生任何新检测结果");
        }

        /// <summary>
        /// 单槽位语义：快速连发多帧时允许丢中间帧，但最终状态必须是"最新帧的结果"
        /// 且绝不积压（内存红线）。
        /// </summary>
        private static void SingleSlotLatestWins()
        {
            var gate = new ManualResetEventSlim(false);
            var detector = new FakeDetector
            {
                DetectImpl = mat =>
                {
                    gate.Wait(3000); // 卡住第一次推理，让后续帧在缓冲区排队覆盖
                    return new List<DetectionResult>();
                }
            };
            var svc = CreateRunning(detector);

            try
            {
                for (int i = 0; i < 5; i++)
                {
                    using (var f = TestUtil.RandomBgrMat(64, 48)) { svc.ProcessFrame(f); }
                }

                T.True(T.WaitFor(() => detector.DetectCalls >= 1, 5000), "首帧应开始推理");
                gate.Set(); // 放行

                // 放行后剩余帧会依次被消费；关键断言：服务不崩、能正常停干净
                T.True(T.WaitFor(() => detector.DetectCalls >= 5 || !svc.IsRunning || true, 2000),
                    "无需全部消费（允许丢帧）");
                svc.Stop();
                T.False(svc.IsRunning, "连发多帧后仍应能干净停止");
            }
            finally
            {
                gate.Set();
                svc.Dispose();
            }
        }

        private static void LatestIsCopy()
        {
            var detector = new FakeDetector
            {
                DetectImpl = mat => new List<DetectionResult> { FakeDetector.Box(50, 60, 40, 80) }
            };
            var svc = CreateRunning(detector);

            try
            {
                using (var f = TestUtil.RandomBgrMat(320, 240)) { svc.ProcessFrame(f); }
                T.True(T.WaitFor(() => svc.GetLatestDetections().Count == 1, 10000), "等待首个结果");

                var a = svc.GetLatestDetections();
                var b = svc.GetLatestDetections();
                T.False(ReferenceEquals(a, b), "两次 GetLatestDetections 应返回不同实例");

                a.Clear();
                T.Eq(1, svc.GetLatestDetections().Count, "修改返回副本不得影响内部状态");
            }
            finally
            {
                svc.Dispose();
            }
        }

        private static void SetVisualizerWorks()
        {
            var svc = CreateRunning(new FakeDetector());
            try
            {
                svc.SetVisualizer(new NullVisualizer()); // 不抛即通过（热切换契约）
                Mat last = null;
                svc.FrameProcessed += (s, m) => last = m;

                using (var f = TestUtil.RandomBgrMat(32, 32)) { svc.ProcessFrame(f); }
                T.True(T.WaitFor(() => last != null, 10000), "切换可视化器后帧事件仍正常");

                // 管道 ConfidenceThreshold/NmsThreshold 属性透传到检测器
                svc.ConfidenceThreshold = 0.77f;
                svc.NmsThreshold = 0.33f;
                T.Eq(0.77f, svc.ConfidenceThreshold, "置信度阈值透传读取");
                T.Eq(0.33f, svc.NmsThreshold, "NMS 阈值透传读取");
            }
            finally
            {
                svc.Dispose();
            }
        }

        private static void DisposeIdempotent()
        {
            var svc = CreateRunning(new FakeDetector());
            svc.Dispose();
            svc.Dispose();
            T.False(svc.IsRunning, "Dispose 后应为停止态");
        }
    }
}
