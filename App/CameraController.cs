using System;
using System.Threading;
using System.Threading.Tasks;
using YoloDetector.Cameras;

namespace YoloDetector.App
{
    /// <summary>
    /// 相机连接控制器：封装相机客户端生命周期、连接状态与状态轮询。
    ///
    /// 设计要点：
    ///   1. 客户端实例的创建/替换集中在此，UI 层不直接接触 ICameraApi
    ///   2. 状态轮询内置防重入保护：上一次请求未完成时自动跳过本轮，
    ///      避免网络卡顿导致 async 轮询请求无限堆积
    /// </summary>
    public sealed class CameraController
    {
        private ICameraApi _client;
        private int _statusPollInFlight;

        /// <summary>当前是否已连接</summary>
        public bool IsConnected
        {
            get { return _client != null; }
        }

        /// <summary>当前连接的 IP（未连接时为 null）</summary>
        public string ConnectedIp { get; private set; }

        /// <summary>
        /// 连接相机。成功返回 true；失败返回 false（内部状态保持未连接）。
        /// </summary>
        public async Task<bool> ConnectAsync(string ip)
        {
            if (string.IsNullOrEmpty(ip))
                throw new ArgumentException("IP地址不能为空", nameof(ip));

            Disconnect();

            ICameraApi client = CameraApiFactory.Create(ip);
            bool ok = await client.TestConnectionAsync().ConfigureAwait(false);

            if (ok)
            {
                _client = client;
                ConnectedIp = ip;
            }

            return ok;
        }

        /// <summary>断开连接（幂等）</summary>
        public void Disconnect()
        {
            _client = null;
            ConnectedIp = null;
        }

        /// <summary>
        /// 获取设备状态快照（防重入：上一轮未完成时返回 null 表示跳过本轮）。
        /// 未连接时也返回 null。
        /// </summary>
        public async Task<DeviceStatus> TryGetStatusAsync()
        {
            ICameraApi client = _client;
            if (client == null)
            {
                return null;
            }

            // 防重入：仅允许一个在途的状态查询
            if (Interlocked.CompareExchange(ref _statusPollInFlight, 1, 0) != 0)
            {
                return null;
            }

            try
            {
                return await client.GetDeviceStatusAsync().ConfigureAwait(false);
            }
            finally
            {
                Interlocked.Exchange(ref _statusPollInFlight, 0);
            }
        }

        /// <summary>开启/关闭指定通道的 RTSP 拉流。未连接时返回 false。</summary>
        public async Task<bool> SetRtspAsync(int channel, bool enable)
        {
            ICameraApi client = _client;
            if (client == null) return false;

            return enable
                ? await client.SetRtspEnableAsync(channel).ConfigureAwait(false)
                : await client.SetRtspDisableAsync(channel).ConfigureAwait(false);
        }

        /// <summary>开启/关闭指定通道的 RTMP 推流。未连接时返回 false。</summary>
        public async Task<bool> SetRtmpAsync(int channel, string url, bool enable)
        {
            ICameraApi client = _client;
            if (client == null) return false;

            return enable
                ? await client.SetRtmpEnableAsync(channel, url).ConfigureAwait(false)
                : await client.SetRtmpDisableAsync(channel).ConfigureAwait(false);
        }

        /// <summary>生成指定通道的视频流地址（未连接时基于传入 ip 或默认 ip 生成）</summary>
        public string BuildStreamUrl(int channel, string fallbackIp)
        {
            ICameraApi client = _client;
            if (client != null)
            {
                return client.GetVideoStreamUrl(channel);
            }

            var config = Configuration.AppConfig.Current;
            string ip = string.IsNullOrEmpty(fallbackIp) ? config.Connection.DefaultIp : fallbackIp;
            return config.Stream.GetRtspUrl(ip, channel);
        }
    }
}
