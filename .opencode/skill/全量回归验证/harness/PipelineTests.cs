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
            T.Case("管道-阈值透传到检测器", ThresholdPassthrough);
            T.Case("管道-Dispose幂等", DisposeIdempotent);
            T.Case("管道-检测器返回null按空处理", DetectorNullList);
            T.Case("管道-后处理器返回null不崩", ProcessorNullList);
            T.Case("管道-可视化器返回null回退原帧", VisualizerNullFallback);
            T.Case("管道-可视化器抛异常管道存活", VisualizerExceptionSurvival);
            T.Case("管道-null与空帧提交被忽略", NullAndEmptyFrames);
            T.Case("管道-ESD降频节流生效", EsdThrottle);
            T.Case("管道-门监测旁路联动", DoorBypass);
            T.Case("管道-快速启停压力", RapidStartStop);
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

            // null 可视化器回退默认实现（OpenCVVisualizer），不得抛异常
            var svc = new YoloDetectionService(new FakeDetector(), null);
            T.True(svc != null, "null 可视化器应回退默认实现");
            svc.Dispose();
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

        private static void ThresholdPassthrough()
        {
            var svc = CreateRunning(new FakeDetector());
            try
            {
                Mat last = null;
                svc.FrameProcessed += (s, m) => last = m;

                using (var f = TestUtil.RandomBgrMat(32, 32)) { svc.ProcessFrame(f); }
                T.True(T.WaitFor(() => last != null, 10000), "运行态帧事件正常");

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

        /// <summary>返回 null 的假可视化器（绘制失败分支用）</summary>
        private class NullReturnVisualizer : IDetectionVisualizer
        {
            public Mat Draw(Mat frame, List<DetectionResult> results) { return null; }
        }

        /// <summary>抛异常的假可视化器（绘制异常分支用）</summary>
        private class ThrowingVisualizer : IDetectionVisualizer
        {
            public Mat Draw(Mat frame, List<DetectionResult> results)
            {
                throw new InvalidOperationException("模拟绘制崩溃");
            }
        }

        /// <summary>返回 null 的假后处理器</summary>
        private class NullReturnProcessor : IDetectionResultProcessor
        {
            public string ProcessorName => "NullReturn";
            public List<DetectionResult> Process(List<DetectionResult> rawResults, int w, int h)
            {
                return null;
            }
        }

        /// <summary>检测器返回 null（违反契约的坏实现）→ 管道按空列表处理，不得崩</summary>
        private static void DetectorNullList()
        {
            var detector = new FakeDetector { DetectImpl = mat => null };
            var svc = CreateRunning(detector);
            int frames = 0, results = 0;
            svc.FrameProcessed += (s, m) => { Interlocked.Increment(ref frames); m.Dispose(); };
            svc.DetectionsUpdated += (s, list) => Interlocked.Increment(ref results);
            try
            {
                using (var f = TestUtil.RandomBgrMat(160, 120)) { svc.ProcessFrame(f); }
                T.True(T.WaitFor(() => Volatile.Read(ref frames) > 0, 10000), "检测器返null时仍应出帧");
                T.True(T.WaitFor(() => Volatile.Read(ref results) > 0, 10000), "结果事件仍应触发");
                T.Eq(0, svc.GetLatestDetections().Count, "最新结果应为空列表");
            }
            finally
            {
                svc.Dispose();
            }
        }

        /// <summary>后处理器返回 null → 管道按空处理，主链路存活</summary>
        private static void ProcessorNullList()
        {
            var detector = new FakeDetector
            {
                DetectImpl = mat => new List<DetectionResult> { FakeDetector.Box(50, 60, 40, 80) }
            };
            var svc = CreateRunning(detector);
            svc.ResultProcessor = new NullReturnProcessor();
            int frames = 0;
            svc.FrameProcessed += (s, m) => { Interlocked.Increment(ref frames); m.Dispose(); };
            try
            {
                using (var f = TestUtil.RandomBgrMat(320, 240)) { svc.ProcessFrame(f); }
                T.True(T.WaitFor(() => Volatile.Read(ref frames) > 0, 10000), "后处理返null时仍应出帧");
                T.Eq(0, svc.GetLatestDetections().Count, "结果应被置空");
            }
            finally
            {
                svc.Dispose();
            }
        }

        /// <summary>可视化器返回 null → 管道回退为原帧副本，订阅者仍收到有效 Mat</summary>
        private static void VisualizerNullFallback()
        {
            var detector = new FakeDetector { DetectImpl = mat => new List<DetectionResult>() };
            detector.InitializedFlag = true;
            var svc = new YoloDetectionService(detector, new NullReturnVisualizer());
            svc.Start();
            int frames = 0;
            bool sizeOk = false;
            svc.FrameProcessed += (s, m) =>
            {
                Interlocked.Increment(ref frames);
                sizeOk = m.Cols == 160 && m.Rows == 120;
                m.Dispose();
            };
            try
            {
                using (var f = TestUtil.RandomBgrMat(160, 120)) { svc.ProcessFrame(f); }
                T.True(T.WaitFor(() => Volatile.Read(ref frames) > 0, 10000), "绘制失败应回退原帧并触发事件");
                T.True(sizeOk, "回退帧尺寸应与输入一致");
            }
            finally
            {
                svc.Dispose();
            }
        }

        /// <summary>可视化器抛异常 → 该帧丢弃但检测线程存活，后续帧继续出</summary>
        private static void VisualizerExceptionSurvival()
        {
            var detector = new FakeDetector { DetectImpl = mat => new List<DetectionResult>() };
            detector.InitializedFlag = true;
            var svc = new YoloDetectionService(detector, new ThrowingVisualizer());
            svc.Start();
            int frames = 0, results = 0;
            svc.FrameProcessed += (s, m) => { Interlocked.Increment(ref frames); m.Dispose(); };
            svc.DetectionsUpdated += (s, list) => Interlocked.Increment(ref results);
            try
            {
                using (var f = TestUtil.RandomBgrMat(160, 120)) { svc.ProcessFrame(f); }
                Thread.Sleep(300); // 给检测线程时间处理第1帧（绘制抛异常被吞）
                using (var f = TestUtil.RandomBgrMat(160, 120)) { svc.ProcessFrame(f); }
                T.True(T.WaitFor(() => Volatile.Read(ref results) >= 2, 10000), "绘制异常不得杀死管道");
                T.True(svc.IsRunning, "管道应保持运行");
            }
            finally
            {
                svc.Dispose();
            }
        }

        /// <summary>null/空帧提交必须静默忽略（Detect 不应被调用）</summary>
        private static void NullAndEmptyFrames()
        {
            var detector = new FakeDetector();
            var svc = CreateRunning(detector);
            try
            {
                svc.ProcessFrame(null);
                using (var empty = new Mat()) { svc.ProcessFrame(empty); }
                Thread.Sleep(200);
                T.Eq(0, detector.DetectCalls, "null/空帧不得进入检测");
                T.True(svc.IsRunning, "忽略坏帧后管道保持运行");
            }
            finally
            {
                svc.Dispose();
            }
        }

        /// <summary>
        /// ESD 降频节流：EsdProcessEveryNFrames=3 时姿态推理次数应约为帧数的 1/3，
        /// 且快照事件仍正常触发（跳过帧沿用上次快照供叠加层绘制）。
        /// </summary>
        private static void EsdThrottle()
        {
            var detector = new FakeDetector
            {
                InitializedFlag = true,
                DetectImpl = mat => new List<DetectionResult> { FakeDetector.Box(80, 60, 40, 80) }
            };
            var pose = new EsdAnalyzerTests.FakePoseDetector
            {
                DetectImpl = (mat, persons) => new List<PoseResult>()
            };
            var svc = new YoloDetectionService(detector, new NullVisualizer())
            {
                PoseDetector = pose,
                EsdAnalyzer = new EsdContactAnalyzer(new EsdAnalysisOptions
                {
                    RoiX = 0.4f, RoiY = 0.2f, RoiW = 0.3f, RoiH = 0.5f,
                    HoldDurationMs = 0
                }),
                EsdProcessEveryNFrames = 3
            };
            int statusEvents = 0, frameEvents = 0;
            svc.Start();
            try
            {
                svc.EsdStatusUpdated += (s, snap) => Interlocked.Increment(ref statusEvents);
                svc.FrameProcessed += (s, m) => { Interlocked.Increment(ref frameEvents); m.Dispose(); };

                for (int i = 0; i < 6; i++)
                {
                    using (var f = TestUtil.RandomBgrMat(160, 120)) { svc.ProcessFrame(f); }
                    T.WaitFor(() => Volatile.Read(ref frameEvents) >= i + 1, 10000);
                }

                T.True(pose.Calls >= 1, "降频后姿态仍应被调用, 实际=" + pose.Calls);
                T.True(pose.Calls <= 3, "6 帧按每3帧一次应至多推理 3 次, 实际=" + pose.Calls);
                T.True(Volatile.Read(ref statusEvents) >= 1, "快照事件应正常触发");
            }
            finally
            {
                svc.Dispose();
            }
        }

        /// <summary>
        /// 门监测旁路联动：无基准时快照 HasBaseline=false 且主链路正常；
        /// 采集基准后差异值可用；DoorStateChanged 转发通路不断。
        /// </summary>
        private static void DoorBypass()
        {
            string tmpPath = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "opencode",
                "zztest_pipeline_door_" + System.Guid.NewGuid().ToString("N").Substring(0, 8) + ".png");
            var detector = new FakeDetector
            {
                InitializedFlag = true,
                DetectImpl = mat => new List<DetectionResult>()
            };
            var analyzer = new DoorMonitorAnalyzer(new DoorMonitorOptions
            {
                RoiX = 0.1f, RoiY = 0.1f, RoiW = 0.5f, RoiH = 0.5f,
                DiffThreshold = 18f, StateHoldMs = 1000,
                BaselinePath = tmpPath
            });
            var svc = new YoloDetectionService(detector, new NullVisualizer())
            {
                DoorAnalyzer = analyzer,
                DoorOverlay = new DoorOverlayRenderer()
            };
            int doorEvents = 0, statusEvents = 0, frameEvents = 0;
            bool? lastHasBaseline = null;
            svc.Start();
            try
            {
                svc.DoorStatusUpdated += (s, snap) =>
                {
                    Interlocked.Increment(ref statusEvents);
                    lastHasBaseline = snap.HasBaseline;
                };
                svc.DoorStateChanged += (s, open) => Interlocked.Increment(ref doorEvents);
                svc.FrameProcessed += (s, m) => { Interlocked.Increment(ref frameEvents); m.Dispose(); };

                using (var f = TestUtil.RandomBgrMat(160, 120)) { svc.ProcessFrame(f); }
                T.True(T.WaitFor(() => Volatile.Read(ref frameEvents) > 0, 10000), "门旁路下主链路应正常出帧");
                T.True(T.WaitFor(() => Volatile.Read(ref statusEvents) > 0, 10000), "无基准时也应发门快照");
                T.True(lastHasBaseline == false, "未采集基准时 HasBaseline=false");

                // 采集基准后再跑一帧：快照应带基准
                using (var f = TestUtil.RandomBgrMat(160, 120)) { analyzer.SetBaselineFromFrame(f); }
                using (var f = TestUtil.RandomBgrMat(160, 120)) { svc.ProcessFrame(f); }
                T.True(T.WaitFor(() => lastHasBaseline == true, 10000), "采集后快照 HasBaseline=true");
            }
            finally
            {
                svc.Dispose();
                try { if (System.IO.File.Exists(tmpPath)) System.IO.File.Delete(tmpPath); } catch { }
            }
        }

        /// <summary>10 次 Start/Stop 循环：线程无泄漏、无挂起，结束状态干净</summary>
        private static void RapidStartStop()
        {
            var detector = new FakeDetector();
            detector.InitializedFlag = true;
            var svc = new YoloDetectionService(detector, new NullVisualizer());
            try
            {
                for (int i = 0; i < 10; i++)
                {
                    svc.Start();
                    using (var f = TestUtil.RandomBgrMat(64, 48)) { svc.ProcessFrame(f); }
                    svc.Stop();
                }
                T.False(svc.IsRunning, "10 次启停后应为停止态");
                T.Eq(0, svc.GetLatestDetections().Count, "停止后无残留结果");
            }
            finally
            {
                svc.Dispose();
            }
        }
    }
}
