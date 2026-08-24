using Newtonsoft.Json;

namespace YoloDetector.Configuration
{
    /// <summary>
    /// 相机配置模型（对应 cameraConfigs/{品牌}.json）
    /// 所有属性都有默认值，配置文件缺失或损坏时程序仍可正常运行。
    /// </summary>
    public class CameraConfig
    {
        /// <summary>当前激活的品牌配置文件名（不含扩展名）</summary>
        [JsonProperty("ActiveCameraConfig")]
        public string ActiveCameraConfig { get; set; } = "ANGEHUA";

        public ConnectionConfig Connection { get; set; } = new ConnectionConfig();

        public ApiConfig Api { get; set; } = new ApiConfig();

        public StreamConfig Stream { get; set; } = new StreamConfig();

        public PreviewConfig Preview { get; set; } = new PreviewConfig();

        /// <summary>相机连接参数（IP、超时等）</summary>
        public class ConnectionConfig
        {
            [JsonProperty("_说明")]
            public string Description { get; set; } = "相机连接参数";

            /// <summary>默认相机IP地址（程序启动时自动填入输入框）</summary>
            [JsonProperty("DefaultIp")]
            public string DefaultIp { get; set; } = "192.168.0.15";

            [JsonProperty("Username")]
            public string Username { get; set; } = "admin";

            [JsonProperty("Password")]
            public string Password { get; set; } = "admin";

            /// <summary>HTTP/TCP 连接与请求超时（秒）</summary>
            [JsonProperty("TimeoutSeconds")]
            public int TimeoutSeconds { get; set; } = 10;

            [JsonProperty("UserAgent")]
            public string UserAgent { get; set; } = "YoloDetector/1.0";
        }

        /// <summary>API 接口参数（签名密钥、接口路径）</summary>
        public class ApiConfig
        {
            [JsonProperty("_说明")]
            public string Description { get; set; } = "API接口参数";

            /// <summary>相机品牌标识，决定 CameraApiFactory 创建哪个客户端实现</summary>
            [JsonProperty("CameraBrand")]
            public string CameraBrand { get; set; } = "ANGEHUA";

            /// <summary>API 签名密钥（换品牌时必须修改）</summary>
            [JsonProperty("SignSecret")]
            public string SignSecret { get; set; } = "f6fdffe48c908deb0f4c3bd36c032e72";

            [JsonProperty("OldControlApiPath")]
            public string OldControlApiPath { get; set; } = "/control";

            [JsonProperty("NewControlApiPath")]
            public string NewControlApiPath { get; set; } = "/xsw/control";

            // ---- 设备状态查询接口路径（不同品牌路径不同）----

            [JsonProperty("IpApiPath")]
            public string IpApiPath { get; set; } = "/jsonfile/ip";

            [JsonProperty("CpuApiPath")]
            public string CpuApiPath { get; set; } = "/jsonfile/cpu";

            [JsonProperty("MemApiPath")]
            public string MemApiPath { get; set; } = "/jsonfile/mem";

            [JsonProperty("DiskTotalApiPath")]
            public string DiskTotalApiPath { get; set; } = "/jsonfile/disk_total";

            [JsonProperty("DiskFreeApiPath")]
            public string DiskFreeApiPath { get; set; } = "/jsonfile/disk_free";

            [JsonProperty("TotalCountApiPath")]
            public string TotalCountApiPath { get; set; } = "/jsonfile/totalcount";

            /// <summary>RTMP 带宽查询路径模板，{channel} 会被替换为通道号</summary>
            [JsonProperty("RtmpBandwidthApiPath")]
            public string RtmpBandwidthApiPath { get; set; } = "/jsonfile/rtmpband{channel}";

            /// <summary>RTSP 带宽查询路径模板，{channel} 会被替换为通道号</summary>
            [JsonProperty("RtspBandwidthApiPath")]
            public string RtspBandwidthApiPath { get; set; } = "/jsonfile/rtspband{channel}";
        }

        /// <summary>视频流参数（RTSP端口、地址模板、通道数上限）</summary>
        public class StreamConfig
        {
            [JsonProperty("_说明")]
            public string Description { get; set; } = "视频流参数";

            /// <summary>RTSP 端口号（标准端口 554）</summary>
            [JsonProperty("RtspPort")]
            public int RtspPort { get; set; } = 554;

            /// <summary>
            /// RTSP 地址格式模板。可用占位符：{ip}、{port}、{channel}
            /// </summary>
            [JsonProperty("RtspUrlFormat")]
            public string RtspUrlFormat { get; set; } = "rtsp://{ip}:{port}/stream{channel}";

            /// <summary>最大通道数（界面通道选择控件的上限）</summary>
            [JsonProperty("MaxChannel")]
            public int MaxChannel { get; set; } = 7;

            /// <summary>根据模板生成完整的 RTSP 流地址</summary>
            public string GetRtspUrl(string ip, int channel)
            {
                return RtspUrlFormat
                    .Replace("{ip}", ip)
                    .Replace("{port}", RtspPort.ToString())
                    .Replace("{channel}", channel.ToString());
            }
        }

        /// <summary>预览参数（预览页面路径、状态刷新间隔）</summary>
        public class PreviewConfig
        {
            [JsonProperty("_说明")]
            public string Description { get; set; } = "预览参数";

            /// <summary>相机 Web 预览页面路径</summary>
            [JsonProperty("PreviewPagePath")]
            public string PreviewPagePath { get; set; } = "/draw.html";

            /// <summary>设备状态自动刷新间隔（毫秒）</summary>
            [JsonProperty("StatusRefreshIntervalMs")]
            public int StatusRefreshIntervalMs { get; set; } = 5000;
        }
    }
}
