using System.Collections.Generic;

namespace YoloDetection
{
    /// <summary>
    /// 单个人的静电杆接触状态快照（不可变数据，订阅方可自由持有）。
    /// </summary>
    public class EsdPersonStatus
    {
        /// <summary>
        /// 轨迹编号：同一个人在画面中持续出现期间保持不变（跨帧跟踪），
        /// 用于日志/报警关联"是哪个人"；人离开画面超过遗忘时间后编号会更换。
        /// </summary>
        public int TrackId { get; set; }

        /// <summary>人体框中心 X / Y 与宽高（原图像素坐标系，与 DetectionResult 同义）</summary>
        public float X { get; set; }
        public float Y { get; set; }
        public float Width { get; set; }
        public float Height { get; set; }

        /// <summary>本帧左手腕是否落在静电杆区域内（含容差）</summary>
        public bool LeftWristInZone { get; set; }

        /// <summary>本帧右手腕是否落在静电杆区域内（含容差）</summary>
        public bool RightWristInZone { get; set; }

        /// <summary>
        /// 是否处于"正在触摸静电杆"状态：
        /// 手腕命中持续时长达到 HoldDurationMs 后置位，
        /// 离开（含宽限期）后复位。
        /// </summary>
        public bool InContact { get; set; }

        /// <summary>
        /// 当前连续接触累计时长（毫秒）。未接触时为 0；
        /// 宽限期内保持不增长也不清零。
        /// </summary>
        public double ContactElapsedMs { get; set; }

        /// <summary>人体检测置信度</summary>
        public float Confidence { get; set; }
    }

    /// <summary>
    /// 一帧的静电接触分析总快照（不可变数据）。
    /// Persons 为独立副本列表——修改本列表不会影响分析器内部状态
    /// （与 DetectionsUpdated 的快照契约一致）。
    /// </summary>
    public class EsdFrameSnapshot
    {
        /// <summary>本帧所有人的接触状态（含未接触的人）</summary>
        public List<EsdPersonStatus> Persons { get; set; } = new List<EsdPersonStatus>();

        /// <summary>当前处于触摸状态的人数（= InContact 计数，方便直接取用）</summary>
        public int ContactCount { get; set; }
    }
}
