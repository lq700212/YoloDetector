using OpenCvSharp;

namespace YoloDetection
{
    /// <summary>
    /// 门状态监测叠加渲染器接口：把门区域框与状态标签画到预览帧上。
    /// 实现必须原地绘制（不克隆帧），调用方保证帧所有权不变。
    /// </summary>
    public interface IDoorOverlayRenderer
    {
        /// <summary>
        /// 在帧上绘制门状态叠加层。
        /// </summary>
        /// <param name="frame">预览帧（原地修改）</param>
        /// <param name="snapshot">最近一次门监测快照（可能为 null：未启用/尚未产出）</param>
        /// <param name="options">门监测参数（取 ROI 绘制）</param>
        void Draw(Mat frame, DoorFrameSnapshot snapshot, DoorMonitorOptions options);
    }
}
