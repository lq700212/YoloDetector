using System;

namespace YoloDetection
{
    /// <summary>
    /// 静电接触状态翻转事件参数。
    /// </summary>
    public class EsdContactChangedEventArgs : EventArgs
    {
        /// <summary>人员轨迹编号（同一个人持续在画面内时保持不变）</summary>
        public int TrackId { get; set; }

        /// <summary>true=开始触摸静电杆；false=结束触摸</summary>
        public bool InContact { get; set; }

        /// <summary>触发时刻的累计接触时长（毫秒；结束时为清零前的最终值）</summary>
        public double ContactElapsedMs { get; set; }

        public EsdContactChangedEventArgs(int trackId, bool inContact, double contactElapsedMs)
        {
            TrackId = trackId;
            InContact = inContact;
            ContactElapsedMs = contactElapsedMs;
        }
    }
}
