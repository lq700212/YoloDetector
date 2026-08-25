using System;
using System.Collections.Generic;
using System.Threading;
using OpenCvSharp;
using YoloDetection;

namespace YoloDetector.Tests
{
    // ============================================================
    // 静电杆接触(ESD)分析器测试。
    //
    // 状态机是纯逻辑（几何规则+时间累计），用显式毫秒时钟驱动即可
    // 覆盖全部判定分支——不需要真实视频/模型：
    //   命中累计 → 认定 → 宽限保持 → 超时退出 → 轨迹遗忘 → 快照隔离
    //
    // 另含管道(YoloDetectionService)的 ESD 旁路集成用例：
    //   三件套装配生效、姿态异常不拖垮主检测、未配置时零事件。
    //
    // 时序约定：Update(persons, poses, w, h, nowMs) 的 nowMs 由用例注入，
    // 时间完全可控，无 Thread.Sleep 时序脆弱性。
    // ============================================================

    internal static class EsdAnalyzerTests
    {
        // 统一测试参数：ROI=(0.4,0.2,0.2,0.3)，Hold=1500ms，Grace=2000ms，margin=20px
        private static EsdAnalysisOptions MakeOptions()
        {
            return new EsdAnalysisOptions
            {
                RoiX = 0.4f, RoiY = 0.2f, RoiW = 0.2f, RoiH = 0.3f, // 1000x800 → (400,160,200,240)
                MarginPx = 20f,
                HoldDurationMs = 1500,
                ReleaseGraceMs = 2000,
                WristConfidenceThreshold = 0.35f,
                TrackForgetMs = 3000,
                MaxTrackedPersons = 4
            };
        }

        public static void RunAll()
        {
            T.Case("ESD-归一化ROI换算像素矩形", RoiConversion);
            T.Case("ESD-命中未达Hold时长不认定", Hold_NotReached);
            T.Case("ESD-持续命中达到Hold时长认定并触发事件", Hold_Reached);
            T.Case("ESD-宽限期内丢失保持接触", Grace_KeepsContact);
            T.Case("ESD-超宽限清零退出并触发结束事件", Grace_Expired);
            T.Case("ESD-低置信度手腕不参与判定", LowConfidenceWrist);
            T.Case("ESD-Margin容差内贴边命中", MarginTolerance);
            T.Case("ESD-左右手腕任一命中即命中", EitherWristCounts);
            T.Case("ESD-轨迹遗忘后重新计时换TrackId", TrackForget);
            T.Case("ESD-快照与内部状态隔离", SnapshotIsolation);
            T.Case("ESD-轨迹容量上限淘汰最旧", TrackLimit);
            T.Case("ESD-叠加绘制null参数契约", Overlay_NullContract);
            T.Case("ESD-叠加绘制原地修改帧内容", Overlay_InPlaceDraw);
            T.Case("管道-ESD旁路事件与快照联动", Pipeline_EsdEvents);
            T.Case("管道-姿态异常不影响主检测", Pipeline_PoseExceptionSurvival);
            T.Case("管道-未配置ESD时零附加事件", Pipeline_NoEsdNoEvents);
        }

        /// <summary>造一个人体框（中心点+尺寸）</summary>
        private static DetectionResult Person(float cx, float cy, float w, float h, float conf = 0.9f)
        {
            return FakeDetector.Box(cx, cy, w, h, conf);
        }

        /// <summary>
        /// 造一份姿态结果：只有左右手腕两个点有值（其余关键点高置信度但位置在人体中部，
        /// 避免意外落进 ROI 干扰判定）。
        /// </summary>
        private static PoseResult PoseWithWrists(
            float lx, float ly, float lc, float rx, float ry, float rc)
        {
            var pose = new PoseResult();
            for (int i = 0; i < CocoKeyPointIndexes.TotalCount; i++)
            {
                if (i == CocoKeyPointIndexes.LeftWrist)
                {
                    pose.Keypoints.Add(new PoseKeypoint { X = lx, Y = ly, Confidence = lc });
                }
                else if (i == CocoKeyPointIndexes.RightWrist)
                {
                    pose.Keypoints.Add(new PoseKeypoint { X = rx, Y = ry, Confidence = rc });
                }
                else
                {
                    pose.Keypoints.Add(new PoseKeypoint { X = -1000, Y = -1000, Confidence = 0.9f });
                }
            }
            return pose;
        }

