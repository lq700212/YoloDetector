using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using YoloDetection;

namespace YoloDetector.UI
{
    /// <summary>
    /// 自带"拖拽框选 ROI"能力的 WPF 预览控件（玻璃拟态版薄壳）。
    ///
    /// 接入方用法（两步，与 WinForms 版 RoiSelectionPictureBox 完全一致）：
    ///   1. 把本控件当普通 Image 用（Source 显示画面，Stretch 用 Uniform 对应 Zoom letterbox）；
    ///   2. 订阅 RoiSelected 事件，拿到归一化 ROI(0~1) 自行决定用途
    ///      （热更新检测参数 / 写配置文件 / 存数据库……）。
    ///
    /// 内置能力：
    ///   - 按住左键拖拽框选，实时绘制黄色虚线框（与运行期 ESD ROI 黄框同色系）；
    ///   - 松手自动完成 Zoom(letterbox) 坐标换算，输出图像归一化矩形；
    ///   - 拖动幅度不足 5px 视为误触点击，静默忽略（状态机内置）；
    ///   - 光标 Cross 十字，提示可框选。
    ///
    /// 纯逻辑（状态机/坐标换算）位于检测类库（RoiSelectionState/ZoomMapping，注释明说
    /// WinForms/WPF 宿主通用），本类只是 WPF 薄壳：只做鼠标事件接线、虚线框绘制、
    /// 显示尺寸与图像像素尺寸的参数传递，不含任何框选算法。
    ///
    /// 与 WinForms 版的三点 WPF 差异（换算行为零差异）：
    ///   1. 拖拽出控件范围需显式 CaptureMouse 才能继续收 Move 事件（WinForms 默认捕获）；
    ///   2. Source 为空（未开始预览）时 Image 透明区域默认不参与命中测试，
    ///      override HitTestCore 让全区域可框选（与 WinForms PictureBox 行为对齐）；
    ///   3. 重绘用 InvalidateVisual（对应 WinForms Invalidate）。
    ///
    /// 线程契约：事件在 UI 线程触发（鼠标消息天然在 UI 线程），订阅方可直接更新控件。
    /// </summary>
    public class RoiSelectionImage : Image
    {
        private readonly RoiSelectionState _selection = new RoiSelectionState();

        // 黄色虚线框画笔（255,232,64）与 WinForms 版一致：冻结为只读资源，
        // OnRender 每帧复用，零分配
        private readonly Pen _dashPen;

        /// <summary>
        /// 框选完成：松手且拖动幅度达标时触发一次。
        /// 参数为图像归一化矩形（X/Y/W/H 均为 0~1 比例，与分辨率无关）。
        /// </summary>
        public event Action<EsdRoiRect> RoiSelected;

        public RoiSelectionImage()
        {
            Cursor = Cursors.Cross;
            Stretch = Stretch.Uniform; // 等比 letterbox，与 ZoomMapping 的换算模型一致

            var brush = new SolidColorBrush(Color.FromRgb(255, 232, 64));
            brush.Freeze();
            var pen = new Pen(brush, 2d) { DashStyle = DashStyles.Dash };
            pen.Freeze();
            _dashPen = pen;
        }

        /// <summary>让 Source 为空时控件整体仍可命中（WPF 透明区域默认穿透），保证未开预览时也能拖框</summary>
        protected override HitTestResult HitTestCore(PointHitTestParameters hitTestParameters)
        {
            return new PointHitTestResult(this, hitTestParameters.HitPoint);
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            Point p = e.GetPosition(this);
            _selection.Press((float)p.X, (float)p.Y);

            // 拖拽移出控件范围仍要继续收 Move/Up 事件（WinForms 的默认捕获行为，WPF 需显式捕获）
            CaptureMouse();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!_selection.IsSelecting)
            {
                return;
            }

            Point p = e.GetPosition(this);
            _selection.Drag((float)p.X, (float)p.Y);

            // 每帧图像替换（Source 更新）本身会触发重绘；这里主动失效一次，
            // 保证静止画面（无新帧到达）时虚线框也能跟手移动
            InvalidateVisual();
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);
            if (!ReferenceEquals(Mouse.Captured, this))
            {
                return;
            }

            ReleaseMouseCapture();
            HandleMouseUp(e.GetPosition(this));
        }

        // 强制失去捕获（如 Alt+Tab、窗口失焦打断拖拽）时复位状态机，
        // 防止"虚线框卡在半拖拽状态"（对应 WinForms MouseCaptureChanged 的保险）
        protected override void OnLostMouseCapture(MouseEventArgs e)
        {
            base.OnLostMouseCapture(e);
            if (_selection.IsSelecting)
            {
                _selection.Reset();
                InvalidateVisual();
            }
        }

        private void HandleMouseUp(Point p)
        {
            EsdRoiRect dragRect;
            if (!_selection.Release((float)p.X, (float)p.Y, out dragRect))
            {
                return; // 误触点击（幅度不足），状态机已复位
            }

            var source = Source as BitmapSource;
            if (source == null)
            {
                return; // 无画面可参照，忽略本次标定
            }

            EsdRoiRect normalized;
            if (ZoomMapping.TryMapDragToNormalizedRoi(
                    dragRect.X, dragRect.Y,
                    dragRect.X + dragRect.W, dragRect.Y + dragRect.H,
                    (int)Math.Round(ActualWidth), (int)Math.Round(ActualHeight),
                    source.PixelWidth, source.PixelHeight,
                    out normalized))
            {
                var handler = RoiSelected;
                if (handler != null)
                {
                    handler(normalized);
                }
            }
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            base.OnRender(drawingContext);

            if (!_selection.IsSelecting)
            {
                return;
            }

            EsdRoiRect rect = _selection.CurrentRectPixels;
            if (rect.W < 1f || rect.H < 1f)
            {
                return;
            }

            // 黄色虚线框（与运行期 ESD 叠加层的黄色 ROI 框同色系，视觉上前后呼应）
            drawingContext.DrawRectangle(null, _dashPen, new Rect(rect.X, rect.Y, rect.W, rect.H));
        }
    }
}
