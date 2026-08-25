using System;
using System.Collections.Generic;
using OpenCvSharp;
using SkiaSharp;

namespace YoloDetection
{
    /// <summary>
    /// 模块内置可视化器（红色检测框 + 红底白字标签，Windows/Linux/macOS 全平台一致）。
    /// 内部路径：Mat → SKBitmap → Skia 绘制 → SKBitmap 转 Mat。
    ///
    /// 历史说明：早期实现基于 System.Drawing(GDI+)，仅 Windows 可用；
    /// 现已迁移到 SkiaSharp（Google Skia 跨平台封装），三大平台渲染效果与 API 完全一致。
    ///
    /// 性能说明：Skia 是 native 渲染引擎（Chrome/Android 同源），矩形+文字绘制开销
    /// 微秒级，相对推理耗时可忽略；像素搬运走 Buffer.MemoryCopy 整块拷贝。
    /// </summary>
    public class YoloBuiltinVisualizer : IDetectionVisualizer
    {
        public Mat Draw(Mat frame, List<DetectionResult> results)
        {
            if (frame == null || frame.Empty())
            {
                return null;
            }

            using (SKBitmap bitmap = MatExtensions.MatToSKBitmap(frame))
            {
                if (bitmap == null)
                {
                    return null;
                }

                if (results != null && results.Count > 0)
                {
                    using (var canvas = new SKCanvas(bitmap))
                    using (var boxPaint = new SKPaint())
                    using (var bgPaint = new SKPaint())
                    using (var textPaint = new SKPaint())
                    using (var typeface = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold))
                    {
                        // 抗锯齿 + 红色 2px 描边框（与原 GDI+ 版视觉一致）
                        boxPaint.IsAntialias = true;
                        boxPaint.Style = SKPaintStyle.Stroke;
                        boxPaint.StrokeWidth = 2;
                        boxPaint.Color = SKColors.Red;

                        textPaint.IsAntialias = true;
                        textPaint.Typeface = typeface;
                        textPaint.TextSize = 14;
                        textPaint.Color = SKColors.White;

                        bgPaint.IsAntialias = true;
                        bgPaint.Style = SKPaintStyle.Fill;
                        bgPaint.Color = SKColors.Red;

                        foreach (var det in results)
                        {
                            var rect = new SKRect(
                                (float)Math.Max(0, det.Left),
                                (float)Math.Max(0, det.Top),
                                (float)Math.Max(0, det.Left) + (float)Math.Max(1, det.Width),
                                (float)Math.Max(0, det.Top) + (float)Math.Max(1, det.Height));

                            canvas.DrawRect(rect, boxPaint);

                            string label = $"{det.ClassName} {det.Confidence:F2}";
                            float textWidth = textPaint.MeasureText(label);
                            float labelY = (float)Math.Max(0, rect.Top - textPaint.TextSize - 2);

                            // 标签底色（红）+ 白字，贴框上方；贴顶时压入框内
                            canvas.DrawRect(rect.Left, labelY, textWidth + 4, textPaint.TextSize + 2, bgPaint);
                            canvas.DrawText(label, rect.Left + 2, labelY + textPaint.TextSize, textPaint);
                        }
                    }
                }

                return MatExtensions.SKBitmapToMat(bitmap);
            }
        }
    }
}