        /// <summary>单人单帧快捷构造</summary>
        private static List<PoseResult> SinglePose(float lx, float ly, float lc, float rx, float ry, float rc)
        {
            return new List<PoseResult> { PoseWithWrists(lx, ly, lc, rx, ry, rc) };
        }

        private static List<DetectionResult> SinglePerson()
        {
            return new List<DetectionResult> { Person(500, 400, 120, 260) };
        }

        // ---------- 用例实现 ----------

        private static void RoiConversion()
        {
            var roi = EsdContactAnalyzer.ComputeRoiPixels(MakeOptions(), 1000, 800);
            // 归一化比例(0.2/0.3/0.4)是二进制无限小数，乘积带浮点尾差，
            // 必须用容差比较——精确相等会出现"显示相同数值却断言失败"
            T.True(Math.Abs(roi.X - 400f) < 0.01f, "RoiX=0.4 × 1000 → X≈400");
            T.True(Math.Abs(roi.Y - 160f) < 0.01f, "RoiY=0.2 × 800 → Y≈160");
            T.True(Math.Abs(roi.W - 200f) < 0.01f, "RoiW=0.2 × 1000 → W≈200");
            T.True(Math.Abs(roi.H - 240f) < 0.01f, "RoiH=0.3 × 800 → H≈240");

            // Contains 语义：矩形内部命中、外扩 margin 内命中、再远不命中
            T.True(roi.Contains(500, 300, 20), "ROI 中心应命中");
            T.True(roi.Contains(615, 300, 20), "右边界外15px(≤margin20) 应命中");
            T.False(roi.Contains(630, 300, 20), "右边界外30px 不应命中");

            // 出界配置被夹紧（ToOptions 已夹紧，这里验证分析器侧同样防御）
            var bad = new EsdAnalysisOptions { RoiX = 1.5f, RoiY = -0.5f };
            var r2 = EsdContactAnalyzer.ComputeRoiPixels(bad, 100, 100);
            T.True(Math.Abs(r2.X - 100f) < 0.01f, "RoiX>1 应回退到画面右缘(夹紧到1)");
            T.Eq(0f, r2.Y, "RoiY<0 应夹紧到 0");
        }

        private static void Hold_NotReached()
        {
            var analyzer = new EsdContactAnalyzer(MakeOptions());
            bool anyEvent = false;
            analyzer.ContactChanged += (id, c, e) => anyEvent = true;

            var persons = SinglePerson();
            // 手腕落在 ROI 中心 (500,280)
            var poses = SinglePose(500, 280, 0.9f, 500, 280, 0.9f);

            analyzer.Update(persons, poses, 1000, 800, nowMs: 0);      // 首帧建立轨迹(dt=0)
            var s1 = analyzer.Update(persons, poses, 1000, 800, nowMs: 1400);

            T.False(s1.Persons[0].InContact, "命中1400ms(<1500) 不应认定接触");
            T.True(s1.ContactCount == 0, "接触计数应为0");
            T.False(anyEvent, "未达Hold不应触发翻转事件");
            T.True(s1.Persons[0].LeftWristInZone && s1.Persons[0].RightWristInZone,
                "两腕瞬时落区标记应为真");
        }

        private static void Hold_Reached()
        {
            var analyzer = new EsdContactAnalyzer(MakeOptions());
            int? eventTrackId = null;
            bool? eventState = null;
            double eventElapsed = -1;
            analyzer.ContactChanged += (id, c, e) => { eventTrackId = id; eventState = c; eventElapsed = e; };

            var persons = SinglePerson();
            var poses = SinglePose(500, 280, 0.9f, 500, 280, 0.9f);

            analyzer.Update(persons, poses, 1000, 800, 0);
            analyzer.Update(persons, poses, 1000, 800, 700);
            var s = analyzer.Update(persons, poses, 1000, 800, 1600); // 累计≈1600 ≥ 1500

            T.True(s.Persons[0].InContact, "命中1600ms(≥1500) 应认定接触");
            T.Eq(1, s.ContactCount, "接触计数应为1");
            T.True(eventState == true, "应触发进入接触事件");
            T.True(eventElapsed >= 1500, "事件携带的累计时长应≥Hold阈值: " + eventElapsed);
            T.True(eventTrackId.HasValue, "事件应携带轨迹编号");
        }

