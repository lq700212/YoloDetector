// ============================================================
// EsdLiveProbe — 静电触摸检测真机全链路诊断探针（手动运行，不进自动回归）
//
// 用途：摄像头现场排查"手摸静电杆无反应"类问题。依赖真实 RTSP 相机，
//       无法进 harness 自动回归，故沉淀为手动诊断工具。
// 用法：dotnet build 本目录 EsdProbe.csproj（输出直接落主 bin，现场配置/模型
//       相对路径全部生效），然后 bin 目录下运行 EsdProbe.exe [rtsp地址]。
//       默认地址读 cameraConfigs 的 ANGEHUA 模板（rtsp://192.168.1.188:554/ch01.264）。
// 输出：帧流/人数/手腕坐标与置信度/ROI像素矩形/ESD事件/最终快照，
//       并保存 roi_check.png（黄框=ROI、绿红点=左右腕、红框=人体框）供目检。
// 两阶段验证：阶段1 现场 ROI 15s；阶段2 热更新 ROI 到鼠标区域（手腕常驻位置）
//       20s——若阶段2 触发"开始触摸"事件即证明 判定链路健康，问题在 ROI 标定位置。
// 注意：运行后删除 bin 下的 EsdProbe.exe/pdb/roi_check.png，勿提交。
// ============================================================using System;
using System.Collections.Generic;
using System.Threading;
using OpenCvSharp;
using YoloDetection;

internal static class Program
{
    private static int _frames;
    private static readonly List<string> Events = new List<string>();
    private static EsdFrameSnapshot _lastSnapshot;

