using System;
using System.Collections.Generic;
using System.Drawing;
using OpenCvSharp;

namespace YoloDetector.Detection
{
    /// <summary>
    /// 可视化器工厂：根据类型枚举创建对应的绘制器实例。
    /// </summary>
    public static class VisualizerFactory
    {
        public static IDetectionVisualizer Create(VisualizerType type)
        {
            switch (type)
            {
                case VisualizerType.YoloBuiltin:
                    return new YoloBuiltinVisualizer();
                case VisualizerType.OpenCV:
                    return new OpenCVVisualizer();
                default:
                    return new OpenCVVisualizer();
            }
        }
    }

    /// <summary>
    /// GDI+ 可视化器（红色检测框）。
    /// 内部路径：Mat → Bitmap → GDI+ 绘制 → Bitmap 转 Mat。
    /// 依赖 WinForms/GDI+，适合需要精细文字渲染的场景。
    /// </summary>
    public class YoloBuiltinVisualizer : IDetectionVisualizer
    {
        public Mat Draw(Mat frame, List<DetectionResult> results)
        {
            if (frame == null || frame.Empty())
            {
                return null;
            }

            Bitmap bitmap = MatExtensions.MatToBitmap(frame);
            if (bitmap == null)
            {
                return null;
            }

            if (results != null && results.Count > 0)
            {
                using (var g = Graphics.FromImage(bitmap))
                {
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

                    foreach (var det in results)
                    {
                        var rect = new Rectangle(
                            (int)Math.Max(0, det.Left),
                            (int)Math.Max(0, det.Top),
                            (int)Math.Max(1, det.Width),
                            (int)Math.Max(1, det.Height));

                        using (var pen = new Pen(Color.Red, 2))
                        {
                            g.DrawRectangle(pen, rect);
                        }

                        string label = $"{det.ClassName} {det.Confidence:F2}";
                        using (var font = new Font("Arial", 10, FontStyle.Bold))
                        using (var brush = new SolidBrush(Color.Red))
                        {
                            var labelSize = g.MeasureString(label, font);
                            float labelY = (float)Math.Max(0, rect.Y - labelSize.Height - 2);
                            var labelRect = new RectangleF(rect.X, labelY, labelSize.Width, labelSize.Height);

                            g.FillRectangle(brush, labelRect);
                            g.DrawString(label, font, Brushes.White, rect.X, labelY);
                        }
                    }
                }
            }

            Mat result = MatExtensions.BitmapToMat(bitmap);
            bitmap.Dispose();
            return result;
        }
    }

    /// <summary>
    /// OpenCV 可视化器（绿色检测框）。
    /// 直接在 Mat 上绘制，无编解码开销，性能优于 GDI+ 方案。
    /// </summary>
    public class OpenCVVisualizer : IDetectionVisualizer
    {
        public Mat Draw(Mat frame, List<DetectionResult> results)
        {
            if (frame == null || frame.Empty())
            {
                return null;
            }

            // 克隆帧绘制，不污染原始帧；返回后归调用方所有
            Mat drawFrame = frame.Clone();

            if (results != null)
            {
                var green = new Scalar(0, 255, 0);
                foreach (var det in results)
                {
                    var rect = new Rect(
                        (int)Math.Max(0, det.Left),
                        (int)Math.Max(0, det.Top),
                        (int)Math.Max(1, det.Width),
                        (int)Math.Max(1, det.Height));

                    Cv2.Rectangle(drawFrame, rect, green, 2);

                    string label = $"{det.ClassName} {det.Confidence:F2}";
                    Cv2.PutText(drawFrame, label,
                        new OpenCvSharp.Point((int)det.Left, (int)Math.Max(10, det.Top - 5)),
                        HersheyFonts.HersheySimplex, 0.6, green, 2);
                }
            }

            return drawFrame;
        }
    }
}
