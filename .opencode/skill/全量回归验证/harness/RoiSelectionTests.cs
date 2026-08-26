using System;
using YoloDetection;

namespace YoloDetector.Tests
{
    // ============================================================
    // ROI 拖拽标定纯逻辑层测试（逻辑位于 Detection 类库，无需 STA/真实控件）：
    //
    //   ZoomMapping       —— Zoom(letterbox) 模式控件坐标 ↔ 图像归一化坐标换算
    //                        + 一次拖拽端到端换算归一化 ROI
    //   RoiSelectionState —— 按下/拖动/松开的框选状态机
    //
    // 宿主的 RoiSelectionPictureBox 只是这两个类的 WinForms 薄壳（override 接线），
    // 真实拖拽链路由 GUI 冒烟 + STA 目检探针整体兜底（见 SKILL.md 对账表说明）。
    // ============================================================

    internal static class RoiSelectionTests
    {
        public static void RunAll()
        {
            T.Case("UI坐标-Zoom显示矩形居中计算", Zoom_DisplayRect);
            T.Case("UI坐标-控件点映射归一化与黑边夹紧", Zoom_MapAndClamp);
            T.Case("UI坐标-无效尺寸返回false", Zoom_InvalidSize);
            T.Case("UI坐标-拖拽矩形端到端换算归一化ROI", Zoom_DragToNormalizedRoi);
            T.Case("UI拖拽-正常框选输出矩形", Drag_NormalFlow);
            T.Case("UI拖拽-误触点击不产生ROI", Drag_ClickIgnored);
            T.Case("UI拖拽-反向拖拽矩形规范化", Drag_ReversedRect);
            T.Case("UI拖拽-未按下时Drag被忽略", Drag_IgnoreWithoutPress);
        }

        private const float Eps = 0.01f;

        /// <summary>宽图放进竖高控件：上下无黑边、左右居中留黑边</summary>
        private static void Zoom_DisplayRect()
        {
            EsdRoiRect rect;
            bool ok = ZoomMapping.TryGetDisplayRect(800, 400, 1920, 1080, out rect);

            T.True(ok, "合法尺寸应返回 true");
            // scale = min(800/1920, 400/1080) ≈ 0.3704 → 显示区 711x400，左右各留 (800-711)/2
            T.True(Math.Abs(rect.H - 400f) < Eps, "高应撑满控件: " + rect.H);
            T.True(Math.Abs(rect.W - 711.11f) < 0.1f, "宽按比例缩放: " + rect.W);
            T.True(Math.Abs(rect.X - 44.44f) < 0.1f, "水平居中偏移");
            T.True(Math.Abs(rect.Y) < Eps, "垂直方向贴边无偏移");

            // 竖图放进横宽控件：scale = min(800/320, 400/640) = 0.625 → 显示区 200x400，
            // 高撑满、水平居中（与上一场景互为镜像，锁定"取小缩放比+双向居中"算法）
            EsdRoiRect rect2;
            ZoomMapping.TryGetDisplayRect(800, 400, 320, 640, out rect2);
            T.True(Math.Abs(rect2.W - 200f) < Eps, "竖图宽度受比例限制: " + rect2.W);
            T.True(Math.Abs(rect2.H - 400f) < Eps, "竖图高度撑满控件");
            T.True(Math.Abs(rect2.X - 300f) < Eps, "水平居中偏移: " + rect2.X);
            T.Eq(0f, rect2.Y, "垂直方向贴边无偏移");
        }

        /// <summary>图像中心点映射 (0.5,0.5)；黑边区域坐标夹紧到 [0,1]</summary>
        private static void Zoom_MapAndClamp()
        {
            float nx, ny;

            // 布局同上：显示区 X∈[44.44, 755.56]，Y∈[0,400]
            bool ok = ZoomMapping.TryMapControlToNormalized(
                400f, 200f, 800, 400, 1920, 1080, out nx, out ny);
            T.True(ok, "合法点位应映射成功");
            T.True(Math.Abs(nx - 0.5f) < Eps, "图像水平中心 → nx=0.5，实际=" + nx);
            T.True(Math.Abs(ny - 0.5f) < Eps, "图像垂直中心 → ny=0.5，实际=" + ny);

            // 左侧黑边区域：nx 夹紧到 0；ny 仍在范围内正常换算
            ZoomMapping.TryMapControlToNormalized(
                10f, 100f, 800, 400, 1920, 1080, out nx, out ny);
            T.Eq(0f, nx, "左侧黑边 nx 夹紧到 0");
            T.True(Math.Abs(ny - 0.25f) < Eps, "黑边内 ny 正常换算 100/400=0.25");

            // 控件外右下角：双向都夹紧到 1
            ZoomMapping.TryMapControlToNormalized(
                9999f, 9999f, 800, 400, 1920, 1080, out nx, out ny);
            T.Eq(1f, nx, "超出右缘 nx 夹紧到 1");
            T.Eq(1f, ny, "超出下缘 ny 夹紧到 1");
        }

