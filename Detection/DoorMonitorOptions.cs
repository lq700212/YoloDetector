using System;

namespace YoloDetection
{
    /// <summary>
    /// 门状态监测（工位操作间门开/关识别）的运行参数。
    ///
    /// 归属说明：本类属于检测模块（YoloDetection），由宿主从自身配置文件
    /// （Detection/doorConfig.json）读取后转换注入——模块本身不碰配置文件，
    /// 保证整目录可独立迁移（与 EsdAnalysisOptions 同一注入模式）。
    /// </summary>
    public sealed class DoorMonitorOptions
    {
        // ---------- 门区域（ROI）----------
        // 归一化坐标：换分辨率不用重标，与 ESD 静电杆 ROI 同一约定。

        /// <summary>ROI 左上角 X（相对帧宽的比例 0~1）</summary>
        public float RoiX { get; set; } = 0.85f;

        /// <summary>ROI 左上角 Y（相对帧高的比例 0~1）</summary>
        public float RoiY { get; set; } = 0.10f;

        /// <summary>ROI 宽（相对帧宽比例 0~1）</summary>
        public float RoiW { get; set; } = 0.12f;

        /// <summary>ROI 高（相对帧高比例 0~1）</summary>
        public float RoiH { get; set; } = 0.55f;

        /// <summary>
        /// 差异阈值（灰度 0~255）：门区域当前帧与"关门基准图"的亮度归一化
        /// 平均绝对差超过该值判定为"门开"。基准比对前会对齐两图的整体亮度
        /// （消除昼夜/开关灯造成的整体明暗漂移，只认结构变化——门开了会露出
        /// 门外的新内容）。现场误报多就调大、门开了不报就调小。
        /// </summary>
        public float DiffThreshold { get; set; } = 18f;

        /// <summary>
        /// 状态翻转防抖（毫秒）：候选状态（与当前状态不同的判定结果）持续该时长
        /// 才真正翻转。主要过滤"人走过门区域"的短暂遮挡（配合人体框相交排除
        /// 双保险），以及关键点抖动造成的单帧误判。
        /// </summary>
        public double StateHoldMs { get; set; } = 1500;

        /// <summary>关门基准图路径（相对程序 exe 目录）。首次采集自动保存，重启复用。</summary>
        public string BaselinePath { get; set; } = "Detection/door_baseline.png";

        /// <summary>是否在预览帧上绘制门区域状态叠加层</summary>
        public bool DrawOverlay { get; set; } = true;

        /// <summary>
        /// 运行期热更新 ROI（归一化坐标）：非法值就地夹紧，语义与宿主 ToOptions 一致。
        /// 使用场景：UI 拖拽框选门区域后由宿主调用，改字段即下一帧生效。
        /// 线程契约：允许 UI 线程调用（检测线程并发读取），与 ESD ROI 热更新同一约定。
        /// </summary>
        public void ApplyNormalizedRoi(float roiX, float roiY, float roiW, float roiH)
        {
            RoiX = Clamp01(roiX);
            RoiY = Clamp01(roiY);
            RoiW = Math.Max(0.01f, Clamp01(roiW));
            RoiH = Math.Max(0.01f, Clamp01(roiH));
        }

        private static float Clamp01(float v)
        {
            return v < 0f ? 0f : (v > 1f ? 1f : v);
        }
    }
}
