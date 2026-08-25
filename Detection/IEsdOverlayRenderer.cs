using OpenCvSharp;

namespace YoloDetection
{
    /// <summary>
    /// 静电接触状态叠加渲染器：把 ROI 框、手腕落点、接触状态画到预览帧上。
    ///
    /// 所有权契约：Draw 原地修改传入的 frame 并返回同一实例——
    /// 调用方（检测管道）不需要对返回值做额外释放；
    /// frame 归调用方所有，本接口不取得所有权。
    ///
    /// 标签刻意使用英文（OpenCV PutText 不支持中文，中文会渲染成 ???）。
    /// </summary>
    public interface IEsdOverlayRenderer
    {
        /// <summary>在帧上绘制静电接触叠加层（原地修改）。snapshot 可为 null（跳过绘制）。</summary>
        void Draw(Mat frame, EsdFrameSnapshot snapshot, EsdAnalysisOptions options);
    }
}
