using System;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace YoloDetector.Cameras
{
    /// <summary>
    /// 安格华（ANGEHUA）相机 API 客户端。
    ///
    /// 当前实现说明：
    ///   - 连接测试通过 TCP 探测 RTSP 端口完成（自带超时保护，防止网络不可达时永久挂起）
    ///   - RTSP 开关由相机端自动管理，此处仅返回成功
    ///   - 设备状态暂不支持查询，返回零值状态
    /// </summary>
    public class AngehuaCameraApiClient : ICameraApi
    {
        private readonly string _ip;
        private readonly int _rtspPort;
        private readonly TimeSpan _timeout;
        private readonly string _rtspUrlFormat;

        public AngehuaCameraApiClient(string ip, int rtspPort, int timeoutSeconds, string rtspUrlFormat)
        {
            if (string.IsNullOrEmpty(ip))
                throw new ArgumentException("相机IP不能为空", nameof(ip));

            _ip = ip;
            _rtspPort = rtspPort > 0 ? rtspPort : 554;
            _timeout = TimeSpan.FromSeconds(timeoutSeconds > 0 ? timeoutSeconds : 10);
            _rtspUrlFormat = string.IsNullOrEmpty(rtspUrlFormat)
                ? "rtsp://{ip}:{port}/stream{channel}"
                : rtspUrlFormat;
        }

        public async Task<bool> TestConnectionAsync()
        {
            try
            {
                using (var tcpClient = new TcpClient())
                {
                    Task connectTask = tcpClient.ConnectAsync(_ip, _rtspPort);

                    // net472 的 ConnectAsync 不支持取消，采用"竞速+放弃"模式：
                    // 超时后 using 会 Dispose 掉 TcpClient 关闭底层套接字，连接任务被丢弃
                    Task completed = await Task.WhenAny(
                        connectTask,
                        Task.Delay(_timeout)).ConfigureAwait(false);

                    if (completed != connectTask)
                    {
                        // 超时分支：挂接忽略异常的延续，
                        // 防止 Dispose 后孤儿任务的异常成为 UnobservedTaskException
                        _ = connectTask.ContinueWith(
                            t => _ = t.Exception,
                            TaskContinuationOptions.OnlyOnFaulted);
                        return false;
                    }

                    if (!tcpTaskSucceeded(connectTask))
                    {
                        return false;
                    }

                    return tcpClient.Connected;
                }
            }
            catch
            {
                return false;
            }
        }

        public Task<DeviceStatus> GetDeviceStatusAsync()
        {
            var status = new DeviceStatus
            {
                IpAddress = _ip,
                Brand = "ANGEHUA"
            };
            return Task.FromResult(status);
        }

        public Task<bool> SetRtspEnableAsync(int channel)
        {
            return Task.FromResult(true);
        }

        public Task<bool> SetRtspDisableAsync(int channel)
        {
            return Task.FromResult(true);
        }

        public Task<bool> SetRtmpEnableAsync(int channel, string url)
        {
            return Task.FromResult(false);
        }

        public Task<bool> SetRtmpDisableAsync(int channel)
        {
            return Task.FromResult(false);
        }

        public string GetVideoStreamUrl(int channel, string streamType = "RTSP")
        {
            if (streamType != null && streamType.Equals("RTMP", StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            return _rtspUrlFormat
                .Replace("{ip}", _ip)
                .Replace("{port}", _rtspPort.ToString())
                .Replace("{channel}", channel.ToString());
        }

        private static bool tcpTaskSucceeded(Task connectTask)
        {
            try
            {
                connectTask.Wait();
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
