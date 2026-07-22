using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace YoloDetector.YoloDetection
{
    /// <summary>
    /// 浮动透明覆盖窗口 — 替代托管控件覆盖层，解决与native VideoView的Z轴问题
    /// 使用原生窗口 + TransparencyKey实现真透明，原生窗口可以对等控制Z轴
    /// </summary>
    public class DetectionOverlayForm : Form
    {
        private List<DetectionResult> _detections = new List<DetectionResult>();
        private int _frameWidth = 1920;
        private int _frameHeight = 1080;
        private Control _anchorControl; // 锚定控件（VideoView），覆盖层跟随其位置和大小

        // 缓存GDI绘制对象，避免每次OnPaint时重新创建（OnPaint每100ms触发一次）
        private readonly Pen _detectionPen = new Pen(Color.Red, 2);
        private readonly Font _labelFont = new Font("Arial", 10, FontStyle.Bold);
        private readonly SolidBrush _labelBgBrush = new SolidBrush(Color.FromArgb(200, Color.Red));

        public DetectionOverlayForm(Control anchorControl)
        {
            _anchorControl = anchorControl ?? throw new ArgumentNullException(nameof(anchorControl));

            // 无边框、不显示在任务栏
            this.FormBorderStyle = FormBorderStyle.None;
            this.ShowInTaskbar = false;
            this.StartPosition = FormStartPosition.Manual;

            // 透明键：Magenta被当作完全透明
            this.TransparencyKey = Color.Magenta;
            this.BackColor = Color.Magenta;

            // 双缓冲提升绘制性能
            this.DoubleBuffered = true;

            // 不在任务栏获取焦点
            this.ShowIcon = false;

            // 注册锚定控件的移动/大小变更事件
            if (_anchorControl is Control ctrl)
            {
                ctrl.Move += (s, e) => SyncPosition();
                ctrl.Resize += (s, e) => SyncPosition();
                ctrl.ParentChanged += (s, e) => SyncPosition();
            }
            if (_anchorControl.Parent != null)
            {
                _anchorControl.Parent.Move += (s, e) => SyncPosition();
                _anchorControl.Parent.Resize += (s, e) => SyncPosition();
            }
        }

        protected override bool ShowWithoutActivation => true; // 不抢焦点

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE: 不激活
                cp.ExStyle |= 0x00000020; // WS_EX_TRANSPARENT: 鼠标穿透
                return cp;
            }
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

        public void SyncPosition()
        {
            if (_anchorControl == null || _anchorControl.IsDisposed)
                return;

            // 将锚定控件的屏幕坐标映射到覆盖层
            var screenPos = _anchorControl.PointToScreen(Point.Empty);
            this.Location = screenPos;
            this.Size = _anchorControl.Size;
        }

        public new void Show()
        {
            SyncPosition();
            base.Show();
        }

        public new void Hide()
        {
            base.Hide();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            if (_detections == null || _detections.Count == 0)
                return;
            if (_frameWidth <= 0 || _frameHeight <= 0)
                return;

            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // === 关键修复：计算LibVLC letterboxing偏移 ===
            // LibVLC默认保持视频宽高比，VideoView和视频比例不同时会有黑边
            // 覆盖层需要和视频内容对齐，而不是和VideoView全尺寸对齐
            float videoAspect = (float)_frameWidth / _frameHeight;
            float viewAspect = (float)this.Width / this.Height;
            float videoW, videoH, offsetX, offsetY;

            if (videoAspect > viewAspect)
            {
                // 视频更宽 → 上下黑边
                videoW = this.Width;
                videoH = this.Width / videoAspect;
                offsetX = 0;
                offsetY = (this.Height - videoH) / 2f;
            }
            else
            {
                // 视频更高 → 左右黑边
                videoH = this.Height;
                videoW = this.Height * videoAspect;
                offsetX = (this.Width - videoW) / 2f;
                offsetY = 0;
            }

            float scaleX = videoW / _frameWidth;
            float scaleY = videoH / _frameHeight;

            // 使用缓存的GDI对象，避免每次OnPaint都创建/销毁
            foreach (var detection in _detections)
            {
                int x = (int)(offsetX + detection.Left * scaleX);
                int y = (int)(offsetY + detection.Top * scaleY);
                int w = (int)Math.Max(1, detection.Width * scaleX);
                int h = (int)Math.Max(1, detection.Height * scaleY);

                g.DrawRectangle(_detectionPen, x, y, w, h);

                var label = $"{detection.ClassName}: {detection.Confidence:F2}";
                var labelSize = g.MeasureString(label, _labelFont);
                var labelRect = new RectangleF(x, (float)Math.Max(0, y - labelSize.Height - 2),
                    labelSize.Width + 4, labelSize.Height + 2);

                g.FillRectangle(_labelBgBrush, labelRect);
                g.DrawString(label, _labelFont, Brushes.White, x + 2, (float)Math.Max(0, y - labelSize.Height));
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _detectionPen?.Dispose();
                _labelFont?.Dispose();
                _labelBgBrush?.Dispose();
                _anchorControl = null;
            }
            base.Dispose(disposing);
        }
    }
}
