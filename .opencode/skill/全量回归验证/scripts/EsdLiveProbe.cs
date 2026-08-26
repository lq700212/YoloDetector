// 真机观察 v2：双手交叠场景下，除左右腕外同时记录左右肘置信度，
// 验证"肘点交叉验证"修复方案的可行性
using System;
using System.Collections.Generic;
using System.Threading;
using OpenCvSharp;
using YoloDetection;

internal static class Program
{
    private static void Main(string[] args)
    {
        string rtsp = args.Length > 0 ? args[0] : "rtsp://192.168.1.188:554/ch01.264";
        Console.WriteLine("==== 双手交叠观察（腕+肘置信度）====");
        var pose = new YoloPoseDetector();
        pose.Initialize("Detection/model/yolo11n-pose.onnx");
        var esdOptions = YoloDetector.Configuration.AppConfig.Esd.ToOptions();

        var source = new RtspFrameCapturer();
        Mat latestFrame = null;
        var frameLock = new object();
        source.FrameReady += (s, frame) =>
        {
            lock (frameLock)
            {
                var old = latestFrame;
                latestFrame = frame.Clone();
                if (old != null) old.Dispose();
            }
            frame.Dispose();
        };
        if (!source.Start(rtsp)) { Console.WriteLine("!! RTSP 连接失败"); return; }

        Console.WriteLine("90 秒：请交替做 单手摸 / 双手合拢摸（捧住杯子）...");
        var analyzer = new EsdContactAnalyzer(esdOptions);
        analyzer.ContactChanged += (id, inContact, elapsed) =>
            Console.WriteLine("  [ESD事件] " + (inContact ? "⚡开始触摸" : "结束触摸") + " #" + id
                + " @" + DateTime.Now.ToString("HH:mm:ss"));
        var detector = new YoloV26Detector();
        detector.Initialize("Detection/model/yolo26n.onnx");
        var processor = new DefaultResultProcessor();

        var timer = new Timer(o =>
        {
            Mat f;
            lock (frameLock) { f = latestFrame == null ? null : latestFrame.Clone(); }
            if (f == null) return;
            try
            {
                int w = f.Cols, h = f.Rows;
                var roi = EsdContactAnalyzer.ComputeRoiPixels(esdOptions, w, h);
                var persons = processor.Process(detector.Detect(f), w, h);
                if (persons.Count == 0) { Console.WriteLine("[观察] 无人"); return; }
                var poses = pose.Detect(f, new List<DetectionResult> { persons[0] });
                if (poses.Count == 0 || !poses[0].HasKeypoints ||
                    poses[0].Keypoints.Count <= CocoKeyPointIndexes.RightWrist)
                {
                    Console.WriteLine("  无关键点");
                    return;
                }
                analyzer.Update(new List<DetectionResult> { persons[0] }, poses, w, h);
                var kp = poses[0].Keypoints;
                var lw = kp[CocoKeyPointIndexes.LeftWrist];
                var rw = kp[CocoKeyPointIndexes.RightWrist];
                var le = kp[CocoKeyPointIndexes.LeftElbow];
                var re = kp[CocoKeyPointIndexes.RightElbow];
                float wristDist = (float)Math.Sqrt((lw.X - rw.X) * (lw.X - rw.X) + (lw.Y - rw.Y) * (lw.Y - rw.Y));
                bool overlapped = wristDist < 80;
                Console.WriteLine("  腕距=" + (int)wristDist + (overlapped ? "[交叠]" : "")
                    + " 左腕c" + lw.Confidence.ToString("F2") + " 右腕c" + rw.Confidence.ToString("F2")
                    + " | 左肘c" + le.Confidence.ToString("F2") + " 右肘c" + re.Confidence.ToString("F2"));
            }
            finally { f.Dispose(); }
        }, null, 3000, 1500);

        for (int i = 0; i < 24; i++) Thread.Sleep(3750);
        timer.Dispose();
        source.Dispose();
        pose.Dispose();
        detector.Dispose();
        Console.WriteLine("==== 完成 ====");
    }
}
