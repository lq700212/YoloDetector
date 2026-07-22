using System.Threading.Tasks;

namespace YoloDetector
{
    // ============================================================
    // 相机API接口
    // ============================================================
    // 功能：定义所有相机API的通用方法签名
    // 
    // 设计说明：
    //   - 这是一个接口（interface），只定义方法签名，不包含具体实现
    //   - 不同品牌的相机可以实现这个接口，提供各自的实现逻辑
    //   - 通过这个接口，可以实现相机品牌的解耦
    //   - 当更换相机品牌时，只需要：
    //     1. 创建一个新的实现类（如HikCameraApiClient）
    //     2. 修改配置文件中的CameraBrand字段
    //     3. 不需要修改MainForm等调用代码
    //
    // 使用方式：
    //   ICameraApi cameraApi = CameraApiFactory.Create();
    //   var status = await cameraApi.GetDeviceStatusAsync(ip);
    // ============================================================
    public interface ICameraApi
    {
        // ============================================================
        // 测试相机连接
        // 参数：ip - 相机IP地址
        // 返回：是否连接成功
        // ============================================================
        Task<bool> TestConnectionAsync(string ip);

        // ============================================================
        // 获取设备状态信息
        // 参数：ip - 相机IP地址
        // 返回：设备状态对象（包含CPU、内存、磁盘、录像数等信息）
        // ============================================================
        Task<DeviceStatus> GetDeviceStatusAsync(string ip);

        // ============================================================
        // 开启RTSP拉流
        // 参数：ip - 相机IP地址；channel - 通道号
        // 返回：是否开启成功
        // ============================================================
        Task<bool> SetRtspEnableAsync(string ip, int channel);

        // ============================================================
        // 关闭RTSP拉流
        // 参数：ip - 相机IP地址；channel - 通道号
        // 返回：是否关闭成功
        // ============================================================
        Task<bool> SetRtspDisableAsync(string ip, int channel);

        // ============================================================
        // 开启RTMP推流
        // 参数：ip - 相机IP地址；channel - 通道号；url - 推流地址
        // 返回：是否开启成功
        // ============================================================
        Task<bool> SetRtmpEnableAsync(string ip, int channel, string url);

        // ============================================================
        // 关闭RTMP推流
        // 参数：ip - 相机IP地址；channel - 通道号
        // 返回：是否关闭成功
        // ============================================================
        Task<bool> SetRtmpDisableAsync(string ip, int channel);

        // ============================================================
        // 获取视频流地址
        // 参数：ip - 相机IP地址；channel - 通道号；streamType - 流类型（RTSP/RTMP）
        // 返回：视频流地址字符串
        // ============================================================
        string GetVideoStreamUrl(string ip, int channel, string streamType = "RTSP");
    }

    // ============================================================
    // 设备状态类（通用数据模型）
    // ============================================================
    // 功能：存储从相机获取的设备状态信息
    // 
    // 说明：这是一个通用的数据模型，用于在接口层传递数据
    //       不同品牌的相机可能返回不同格式的数据，但都会转换为这个通用格式
    // ============================================================
    public class DeviceStatus
    {
        // ============================================================
        // 基础信息
        // ============================================================
        
        // 相机IP地址
        public string IpAddress { get; set; }

        // 相机品牌
        public string Brand { get; set; }

        // ============================================================
        // 系统资源信息
        // ============================================================
        
        // CPU使用率（百分比，0-100）
        public float CpuUsage { get; set; }

        // 内存使用率（百分比，0-100）
        public float MemoryUsage { get; set; }

        // 磁盘总容量（字节）
        public long DiskTotal { get; set; }

        // 磁盘可用空间（字节）
        public long DiskFree { get; set; }

        // ============================================================
        // 视频流信息
        // ============================================================
        
        // 录像总数
        public int TotalVideoCount { get; set; }

        // RTMP带宽（kbps）
        public int RtmpBandwidth { get; set; }

        // RTSP带宽（kbps）
        public int RtspBandwidth { get; set; }

        // ============================================================
        // 状态检查方法
        // ============================================================
        
        // 获取磁盘使用率（百分比）
        public float GetDiskUsage()
        {
            if (DiskTotal <= 0) return 0;
            return ((float)(DiskTotal - DiskFree) / DiskTotal) * 100;
        }

        // 获取磁盘总容量（格式化字符串，如"100 GB"）
        public string GetFormattedDiskTotal()
        {
            return FormatBytes(DiskTotal);
        }

        // 获取磁盘可用空间（格式化字符串，如"50 GB"）
        public string GetFormattedDiskFree()
        {
            return FormatBytes(DiskFree);
        }

        // 将字节数格式化为可读的字符串
        private string FormatBytes(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString("F2") + " KB";
            if (bytes < 1024 * 1024 * 1024) return (bytes / (1024.0 * 1024)).ToString("F2") + " MB";
            return (bytes / (1024.0 * 1024 * 1024)).ToString("F2") + " GB";
        }
    }

    // ============================================================
    // API响应类（通用数据模型）
    // ============================================================
    // 功能：存储相机API返回的响应数据
    // 
    // 说明：这是一个通用的API响应模型，用于解析相机API返回的JSON数据
    //       不同品牌的相机可能返回不同格式的响应，但都会转换为这个通用格式
    // ============================================================
    public class ApiResponse
    {
        // 响应状态码（0表示成功，非0表示失败）
        public int Code { get; set; }

        // 响应消息
        public string Msg { get; set; }

        // 响应数据（JSON字符串格式）
        public string Data { get; set; }

        // 是否成功（Code == 0表示成功）
        public bool IsSuccess => Code == 0;
    }
}