        private static void Grace_KeepsContact()
        {
            var analyzer = new EsdContactAnalyzer(MakeOptions());

            var persons = SinglePerson();
            var hit = SinglePose(500, 280, 0.9f, 500, 280, 0.9f);
            var miss = SinglePose(50, 50, 0.9f, 50, 50, 0.9f); // 手腕远离 ROI

            analyzer.Update(persons, hit, 1000, 800, 0);
            analyzer.Update(persons, hit, 1000, 800, 1600);
            T.True(analyzer.Update(persons, miss, 1000, 800, 2600).Persons[0].InContact,
                "丢失1000ms(<2000宽限) 应保持接触");
            var s = analyzer.Update(persons, miss, 1000, 800, 3400).Persons[0];

            T.True(s.InContact, "丢失1800ms 仍在宽限期内应保持接触");
            // 冻结语义：宽限期不增长也不清零（保持最后命中时的累计值附近）
            T.True(s.ContactElapsedMs >= 1500, "宽限期累计时长不得清零");
            T.True(s.ContactElapsedMs <= 1700,
                "宽限期累计时长不得继续大幅增长(冻结语义): " + s.ContactElapsedMs);

            // 宽限期内重新命中 → 接触继续保持且从冻结值继续累计
            var s2 = analyzer.Update(persons, hit, 1000, 800, 3800).Persons[0];
            T.True(s2.InContact, "宽限期内恢复命中应保持/恢复接触态");
        }

        private static void Grace_Expired()
        {
            var analyzer = new EsdContactAnalyzer(MakeOptions());
            bool endEventFired = false;
            double endElapsed = -1;
            analyzer.ContactChanged += (id, inContact, elapsed) =>
            {
                if (!inContact) { endEventFired = true; endElapsed = elapsed; }
            };

            var persons = SinglePerson();
            var hit = SinglePose(500, 280, 0.9f, 500, 280, 0.9f);
            var miss = SinglePose(50, 50, 0.9f, 50, 50, 0.9f);

            analyzer.Update(persons, hit, 1000, 800, 0);
            analyzer.Update(persons, hit, 1000, 800, 1600);   // 进入接触
            analyzer.Update(persons, miss, 1000, 800, 2600);  // 宽限中
            var s = analyzer.Update(persons, miss, 1000, 800, 4200).Persons[0]; // 距最后命中2600ms > 2000

            T.False(s.InContact, "超宽限(2600ms>2000ms) 应退出接触");
            T.Eq(0.0, s.ContactElapsedMs, "退出后累计时长应清零");
            T.True(endEventFired, "应触发结束触摸事件");
            T.True(endElapsed >= 1500, "结束事件的时长应为清零前的最终值");
        }

        private static void LowConfidenceWrist()
        {
            var analyzer = new EsdContactAnalyzer(MakeOptions());
            var persons = SinglePerson();
            // 坐标在 ROI 正中但置信度 0.2 < 0.35 → 不可信
            var lowConf = SinglePose(500, 280, 0.2f, 500, 280, 0.1f);

            var s = analyzer.Update(persons, lowConf, 1000, 800, 0).Persons[0];
            T.False(s.LeftWristInZone && s.RightWristInZone, "低置信度手腕不得判落区");
            T.False(s.InContact, "全程不可信手腕不得进入接触");
        }

        private static void MarginTolerance()
        {
            var analyzer = new EsdContactAnalyzer(MakeOptions());
            var persons = SinglePerson();

            // ROI 右缘 x=600；点 x=613 在 margin=20 容差内
            var nearEdge = SinglePose(613, 300, 0.9f, 613, 300, 0.9f);
            T.True(analyzer.Update(persons, nearEdge, 1000, 800, 0).Persons[0].LeftWristInZone,
                "ROI外13px(≤margin20) 应判命中");

            // 点 x=635 超出容差
            var farEdge = SinglePose(635, 300, 0.9f, 635, 300, 0.9f);
            T.False(analyzer.Update(persons, farEdge, 1000, 800, 100).Persons[0].LeftWristInZone,
                "ROI外35px(>margin20) 不应判命中");
        }