    private static void Main(string[] args)
    {
        string rtsp = args.Length > 0 ? args[0] : "rtsp://192.168.1.188:554/ch01.264";

        Console.WriteLine("==== ESD 真机全链路诊断 ====");
        var detector = new YoloV26Detector();
        detector.Initialize("Detection/model/yolo26n.onnx");
        var pose = new YoloPoseDetector();
        pose.Initialize("Detection/model/yolo11n-pose.onnx");
        Console.WriteLine("两个模型加载成功");

        // 与 VideoDetectionController.Start 相同的现场参数来源
        var esdOptions = YoloDetector.Configuration.AppConfig.Esd.ToOptions();
        Console.WriteLine("ESD ROI=( " + esdOptions.RoiX.ToString("F4") + ", " + esdOptions.RoiY.ToString("F4")
            + ", " + esdOptions.RoiW.ToString("F4") + ", " + esdOptions.RoiH.ToString("F4") + ") Hold=" + esdOptions.HoldDurationMs);

        var pipeline = new YoloDetectionService(detector, VisualizerFactory.Create(VisualizerType.YoloBuiltin))
        {
            PoseDetector = pose,
            EsdAnalyzer = new EsdContactAnalyzer(esdOptions),
            EsdOverlay = new EsdOverlayRenderer()
        };

        int lastPersons = -1;
        pipeline.DetectionsUpdated += (s, dets) =>
        {
            if (dets.Count != lastPersons)
            {
                lastPersons = dets.Count;
                Console.WriteLine("[DET] 检测人数=" + dets.Count);
            }
        };
        pipeline.FrameProcessed += (s, mat) => Interlocked.Increment(ref _frames);
        pipeline.EsdContactChanged += (s, e) =>
        {
            string msg = (e.InContact ? "⚡开始触摸" : "结束触摸") + " #" + e.TrackId + " @" + DateTime.Now.ToString("HH:mm:ss");
            lock (Events) Events.Add(msg);
            Console.WriteLine("[ESD] " + msg);
        };
        pipeline.EsdStatusUpdated += (s, snap) => _lastSnapshot = snap;

        pipeline.Start();

        // 独立姿态实例做旁路观察（YoloPoseDetector 非线程安全，不能与管道共用）
        var watchPose = new YoloPoseDetector();
        watchPose.Initialize("Detection/model/yolo11n-pose.onnx");
        Mat latestFrame = null;
        List<DetectionResult> latestPersons = new List<DetectionResult>();
        pipeline.FrameProcessed += (s, mat) =>
        {
            var old = latestFrame;
            latestFrame = mat.Clone();
            if (old != null) old.Dispose();
        };
        pipeline.DetectionsUpdated += (s, dets) => latestPersons = dets;

        var roiPx = EsdContactAnalyzer.ComputeRoiPixels(esdOptions, 100000, 100000); // 归一化参考
        var reportTimer = new Timer(o =>
        {
            Mat f = latestFrame;
            if (f == null || latestPersons.Count == 0) return;
            int w = f.Cols, h = f.Rows;
            var roi = EsdContactAnalyzer.ComputeRoiPixels(esdOptions, w, h);
            Console.WriteLine("[观察] 帧=" + w + "x" + h
                + " ROI像素=(" + (int)roi.X + "," + (int)roi.Y + "," + (int)roi.W + "," + (int)roi.H + ")");
            using (var snapshot = f.Clone())
            {
                foreach (var person in latestPersons)
                {
                    var poses = watchPose.Detect(snapshot, new List<DetectionResult> { person });
                    if (poses.Count == 0 || poses[0].Keypoints.Count <= CocoKeyPointIndexes.RightWrist)
                    {
                        Console.WriteLine("  人框(" + (int)person.Left + "," + (int)person.Top + ","
                            + (int)person.Width + "," + (int)person.Height + ") 姿态: 无关键点");
                        continue;
                    }
                    var lw = poses[0].Keypoints[CocoKeyPointIndexes.LeftWrist];
                    var rw = poses[0].Keypoints[CocoKeyPointIndexes.RightWrist];
                    Console.WriteLine("  人框(" + (int)person.Left + "," + (int)person.Top + ","
                        + (int)person.Width + "," + (int)person.Height + ")"
                        + " 左腕=(" + (int)lw.X + "," + (int)lw.Y + ") conf=" + lw.Confidence.ToString("F2")
                        + " 右腕=(" + (int)rw.X + "," + (int)rw.Y + ") conf=" + rw.Confidence.ToString("F2"));
                }
            }
        }, null, 4000, 4000);

        var source = new RtspFrameCapturer();
        source.FrameReady += (s, frame) =>
        {
            pipeline.ProcessFrame(frame);
            frame.Dispose(); // 帧所有权契约：ProcessFrame 内部克隆，调用方随后释放
        };
        if (!source.Start(rtsp))
        {
            Console.WriteLine("!! RTSP 连接失败: " + rtsp);
            return;
        }

        // 等 8 秒出稳定画面后，抓帧画 ROI+手腕存图供人工目检
        Thread.Sleep(8000);
        {
            Mat f = latestFrame;
            if (f != null)
            {
                using (var shot = f.Clone())
                {
                    int w = shot.Cols, h = shot.Rows;
                    var roi = EsdContactAnalyzer.ComputeRoiPixels(esdOptions, w, h);
                    Cv2.Rectangle(shot,
                        new Rect((int)roi.X, (int)roi.Y, (int)roi.W, (int)roi.H),
                        new Scalar(0, 215, 255), 4);
                    if (latestPersons.Count > 0)
                    {
                        var poses = watchPose.Detect(shot, new List<DetectionResult> { latestPersons[0] });
                        if (poses.Count > 0 && poses[0].Keypoints.Count > CocoKeyPointIndexes.RightWrist)
                        {
                            var lw = poses[0].Keypoints[CocoKeyPointIndexes.LeftWrist];
                            var rw = poses[0].Keypoints[CocoKeyPointIndexes.RightWrist];
                            Cv2.Circle(shot, (int)lw.X, (int)lw.Y, 12, new Scalar(80, 255, 80), -1);
                            Cv2.Circle(shot, (int)rw.X, (int)rw.Y, 12, new Scalar(80, 80, 255), -1);
                        }
                        foreach (var p in latestPersons)
                        {
                            Cv2.Rectangle(shot, new Rect((int)p.Left, (int)p.Top, (int)p.Width, (int)p.Height),
                                new Scalar(0, 0, 255), 3);
                        }
                    }
                    Cv2.ImWrite("roi_check.png", shot);
                    Console.WriteLine("[目检图] 已保存 roi_check.png (黄=ROI 绿/红点=左右腕 红=人体框)");
                }
            }
        }
        Console.WriteLine("RTSP 已连接，采流 45 秒（请保持画面里有人，并配合触摸静电杆几次）...");

        for (int i = 0; i < 2; i++)
        {
            Thread.Sleep(5000);
            Console.WriteLine("  t+" + ((i + 1) * 5) + "s 帧=" + Volatile.Read(ref _frames));
        }

        // 阶段1（15s）：现场 ROI（水杯）——手在鼠标上，预期零事件
        Console.WriteLine("[阶段1] 现场 ROI(水杯) 采 15 秒，手应保持在鼠标上...");
        Thread.Sleep(15000);

        // 阶段2：热更新 ROI 到鼠标区域（手腕实测坐标约 x∈[1038,1360] y∈[810,950] / 2304x1296）
        // 走 UI 拖拽标定同一条热更新路径（EsdAnalyzer.Options 就地生效），Hold 1.5s 后应出触摸事件
        float mx = 1150f / 2304f, my = 830f / 1296f, mw = 350f / 2304f, mh = 220f / 1296f;
        Console.WriteLine("[阶段2] 热更新 ROI→鼠标区域 (" + mx.ToString("F3") + "," + my.ToString("F3")
            + "," + mw.ToString("F3") + "," + mh.ToString("F3") + ")，采 20 秒，预期触发触摸事件...");
        pipeline.EsdAnalyzer.Options.ApplyNormalizedRoi(mx, my, mw, mh);
        Thread.Sleep(20000);

        Console.WriteLine("\n==== 诊断汇总 ====");
        Console.WriteLine("总帧数=" + Volatile.Read(ref _frames) + "  最后检测人数=" + lastPersons);
        lock (Events) Console.WriteLine("ESD 触摸事件=" + Events.Count + (Events.Count > 0 ? " | " + string.Join("; ", Events.ToArray()) : ""));
        if (_lastSnapshot != null)
        {
            Console.WriteLine("最后快照: 跟踪人数=" + _lastSnapshot.Persons.Count + " 接触中=" + _lastSnapshot.ContactCount);
            foreach (var p in _lastSnapshot.Persons)
            {
                Console.WriteLine("  Track#" + p.TrackId + " InContact=" + p.InContact
                    + " 左腕落区=" + p.LeftWristInZone + " 右腕落区=" + p.RightWristInZone);
            }
        }
        else
        {
            Console.WriteLine("最后快照: 无（ESD 未产出）");
        }

        source.Dispose();
        reportTimer.Dispose();
        pipeline.Dispose();
        pose.Dispose();
        detector.Dispose();
        watchPose.Dispose();
        Console.WriteLine("==== 完成 ====");
    }
}
