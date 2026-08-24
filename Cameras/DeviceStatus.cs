namespace YoloDetector.Cameras
{
    /// <summary>
    /// 设备状态通用数据模型。
    /// 不同品牌相机返回的数据格式不同，但都会转换为本统一格式后在接口层传递。
    /// </summary>
    public class DeviceStatus
    {
        public string IpAddress { get; set; }

        public string Brand { get; set; }

        /// <summary>CPU 使用率（0-100）</summary>
        public float CpuUsage { get; set; }

        /// <summary>内存使用率（0-100）</summary>
        public float MemoryUsage { get; set; }

        /// <summary>磁盘总容量（字节）</summary>
        public long DiskTotal { get; set; }

        /// <summary>磁盘可用空间（字节）</summary>
        public long DiskFree { get; set; }

        public int TotalVideoCount { get; set; }

        /// <summary>RTMP 带宽（kbps）</summary>
        public int RtmpBandwidth { get; set; }

        /// <summary>RTSP 带宽（kbps）</summary>
        public int RtspBandwidth { get; set; }

        /// <summary>磁盘使用率（百分比）</summary>
        public float GetDiskUsage()
        {
            if (DiskTotal <= 0) return 0;
            return ((float)(DiskTotal - DiskFree) / DiskTotal) * 100;
        }

        public string GetFormattedDiskTotal()
        {
            return FormatBytes(DiskTotal);
        }

        public string GetFormattedDiskFree()
        {
            return FormatBytes(DiskFree);
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString("F2") + " KB";
            if (bytes < 1024L * 1024 * 1024) return (bytes / (1024.0 * 1024)).ToString("F2") + " MB";
            return (bytes / (1024.0 * 1024 * 1024)).ToString("F2") + " GB";
        }
    }
}
