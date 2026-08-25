using System;
using System.Collections.Generic;
using System.Threading;
using OpenCvSharp;
using YoloDetection;

namespace YoloDetector.Tests
{
    // ============================================================
    // YoloPoseDetector 姿态检测器测试：
    //   - 初始化/释放契约（与 YoloV26Detector 同一套防御语义）
    //   - 真实模型(yolo11n-pose.onnx)推理：流程稳定、一一对应、坐标合法
    //   - 官方测试图 bus.jpg 端到端对照：检人 → 姿态 → 手腕坐标落位
    //
    // bus.jpg 是 Ultralytics 官方仓库的街景测试照（多人），作为 git 内
    // 固定基准资源（assets/bus.jpg），保证回归可复现。
    // Python 对照基准（ultralytics API 同图同模型）：4 人、3 人完整 17 点、
    // 手腕置信度最高 0.94+——本分区用例的阈值即据此设定。
    // ============================================================

    internal static class PoseTests
    {
        public static void RunAll()
        {
            T.Case("姿态-Initialize契约(null/缺失/未初始化)", Init_Contract);
            T.Case("姿态-真实模型加载成功", Init_RealModel);
            T.Case("姿态-null与空Mat返回一一对应空姿态", Detect_NullAndEmpty);
            T.Case("姿态-合成图逐框推理一一对应且坐标合法", Detect_Synthetic);
            T.Case("姿态-人数上限MaxPersonsPerFrame", Detect_PersonLimit);
            T.Case("姿态-bus真图端到端(检人→姿态→手腕落位)", Detect_BusRealImage);
            T.Case("姿态-Dispose后调用抛ObjectDisposed", AfterDispose);
        }

        /// <summary>新建一个已初始化的姿态检测器（现场 yolo11n-pose.onnx）</summary>
        internal static YoloPoseDetector CreateInitialized()
        {
            var detector = new YoloPoseDetector();
            detector.Initialize(TestUtil.BinPath("Detection", "model", "yolo11n-pose.onnx"));
            return detector;
        }

        private static void Init_Contract()
        {
            using (var d = new YoloPoseDetector())
            {
                T.Throws<ArgumentNullException>(() => d.Initialize(null), "null 路径应抛 ArgumentNullException");
                T.Throws<ArgumentNullException>(() => d.Initialize(""), "空路径应抛 ArgumentNullException");
                T.Throws<System.IO.FileNotFoundException>(
                    () => d.Initialize(TestUtil.BinPath("Detection", "model", "no_such_pose.onnx")),
                    "模型不存在应抛 FileNotFoundException");
                T.False(d.IsInitialized, "失败后 IsInitialized 应保持 false");

                T.Throws<InvalidOperationException>(
                    () => d.Detect(TestUtil.SyntheticPersonMat(), new List<DetectionResult>()),
                    "未初始化 Detect 应抛 InvalidOperationException");
            }
        }

        private static void Init_RealModel()
        {
            using (var d = CreateInitialized())
            {
                T.True(d.IsInitialized, "现场 yolo11n-pose.onnx 应加载成功");
                // COCO 姿态模型的默认阈值约定（改动会影响业务判定松紧）
                T.Eq(0.30f, d.PersonConfidenceThreshold, "人体框置信度默认值应为 0.30");
                T.Eq(0.35f, d.KeyPointConfidenceThreshold, "关键点置信度默认值应为 0.35");
                T.Eq(8, d.MaxPersonsPerFrame, "单帧推理人数上限默认应为 8");
            }
        }

        private static void Detect_NullAndEmpty()
        {
            using (var d = CreateInitialized())
            using (var frame = TestUtil.SyntheticPersonMat(320, 240))
            {
                var forNull = new List<DetectionResult> { FakeDetector.Box(50, 60, 40, 80) };

                // null 帧：返回与 persons 一一对应的空姿态（契约：永不返回 null）
                var r1 = d.Detect(null, forNull);
                T.True(r1 != null, "null 帧结果不得为 null");
                T.Eq(forNull.Count, r1.Count, "null 帧: 结果数应等于 persons 数");

                // 空 persons：返回空列表
                T.Eq(0, d.Detect(frame, new List<DetectionResult>()).Count, "空 persons → 空结果");
                T.True(d.Detect(frame, null) != null, "null persons → 非 null 空列表");
            }
        }

        private static void Detect_Synthetic()
        {
            using (var d = CreateInitialized())
            using (var frame = TestUtil.SyntheticPersonMat(640, 480))
            {
                var persons = new List<DetectionResult>
                {
                    FakeDetector.Box(320, 240, 120, 260),
                    FakeDetector.Box(100, 200, 90, 220)
                };

                var sw = System.Diagnostics.Stopwatch.StartNew();
                var poses = d.Detect(frame, persons);
                sw.Stop();

                T.True(poses != null, "Detect 结果不得为 null");
                T.Eq(persons.Count, poses.Count, "姿态结果必须与 persons 一一对应");

                for (int i = 0; i < poses.Count; i++)
                {
                    T.Info(string.Format(
                        "person{0}: kpts={1}, 总耗时={2}ms",
                        i, poses[i].Keypoints.Count, sw.ElapsedMilliseconds));

                    foreach (var kpt in poses[i].Keypoints)
                    {
                        // 关键点坐标必须落在画面内（坐标还原公式的硬约束）
                        T.True(kpt.X >= -0.5f && kpt.X <= frame.Cols + 0.5f,
                            "关键点X应在画面内: " + kpt.X);
                        T.True(kpt.Y >= -0.5f && kpt.Y <= frame.Rows + 0.5f,
                            "关键点Y应在画面内: " + kpt.Y);
                        T.True(kpt.Confidence >= 0f && kpt.Confidence <= 1f,
                            "关键点置信度应在[0,1]: " + kpt.Confidence);
                    }

                    // 检出关键点时必须是完整的 17 个 COCO 点
                    if (poses[i].HasKeypoints)
                    {
                        T.Eq(CocoKeyPointIndexes.TotalCount, poses[i].Keypoints.Count,
                            "COCO 模型应输出 17 个关键点");
                    }
                }
            }
        }