        private static void EitherWristCounts()
        {
            var analyzer = new EsdContactAnalyzer(MakeOptions());
            var persons = SinglePerson();
            // 左手在 ROI 外、右手在 ROI 内
            var onlyRight = SinglePose(50, 50, 0.9f, 500, 280, 0.9f);

            var s = analyzer.Update(persons, onlyRight, 1000, 800, 0).Persons[0];
            T.False(s.LeftWristInZone, "左手在 ROI 外");
            T.True(s.RightWristInZone, "右手在 ROI 内");

            // 持续只用右手命中也应能进入接触态（任一手即可）
            analyzer.Update(persons, onlyRight, 1000, 800, 800);
            T.True(analyzer.Update(persons, onlyRight, 1000, 800, 1600).Persons[0].InContact,
                "仅右手持续命中也应认定接触");
        }

        private static void TrackForget()
        {
            var analyzer = new EsdContactAnalyzer(MakeOptions());
            var persons = SinglePerson();
            var hit = SinglePose(500, 280, 0.9f, 500, 280, 0.9f);

            var s1 = analyzer.Update(persons, hit, 1000, 800, 0).Persons[0];
            int firstId = s1.TrackId;

            // 人消失超过 TrackForgetMs(3000) → 轨迹删除
            analyzer.Update(new List<DetectionResult>(), new List<PoseResult>(), 1000, 800, 2000);
            var s2 = analyzer.Update(persons, hit, 1000, 800, 6000).Persons[0];

            T.True(s2.TrackId != firstId, "遗忘后再出现应分配新 TrackId(重新计时语义)");
            T.False(s2.InContact, "新轨迹不得继承旧的接触状态");
        }

        private static void SnapshotIsolation()
        {
            var analyzer = new EsdContactAnalyzer(MakeOptions());
            var persons = SinglePerson();
            var hit = SinglePose(500, 280, 0.9f, 500, 280, 0.9f);

            var snap1 = analyzer.Update(persons, hit, 1000, 800, 0);

            // 恶意/误改快照：清空列表、篡改字段
            snap1.Persons.Clear();
            snap1.Persons.Add(new EsdPersonStatus { InContact = true });
            snap1.ContactCount = 99;

            var snap2 = analyzer.Update(persons, hit, 1000, 800, 500);
            T.Eq(1, snap2.Persons.Count, "修改上一帧快照不得影响分析器内部状态");
            T.False(snap2.Persons[0].InContact, "内部状态不受外部篡改影响");
            T.Eq(0, snap2.ContactCount, "ContactCount 反映的是内部真实状态");
        }

        private static void TrackLimit()
        {
            var options = MakeOptions(); // MaxTrackedPersons=4
            var analyzer = new EsdContactAnalyzer(options);

            // 同一帧塞 8 个相距很远的人（彼此不会被近邻匹配合并）
            var persons = new List<DetectionResult>();
            for (int i = 0; i < 8; i++)
            {
                persons.Add(Person(60 + i * 120, 400, 40, 100));
            }
            var emptyPoses = new List<PoseResult>();
            foreach (var p in persons) emptyPoses.Add(new PoseResult { Person = p });

            var snap = analyzer.Update(persons, emptyPoses, 1000, 800, 0);
            T.True(snap.Persons.Count <= options.MaxTrackedPersons,
                "轨迹数不得超过容量上限: " + snap.Persons.Count);
        }

        // ==================== 叠加渲染器契约 ====================
        // EsdOverlayRenderer.Draw 的所有权契约：原地修改传入 frame 并返回同一实例
        // （接口注释明确"调用方不需要对返回值做额外释放"），管道据此不做额外 Dispose。

        private static void Overlay_NullContract()
        {
            var renderer = new EsdOverlayRenderer();
            var options = MakeOptions();

            using (var frame = TestUtil.RandomBgrMat(320, 240))
            {
                // 各 null 组合都不得抛异常（管道在 ESD 未出快照时也会传 null snapshot）
                renderer.Draw(null, new EsdFrameSnapshot(), options);
                renderer.Draw(frame, null, options);
                renderer.Draw(frame, new EsdFrameSnapshot(), null);
            }
        }

