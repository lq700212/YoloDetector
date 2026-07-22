using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace YoloDetector.YoloDetection
{
    /// <summary>
    /// 透明覆盖层控件，用于在native VideoView上方绘制YOLO检测框
    /// 使用WS_EX_TRANSPARENT样式 + 跳过背景绘制，实现真正的透明效果
    /// </summary>
    public class TransparentOverlay : Control
    {
        private List<DetectionResult> _detections = new List<DetectionResult>();
        private int _frameWidth = 1920;
        private int _frameHeight = 1080;

        public TransparentOverlay()
        {
            SetStyle(ControlStyles.SupportsTransparentBackColor, true);
            SetStyle(ControlStyles.Opaque, false);
            SetStyle(ControlStyles.UserPaint, true);
            SetStyle(ControlStyles.AllPaintingInWmPaint, true);
            SetStyle(ControlStyles.OptimizedDoubleBuffer, false);
            SetStyle(ControlStyles.ResizeRedraw, true);
            this.BackColor = Color.Transparent;
            this.DoubleBuffered = false;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x00000020; // WS_EX_TRANSPARENT
                return cp;
            }
        }

        protected override void OnPaintBackground(PaintEventArgs pevent)
        {
            // 不绘制背景，让下方控件可见
        }

        public void UpdateDetections(List<DetectionResult> detections, int frameWidth, int frameHeight)
        {
            _detections = detections ?? new List<DetectionResult>();
            _frameWidth = frameWidth > 0 ? frameWidth : 1920;
            _frameHeight = frameHeight > 0 ? frameHeight : 1080;

            if (this.IsHandleCreated && !this.IsDisposed)
            {
                this.Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            if (_detections == null || _detections.Count == 0)
                return;

            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            float scaleX = this.Width / (float)_frameWidth;
            float scaleY = this.Height / (float)_frameHeight;

            using (var pen = new Pen(Color.Red, 2))
            using (var font = new Font("Arial", 10, FontStyle.Bold))
            using (var bgBrush = new SolidBrush(Color.FromArgb(180, Color.Red)))
            {
                foreach (var detection in _detections)
                {
                    int x = (int)(detection.Left * scaleX);
                    int y = (int)(detection.Top * scaleY);
                    int w = (int)Math.Max(1, detection.Width * scaleX);
                    int h = (int)Math.Max(1, detection.Height * scaleY);

                    // 画框
                    e.Graphics.DrawRectangle(pen, x, y, w, h);

                    // 画标签
                    var label = $"{detection.ClassName}: {detection.Confidence:F2}";
                    var labelSize = e.Graphics.MeasureString(label, font);
                    var labelRect = new RectangleF(x, y - labelSize.Height - 2, labelSize.Width + 4, labelSize.Height + 2);

                    e.Graphics.FillRectangle(bgBrush, labelRect);
                    e.Graphics.DrawString(label, font, Brushes.White, x + 2, y - labelSize.Height - 2);
                }
            }
        }
    }
}
