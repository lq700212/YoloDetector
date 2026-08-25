using System;
using OpenCvSharp;

namespace YoloDetection
{
    /// <summary>
    /// 静电接触叠加渲染器（OpenCV 原地绘制实现）。
    ///
    /// 绘制内容（自上而下）：
    ///   - 静电杆 ROI：黄色矩形 + 顶部 "ESD POLE" 标签——现场据此目检 ROI 标定位置对不对；
    ///   - 每个被跟踪的人：
    ///       接触中 → 绿色粗框 + "ESD OK"；未接触 → 灰色细框 + "NO GND"；
    ///       手腕落点画圆：在区域内绿色实心 / 区域外红色空心，一眼看出是哪只手差多少；
    ///   - 左下角统计行 "ESD: n/m in contact"。
    ///
    /// 性能说明：全部为矢量绘制（矩形/圆/文字），微秒级，相对推理耗时可忽略；
    /// 不走 Skia 位图往返（Mat→SKBitmap→Mat 约 10ms），预览帧零额外拷贝。
    /// </summary>
    public class EsdOverlayRenderer : IEsdOverlayRenderer
    {
        // 配色（OpenCV 为 BGR 通道序）
        private static readonly Scalar RoiColor = new Scalar(0, 215, 255);     // 黄
        private static readonly Scalar ContactColor = new Scalar(80, 255, 80); // 绿
        private static readonly Scalar NoContactColor = new Scalar(160, 160, 160); // 灰
        private static readonly Scalar WristInColor = new Scalar(80, 255, 80);     // 绿（腕已落区）
        private static readonly Scalar WristOutColor = new Scalar(80, 80, 255);    // 红（腕未落区）

        public void Draw(Mat frame, EsdFrameSnapshot snapshot, EsdAnalysisOptions options)
        {
            if (frame == null || frame.Empty() || options == null)
            {
                return;
            }

            // 快照缺失（如本帧 ESD 被节流跳过）时仍要画 ROI，保证标定参考始终可见
            var roi = EsdContactAnalyzer.ComputeRoiPixels(options, frame.Cols, frame.Rows);
            var roiRect = new Rect((int)roi.X, (int)roi.Y, Math.Max(1, (int)roi.W), Math.Max(1, (int)roi.H));

            Cv2.Rectangle(frame, roiRect, RoiColor, 2);
            Cv2.PutText(frame, "ESD POLE",
                new OpenCvSharp.Point(roiRect.X, Math.Max(12, roiRect.Y - 6)),
                HersheyFonts.HersheySimplex, 0.5, RoiColor, 1);

            if (snapshot == null || snapshot.Persons == null)
            {
                return;
            }

            foreach (var status in snapshot.Persons)
            {
                bool inContact = status.InContact;
                var boxColor = inContact ? ContactColor : NoContactColor;
                int thickness = inContact ? 3 : 1;

                var rect = new Rect(
                    (int)Math.Max(0, status.X - status.Width / 2),
                    (int)Math.Max(0, status.Y - status.Height / 2),
                    (int)Math.Max(1, status.Width),
                    (int)Math.Max(1, status.Height));

                Cv2.Rectangle(frame, rect, boxColor, thickness);

                string label = inContact
                    ? $"#{status.TrackId} ESD OK {status.ContactElapsedMs / 1000.0:F1}s"
                    : $"#{status.TrackId} NO GND";
                Cv2.PutText(frame, label,
                    new OpenCvSharp.Point(rect.X, Math.Max(12, rect.Y - 6)),
                    HersheyFonts.HersheySimplex, 0.55, boxColor, 2);

                // 手腕落点：快照里没有存坐标只有"是否落区"，这里以人体框顶部两角近似标注
                // （精确手腕位置由姿态检测内部使用；预览层只关心结论）
                DrawWristBadge(frame, rect, leftSide: true, status.LeftWristInZone);
                DrawWristBadge(frame, rect, leftSide: false, status.RightWristInZone);
            }

            string summary = $"ESD: {snapshot.ContactCount}/{snapshot.Persons.Count} in contact";
            Cv2.PutText(frame, summary,
                new OpenCvSharp.Point(10, frame.Rows - 12),
                HersheyFonts.HersheySimplex, 0.6,
                snapshot.ContactCount > 0 ? ContactColor : NoContactColor, 2);
        }

        /// <summary>在人体框左/右上角画手腕状态徽标（绿实心=落区，红空心=未落区）。</summary>
        private static void DrawWristBadge(Mat frame, Rect personRect, bool leftSide, bool inZone)
        {
            var center = new OpenCvSharp.Point(
                leftSide ? personRect.X + 8 : personRect.Right - 8,
                personRect.Y + 8);

            if (inZone)
            {
                Cv2.Circle(frame, center, 5, WristInColor, -1); // 实心
            }
            else
            {
                Cv2.Circle(frame, center, 5, WristOutColor, 2); // 空心
            }
        }
    }
}
