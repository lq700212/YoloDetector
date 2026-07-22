using System.Net.Sockets;
using System.Threading.Tasks;

namespace YoloDetector
{
    public class ANGEHUACameraApiClient : ICameraApi
    {
        private string cameraIp;

        public ANGEHUACameraApiClient(string cameraIp)
        {
            this.cameraIp = cameraIp;
        }

        public async Task<bool> TestConnectionAsync(string ip)
        {
            try
            {
                using (TcpClient tcpClient = new TcpClient())
                {
                    int rtspPort = AppConfig.Current.Stream.RtspPort;
                    await tcpClient.ConnectAsync(cameraIp, rtspPort);
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        public Task<DeviceStatus> GetDeviceStatusAsync(string ip)
        {
            DeviceStatus status = new DeviceStatus
            {
                IpAddress = cameraIp,
                Brand = "ANGEHUA",
                CpuUsage = 0,
                MemoryUsage = 0,
                DiskTotal = 0,
                DiskFree = 0,
                TotalVideoCount = 0,
                RtmpBandwidth = 0,
                RtspBandwidth = 0
            };
            return Task.FromResult(status);
        }

        public Task<bool> SetRtspEnableAsync(string ip, int channel)
        {
            return Task.FromResult(true);
        }

        public Task<bool> SetRtspDisableAsync(string ip, int channel)
        {
            return Task.FromResult(true);
        }

        public Task<bool> SetRtmpEnableAsync(string ip, int channel, string url)
        {
            return Task.FromResult(false);
        }

        public Task<bool> SetRtmpDisableAsync(string ip, int channel)
        {
            return Task.FromResult(false);
        }

        public string GetVideoStreamUrl(string ip, int channel, string streamType = "RTSP")
        {
            if (streamType.Equals("RTMP", System.StringComparison.OrdinalIgnoreCase))
            {
                return "";
            }
            else
            {
                return AppConfig.Current.Stream.GetRtspUrl(cameraIp, channel);
            }
        }
    }
}