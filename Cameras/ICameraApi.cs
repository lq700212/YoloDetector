using System.Threading.Tasks;

namespace YoloDetector.Cameras
{
    /// <summary>
    /// 相机 API 抽象接口（品牌解耦核心）。
    ///
    /// 设计约定：
    ///   - 实现类在构造时绑定相机 IP，接口方法不再重复传递 IP 参数
    ///   - 不同品牌实现各自的通信协议，上层代码只依赖本接口
    ///   - 新增品牌：实现本接口 + 在 CameraApiFactory 注册即可
    /// </summary>
    public interface ICameraApi
    {
        /// <summary>测试相机连接（TCP/HTTP 探测），内部必须自带超时保护</summary>
        Task<bool> TestConnectionAsync();

        /// <summary>获取设备状态信息（CPU、内存、磁盘、录像数等）</summary>
        Task<DeviceStatus> GetDeviceStatusAsync();

        /// <summary>开启指定通道的 RTSP 拉流</summary>
        Task<bool> SetRtspEnableAsync(int channel);

        /// <summary>关闭指定通道的 RTSP 拉流</summary>
        Task<bool> SetRtspDisableAsync(int channel);

        /// <summary>开启指定通道的 RTMP 推流到 url</summary>
        Task<bool> SetRtmpEnableAsync(int channel, string url);

        /// <summary>关闭指定通道的 RTMP 推流</summary>
        Task<bool> SetRtmpDisableAsync(int channel);

        /// <summary>获取视频流地址（RTSP）</summary>
        string GetVideoStreamUrl(int channel);
    }
}