        private static void Overlay_InPlaceDraw()
        {
            var renderer = new EsdOverlayRenderer();
            var options = MakeOptions();

            // 快照：一个接触中的人（中心 160,120 尺寸 80x160，在 320x240 帧内）
            var snapshot = new EsdFrameSnapshot();
            snapshot.Persons.Add(new EsdPersonStatus
            {
                TrackId = 1,
                X = 160, Y = 120, Width = 80, Height = 160,
                LeftWristInZone = true, RightWristInZone = false,
                InContact = true,
                ContactElapsedMs = 2100
            });

            using (var frame = new Mat(240, 320, MatType.CV_8UC3, new Scalar(30, 30, 30)))
            using (var before = frame.Clone())
            {
                renderer.Draw(frame, snapshot, options);

                // 原地语义：帧内容必须被实际修改（画上了 ROI 框/人体框/文字/徽标）
                long diff = TestUtil.DiffBytes(before, frame);
                T.True(diff > 0, "Draw 后帧应有可见变化(原地绘制), diffBytes=" + diff);
                T.Eq(320, frame.Cols, "Draw 不得替换/缩放帧对象");
            }

            // 空快照也要画 ROI 标定框（现场靠它定位，快照缺失时不能消失）
            using (var frame = new Mat(240, 320, MatType.CV_8UC3, new Scalar(30, 30, 30)))
            using (var before = frame.Clone())
            {
                renderer.Draw(frame, null, options);
                T.True(TestUtil.DiffBytes(before, frame) > 0,
                    "snapshot=null 时仍应绘制 ROI 标定框");
            }
        }

        // ==================== 管道 ESD 旁路集成 ====================

        /// <summary>可编程假姿态检测器（注入固定关键点或异常）</summary>
        internal class FakePoseDetector : IPoseDetector
        {
            public bool InitializedFlag = true;

            public Func<Mat, List<DetectionResult>, List<PoseResult>> DetectImpl =
                (mat, persons) => new List<PoseResult>();

            public Exception ThrowOnDetect;
            public int Calls;
            public int DisposeCalls;

            public bool IsInitialized => InitializedFlag;

            public float PersonConfidenceThreshold { get; set; }
            public float KeyPointConfidenceThreshold { get; set; }

            public void Initialize(string modelPath) { InitializedFlag = true; }

            public List<PoseResult> Detect(Mat mat, List<DetectionResult> persons)
            {
                Calls++;
                if (ThrowOnDetect != null) throw ThrowOnDetect;
                return DetectImpl(mat, persons);
            }

            public void Dispose() { DisposeCalls++; }
        }

        private static void Pipeline_EsdEvents()
        {
            var detector = new FakeDetector
            {
                InitializedFlag = true,
                // 人体框必须落在帧内：160x120 帧上出界的框会被 DefaultResultProcessor
                // 过滤掉，persons 变空后快照就没有人、断言会 index 越界
                DetectImpl = mat => new List<DetectionResult> { FakeDetector.Box(80, 60, 40, 80) }
            };
            var svc = new YoloDetectionService(detector, new NullVisualizer())
            {
                PoseDetector = new FakePoseDetector
                {
                    // 手腕恒定落在 ROI(默认0.4~0.6,0.25~0.6 of 160x120 → (64..96,30..72))
                    DetectImpl = (mat, persons) => new List<PoseResult>
                    {
                        PoseWithWrists(80, 50, 0.95f, 80, 50, 0.95f)
                    }
                },
                EsdAnalyzer = new EsdContactAnalyzer(new EsdAnalysisOptions
                {
                    RoiX = 0.4f, RoiY = 0.2f, RoiW = 0.3f, RoiH = 0.5f,
                    HoldDurationMs = 0, // Hold=0：首帧立即认定，免去多帧推进
                    ReleaseGraceMs = 2000
                }),
                EsdOverlay = new EsdOverlayRenderer()
            };

            int statusEvents = 0, contactEvents = 0, frameEvents = 0;
            EsdFrameSnapshot lastSnapshot = null;
            svc.Start();
            try
            {
                svc.EsdStatusUpdated += (s, snap) => { Interlocked.Increment(ref statusEvents); lastSnapshot = snap; };
                svc.EsdContactChanged += (s, e) => { Interlocked.Increment(ref contactEvents); };
                svc.FrameProcessed += (s, mat) => { Interlocked.Increment(ref frameEvents); mat.Dispose(); };

                using (var frame = TestUtil.RandomBgrMat(160, 120))
                {
                    svc.ProcessFrame(frame);
                }

                T.True(T.WaitFor(() => Volatile.Read(ref frameEvents) > 0, 10000),
                    "帧事件应正常到达");
                T.True(T.WaitFor(() => Volatile.Read(ref statusEvents) > 0, 5000),
                    "启用ESD时应收到状态快照事件");
                T.True(T.WaitFor(() => Volatile.Read(ref contactEvents) > 0, 5000),
                    "Hold=0 且手腕落区时应触发接触开始事件");
                T.True(lastSnapshot != null && lastSnapshot.Persons.Count == 1,
                    "快照应包含被跟踪的人");
                T.True(lastSnapshot != null && lastSnapshot.Persons.Count == 1 && lastSnapshot.Persons[0].InContact,
                    "快照中该人应为接触态");
            }
            finally
            {
                svc.Dispose();
            }
        }