        private static void Detect_PersonLimit()
        {
            using (var d = CreateInitialized())
            {
                d.MaxPersonsPerFrame = 3; // 收紧上限便于验证截断逻辑
                using (var frame = TestUtil.SyntheticPersonMat(640, 480))
                {
                    var persons = new List<DetectionResult>();
                    for (int i = 0; i < 10; i++)
                    {
                        persons.Add(FakeDetector.Box(60 + i * 50, 240, 40, 160,
                            conf: 0.9f - i * 0.05f));
                    }

                    var poses = d.Detect(frame, persons);

                    T.Eq(persons.Count, poses.Count, "截断后仍须与 persons 一一对应");

                    int emptyCount = 0;
                    foreach (var p in poses)
                    {
                        if (!p.HasKeypoints) emptyCount++;
                    }
                    T.True(emptyCount >= 7,
                        "超过上限的人(10-3=7+)应返回空关键点, 实际空=" + emptyCount);
                }
            }
        }

        /// <summary>
        /// 端到端对照：官方 bus.jpg（多人街景）→ YoloV26Detector 检人 → 姿态推理。
        /// Python 基准（ultralytics API 同图同模型）检出 4 人、手腕置信度最高 0.94+；
        /// 本用例验证 C# 链路同样拿到有效手腕点且坐标落在人体框附近。
        /// </summary>
        private static void Detect_BusRealImage()
        {
            string imagePath = TestUtil.BinPath("assets", "bus.jpg");
            if (!System.IO.File.Exists(imagePath))
            {
                T.Fail("缺少基准图片 assets/bus.jpg（应随 harness 构建复制到 bin）");
                return;
            }

            using (Mat frame = Cv2.ImRead(imagePath, ImreadModes.Color))
            {
                T.False(frame.Empty(), "bus.jpg 必须能正常解码");

                // 第一步：主检测器检人（TargetClassIds 默认只留 person）
                List<DetectionResult> persons;
                using (var detector = DetectorTests.CreateInitialized())
                {
                    detector.ConfidenceThreshold = 0.35f;
                    persons = detector.Detect(frame);
                }

                T.True(persons.Count >= 2,
                    "bus.jpg 应至少检出 2 个人(Python基准=4), 实际=" + persons.Count);
                T.Info("bus.jpg 检出 " + persons.Count + " 人");

                // 第二步：姿态推理
                using (var poseDetector = CreateInitialized())
                {
                    var poses = poseDetector.Detect(frame, persons);
                    T.Eq(persons.Count, poses.Count, "姿态结果与检出一一对应");

                    int validPoseCount = 0;
                    for (int i = 0; i < poses.Count; i++)
                    {
                        if (!poses[i].HasKeypoints) continue;
                        validPoseCount++;

                        // 手腕点若达到可信度，坐标必须落在人体框外扩 60% 范围内
                        // （关键点不会离本体太远；外扩容忍裁剪扩边后的还原误差）
                        var person = persons[i];
                        float expandX = person.Width * 0.6f;
                        float expandY = person.Height * 0.6f;
                        float minX = person.Left - expandX, maxX = person.Right + expandX;
                        float minY = person.Top - expandY, maxY = person.Bottom + expandY;

                        foreach (int wristIdx in new[] { CocoKeyPointIndexes.LeftWrist, CocoKeyPointIndexes.RightWrist })
                        {
                            var w = poses[i].Keypoints[wristIdx];
                            if (w.Confidence < 0.5f) continue; // 低分手腕不可信，不校验位置

                            T.True(w.X >= minX && w.X <= maxX && w.Y >= minY && w.Y <= maxY,
                                string.Format("person{0} 手腕({1:F0},{2:F0}) 应在人体框附近 [{3:F0},{4:F0}][{5:F0},{6:F0}]",
                                    i, w.X, w.Y, minX, maxX, minY, maxY));
                        }
                    }

                    T.True(validPoseCount >= 1,
                        "bus.jpg 至少 1 人应检出完整关键点(Python基准=3人17点), 实际=" + validPoseCount);
                    T.Info("有效姿态人数: " + validPoseCount);
                }
            }
        }

        private static void AfterDispose()
        {
            var d = CreateInitialized();
            d.Dispose();
            d.Dispose(); // Dispose 幂等

            var persons = new List<DetectionResult> { FakeDetector.Box(50, 60, 30, 40) };
            T.Throws<ObjectDisposedException>(
                () => d.Detect(TestUtil.SyntheticPersonMat(), persons),
                "Dispose 后 Detect 应抛 ObjectDisposedException");
            T.Throws<ObjectDisposedException>(() => d.Initialize("x.onnx"),
                "Dispose 后 Initialize 应抛 ObjectDisposedException");
        }
    }
}