        private static void Zoom_InvalidSize()
        {
            EsdRoiRect rect;
            T.False(ZoomMapping.TryGetDisplayRect(0, 400, 1920, 1080, out rect),
                "控件宽为 0 应返回 false");
            float nx, ny;
            T.False(ZoomMapping.TryMapControlToNormalized(
                    10f, 10f, 800, 400, 0, 1080, out nx, out ny),
                "图像未加载(宽 0) 应返回 false");
        }

        /// <summary>拖拽两端点一次换算成归一化 ROI：包围盒/最小面积/贴边回收全链路</summary>
        private static void Zoom_DragToNormalizedRoi()
        {
            // 布局同上（显示区 X∈[44.44,755.56], Y∈[0,400]）：
            // 起点(400,200)=归一化(0.5,0.5)，终点(755.56,400)=归一化(1.0,1.0)
            EsdRoiRect roi;
            bool ok = ZoomMapping.TryMapDragToNormalizedRoi(
                400f, 200f, 755.56f, 400f, 800, 400, 1920, 1080, out roi);

            T.True(ok, "合法拖拽应换算成功");
            T.True(Math.Abs(roi.X - 0.5f) < Eps, "ROI.X=0.5，实际=" + roi.X);
            T.True(Math.Abs(roi.Y - 0.5f) < Eps, "ROI.Y=0.5");
            T.True(Math.Abs(roi.W - 0.5f) < Eps, "ROI.W=0.5");
            T.True(Math.Abs(roi.H - 0.5f) < Eps, "ROI.H=0.5");

            // 终点甩出控件右下角：夹紧到 1 后贴边，且保证 X+W ≤ 1 不出界
            ZoomMapping.TryMapDragToNormalizedRoi(
                400f, 200f, 9999f, 9999f, 800, 400, 1920, 1080, out roi);
            T.True(Math.Abs((roi.X + roi.W) - 1f) < Eps, "贴右缘时 X+W=1");
            T.True(Math.Abs((roi.Y + roi.H) - 1f) < Eps, "贴下缘时 Y+H=1");

            // 反向拖拽（从右下往左上）：包围盒语义不变
            ZoomMapping.TryMapDragToNormalizedRoi(
                755.56f, 400f, 400f, 200f, 800, 400, 1920, 1080, out roi);
            T.True(Math.Abs(roi.X - 0.5f) < Eps, "反向拖拽 X 仍为 0.5");
            T.True(Math.Abs(roi.W - 0.5f) < Eps, "反向拖拽 W 仍为 0.5");

            // 图像无效返回 false
            T.False(ZoomMapping.TryMapDragToNormalizedRoi(
                0f, 0f, 100f, 100f, 800, 400, 0, 0, out roi), "图像尺寸无效应返回 false");
        }

        private static void Drag_NormalFlow()
        {
            var sel = new RoiSelectionState();

            T.False(sel.IsSelecting, "初始不在选框状态");

            sel.Press(100f, 100f);
            T.True(sel.IsSelecting, "按下后进入选框状态");

            sel.Drag(300f, 250f);
            EsdRoiRect live = sel.CurrentRectPixels;
            T.Eq(100f, live.X, "实时矩形左缘");
            T.Eq(200f, live.W, "实时矩形宽度(dx)");
            T.Eq(150f, live.H, "实时矩形高度(dy)");

            EsdRoiRect released;
            bool ok = sel.Release(320f, 260f, out released);
            T.True(ok, "幅度达标的松手应返回 true");
            T.Eq(220f, released.W, "松手矩形宽度含最后位移");
            T.Eq(160f, released.H, "松手矩形高度含最后位移");
            T.False(sel.IsSelecting, "松手后退出选框状态");
            T.Eq(0f, sel.CurrentRectPixels.W, "复位后实时矩形清空");
        }

        private static void Drag_ClickIgnored()
        {
            var sel = new RoiSelectionState();
            sel.Press(50f, 50f);

            EsdRoiRect rect;
            T.False(sel.Release(52f, 51f, out rect), "位移 <5px 视为误触点击返回 false");
            T.Eq(0f, rect.W, "误触时矩形为空（契约：false 时一律 Empty，调用方免判幅度）");
            T.False(sel.IsSelecting, "误触后状态已复位");

            // 未按下直接 Release 同样安全
            var idle = new RoiSelectionState();
            T.False(idle.Release(10f, 10f, out rect), "无进行中框选时返回 false");
        }

        private static void Drag_ReversedRect()
        {
            var sel = new RoiSelectionState();
            sel.Press(300f, 260f);   // 从右下往左上反着框
            sel.Drag(120f, 140f);

            EsdRoiRect rect;
            bool ok = sel.Release(110f, 130f, out rect);

            T.True(ok, "反向框选同样有效");
            T.Eq(110f, rect.X, "矩形自动规范化为左上角起点");
            T.Eq(130f, rect.Y, "矩形自动规范化为顶部起点");
            T.Eq(190f, rect.W, "反向拖拽的绝对宽度");
            T.Eq(130f, rect.H, "反向拖拽的绝对高度");
        }

        private static void Drag_IgnoreWithoutPress()
        {
            var sel = new RoiSelectionState();
            sel.Drag(200f, 200f);
            T.False(sel.IsSelecting, "未按下时 Drag 不应进入选框状态");
            T.Eq(0f, sel.CurrentRectPixels.W, "未按下时无实时矩形");
        }
    }
}