        private static void Pipeline_PoseExceptionSurvival()
        {
            var detector = new FakeDetector
            {
                InitializedFlag = true,
                DetectImpl = mat => new List<DetectionResult> { FakeDetector.Box(50, 60, 30, 40) }
            };
            var svc = new YoloDetectionService(detector, new NullVisualizer())
            {
                PoseDetector = new FakePoseDetector { ThrowOnDetect = new InvalidOperationException("pose boom") },
                EsdAnalyzer = new EsdContactAnalyzer(new EsdAnalysisOptions())
            };

            int resultEvents = 0, frameEvents = 0;
            svc.Start();
            try
            {
                svc.DetectionsUpdated += (s, list) => Interlocked.Increment(ref resultEvents);
                svc.FrameProcessed += (s, mat) => { Interlocked.Increment(ref frameEvents); mat.Dispose(); };

                // 两帧必须分批提交：单槽位缓冲会把紧挨着提交的第二帧覆盖掉第一帧
                // （这是管道的正常防积压语义），先确认第一帧处理完再喂第二帧
                using (var frame = TestUtil.RandomBgrMat(160, 120))
                {
                    svc.ProcessFrame(frame);
                }

                T.True(T.WaitFor(() => Volatile.Read(ref frameEvents) >= 1, 10000),
                    "第1帧应正常出帧");
                T.True(Volatile.Read(ref resultEvents) >= 1, "第1帧人员检测结果应照常发布");

                using (var frame = TestUtil.RandomBgrMat(160, 120))
                {
                    svc.ProcessFrame(frame); // 姿态持续抛异常，验证主链路长期存活
                }

                T.True(T.WaitFor(() => Volatile.Read(ref frameEvents) >= 2, 10000),
                    "姿态抛异常时主检测链路必须继续出帧");
                T.True(Volatile.Read(ref resultEvents) >= 2,
                    "人员检测结果必须照常发布");
                T.True(svc.IsRunning, "管道必须保持运行");
            }
            finally
            {
                svc.Dispose();
            }
        }

        private static void Pipeline_NoEsdNoEvents()
        {
            var detector = new FakeDetector
            {
                InitializedFlag = true,
                DetectImpl = mat => new List<DetectionResult> { FakeDetector.Box(50, 60, 30, 40) }
            };
            var svc = new YoloDetectionService(detector, new NullVisualizer()); // 未配任何 ESD 组件

            int statusEvents = 0, contactEvents = 0, frameEvents = 0;
            svc.Start();
            try
            {
                svc.EsdStatusUpdated += (s, snap) => Interlocked.Increment(ref statusEvents);
                svc.EsdContactChanged += (s, e) => Interlocked.Increment(ref contactEvents);
                svc.FrameProcessed += (s, mat) => { Interlocked.Increment(ref frameEvents); mat.Dispose(); };

                using (var frame = TestUtil.RandomBgrMat(160, 120))
                {
                    svc.ProcessFrame(frame);
                }

                T.True(T.WaitFor(() => Volatile.Read(ref frameEvents) > 0, 10000),
                    "纯检测模式帧事件应正常");
                Thread.Sleep(100);
                T.Eq(0, Volatile.Read(ref statusEvents), "未配置ESD不得发状态事件");
                T.Eq(0, Volatile.Read(ref contactEvents), "未配置ESD不得发接触事件");
            }
            finally
            {
                svc.Dispose();
            }
        }
    }
}
