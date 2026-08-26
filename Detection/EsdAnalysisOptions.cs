using System;

namespace YoloDetection
{
    /// <summary>
    /// 静电杆（ESD）接触分析的运行参数。
    ///
    /// 归属说明：本类属于检测模块（YoloDetection），由宿主从自身配置文件
    /// （如 Detection/esdConfig.json）读取后转换注入——模块本身不碰配置文件，
    /// 保证整目录可独立迁移（与 IYoloDetector 的阈值参数同一注入模式）。
    /// </summary>
    public sealed class EsdAnalysisOptions
    {
        // ---------- 静电杆区域（ROI）----------
        // 采用"归一化坐标 + 像素容差"组合：换分辨率不用重标 ROI，近距离微调用 Margin。

        /// <summary>ROI 左上角 X（相对帧宽的比例 0~1）</summary>
        public float RoiX { get; set; } = 0.40f;

        /// <summary>ROI 左上角 Y（相对帧高的比例 0~1）</summary>
        public float RoiY { get; set; } = 0.25f;

        /// <summary>ROI 宽（相对帧宽的比例 0~1）</summary>
        public float RoiW { get; set; } = 0.20f;

        /// <summary>ROI 高（相对帧高的比例 0~1）</summary>
        public float RoiH { get; set; } = 0.35f;

        /// <summary>
        /// 判定容差（像素）：手腕点到 ROI 外扩该距离以内也算命中。
        /// 关键点回归存在几像素抖动，紧贴杆沿的触摸动作靠它兜住。
        /// </summary>
        public float MarginPx { get; set; } = 20f;

        // ---------- 触摸判定状态机 ----------

        /// <summary>
        /// 持续命中时长阈值（毫秒）：手腕连续落在 ROI 内达到该时长才算"正在触摸"。
        /// 过路人的手扫过画面只会命中几十毫秒，用它过滤误报。现场建议 1000~2000ms。
        /// </summary>
        public double HoldDurationMs { get; set; } = 1500;

        /// <summary>
        /// 释放宽限（毫秒）：进入接触态后手腕短暂丢失（遮挡/关键点抖动）时，
        /// 在该时长内不判定为离开、累计时长不清零；超时才判定结束触摸。
        /// </summary>
        public double ReleaseGraceMs { get; set; } = 2000;

        /// <summary>
        /// 手腕关键点置信度阈值：低于该值的手腕坐标不可信，不参与命中判定。
        /// 与 IPoseDetector.KeyPointConfidenceThreshold 保持一致即可。
        /// </summary>
        public float WristConfidenceThreshold { get; set; } = 0.35f;

        // ---------- 跟踪 ----------

        /// <summary>
        /// 轨迹遗忘时间（毫秒）：连续这么久没有帧匹配上的人，其累计状态被删除。
        /// 决定"人走了再回来要不要重新计时"——超过即重新计时。
        /// </summary>
        public double TrackForgetMs { get; set; } = 3000;

        /// <summary>轨迹表容量上限，超出时丢弃最久未更新的轨迹（防拥挤场景内存无界增长）</summary>
        public int MaxTrackedPersons { get; set; } = 16;

        // ---------- 叠加显示 ----------

        /// <summary>是否在预览帧上绘制 ROI 与接触状态叠加层</summary>
        public bool DrawOverlay { get; set; } = true;

        /// <summary>
        /// 是否为"未接触静电杆"的被跟踪人员绘制灰色整身框（#N NO GND）。
        /// 默认 false：未触摸是常态，常驻灰框叠在 YOLO 红框上视觉噪音大，
        /// 且人离开后的宽限残留框容易被误读成"多出一个人"；现场需要目检
        /// 轨迹跟踪效果或核对 ROI 手腕落区时再临时打开。
        /// 接触中的绿色 ESD OK 框是关键事件提示，不受本开关影响始终绘制。
        /// </summary>
        public bool DrawNoContactBoxes { get; set; } = false;

        /// <summary>
        /// 运行期热更新 ROI（归一化坐标）：非法值就地夹紧，语义与宿主 ToOptions 一致。
        ///
        /// 使用场景：UI 拖拽框选静电杆区域后由宿主调用。本实例被 EsdContactAnalyzer
        /// 长期持有且每帧读取，改字段即下一帧生效，无需重建检测链路。
        ///
        /// 线程契约：允许 UI 线程调用（检测线程并发读取）。四个字段非原子写入，
        /// 极端情况下检测线程某帧可能读到"半新半旧"的组合值——后果仅为该帧判定
        /// 区域短暂偏移（毫秒级自愈），故刻意不加锁：ROI 微调不需要强一致，
        /// 而每帧读取走锁是白耗性能。
        /// </summary>
        public void ApplyNormalizedRoi(float roiX, float roiY, float roiW, float roiH)
        {
            RoiX = Clamp01(roiX);
            RoiY = Clamp01(roiY);
            RoiW = Math.Max(0.01f, Clamp01(roiW)); // 宽高下限防止退化成零面积区域
            RoiH = Math.Max(0.01f, Clamp01(roiH));
        }

        private static float Clamp01(float v)
        {
            return v < 0f ? 0f : (v > 1f ? 1f : v);
        }
    }
}
