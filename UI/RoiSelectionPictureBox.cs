using System;
using System.Drawing;
using System.Windows.Forms;
using YoloDetection;

namespace YoloDetector.UI
{
    /// <summary>
    /// 自带"拖拽框选 ROI"能力的预览控件（傻瓜化封装）。
    ///
    /// 接入方用法（两步）：
    ///   1. 把本控件当普通 PictureBox 用（设 Image 显示画面）；
    ///   2. 订阅 RoiSelected 事件，拿到归一化 ROI(0~1) 想干什么自己定
    ///      （热更新检测参数 / 写配置文件 / 存数据库……）。
    ///
    /// 内置能力（全部 override 封装，接入方零事件接线）：
    ///   - 按住左键拖拽框选，实时绘制黄色虚线框（与运行期 ESD ROI 黄框同色系）；
    ///   - 松手自动完成 Zoom(letterbox) 坐标换算，输出图像归一化矩形；
    ///   - 拖动幅度不足 5px 视为误触点击，静默忽略；
    ///   - 光标默认 Cross 十字，提示可框选。
    ///
    /// 纯逻辑（状态机/坐标换算）位于检测类库（RoiSelectionState/ZoomMapping），
    /// 本控件只是 WinForms 薄壳——非 WinForms 宿主可参考本类自行接线。
    ///
    /// 线程契约：事件在 UI 线程触发（鼠标消息天然在 UI 线程），订阅方可直接更新控件。
    /// </summary>
    public class RoiSelectionPictureBox : PictureBox
    {
        private readonly RoiSelectionState _selection = new RoiSelectionState();

        /// <summary>
        /// 框选完成：松手且拖动幅度达标时触发一次。
        /// 参数为图像归一化矩形（X/Y/W/H 均为 0~1 比例，与分辨率无关）。
        /// </summary>
        public event Action<EsdRoiRect> RoiSelected;

        public RoiSelectionPictureBox()
        {
            Cursor = Cursors.Cross;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
            {
                _selection.Press(e.X, e.Y);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!_selection.IsSelecting)
            {
                return;
            }

            _selection.Drag(e.X, e.Y);

            // 每帧图像替换本身会触发重绘；这里主动失效一次，
            // 保证静止画面（无新帧到达）时虚线框也能跟手移动
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            EsdRoiRect dragRect;
            if (!_selection.Release(e.X, e.Y, out dragRect))
            {
                return; // 误触点击（幅度不足），状态机已复位
            }

            var image = Image;
            if (image == null)
            {
                return; // 无画面可参照，忽略本次标定
            }

            EsdRoiRect normalized;
            if (ZoomMapping.TryMapDragToNormalizedRoi(
                    dragRect.X, dragRect.Y,
                    dragRect.X + dragRect.W, dragRect.Y + dragRect.H,
                    ClientSize.Width, ClientSize.Height,
                    image.Width, image.Height,
                    out normalized))
            {
                var handler = RoiSelected;
                if (handler != null)
                {
                    handler(normalized);
                }
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

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
            using (var pen = new Pen(Color.FromArgb(255, 232, 64), 2f))
            {
                pen.DashStyle = System.Drawing.Drawing2D.DashStyle.Dash;
                e.Graphics.DrawRectangle(pen, rect.X, rect.Y, rect.W, rect.H);
            }
        }
    }
}
