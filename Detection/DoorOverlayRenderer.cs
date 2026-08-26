using System;
using OpenCvSharp;

namespace YoloDetection
{
    /// <summary>
    /// 门状态监测叠加渲染器（OpenCV 原地绘制实现）。
    ///
    /// 绘制内容：
    ///   - 门区域矩形：绿色 = 关（与基准一致）/ 红色 = 开（差异超阈值）/
    ///     黄色 = 无基准（尚未采集关门基准，无法判定）；
    ///   - 顶部标签 "DOOR CLOSED" / "DOOR OPEN" / "DOOR NO BASELINE"
    ///     （PutText 不支持中文，与 ESD 叠加层同一约定用英文）。
    ///
    /// 性能：纯矢量绘制微秒级；不走 Skia 位图往返。
    /// </summary>
    public class DoorOverlayRenderer : IDoorOverlayRenderer
    {
        // 配色（OpenCV 为 BGR 通道序）
        private static readonly Scalar ClosedColor = new Scalar(80, 255, 80);    // 绿
        private static readonly Scalar OpenColor = new Scalar(80, 80, 255);      // 红
        private static readonly Scalar NoBaselineColor = new Scalar(0, 215, 255); // 黄

        public void Draw(Mat frame, DoorFrameSnapshot snapshot, DoorMonitorOptions options)
        {
            if (frame == null || frame.Empty() || options == null)
            {
                return;
            }

            Rect roi = DoorMonitorAnalyzer.ComputeRoiPixels(options, frame.Cols, frame.Rows);

            Scalar color;
            string label;
            if (snapshot == null || !snapshot.HasBaseline)
            {
                color = NoBaselineColor;
                label = "DOOR NO BASELINE";
            }
            else if (snapshot.IsOpen)
            {
                color = OpenColor;
                label = "DOOR OPEN";
            }
            else
            {
                color = ClosedColor;
                label = "DOOR CLOSED";
            }

            Cv2.Rectangle(frame, roi, color, 2);
            Cv2.PutText(frame, label,
                new OpenCvSharp.Point(roi.X, Math.Max(12, roi.Y - 6)),
                HersheyFonts.HersheySimplex, 0.5, color, 1);
        }
    }
}
