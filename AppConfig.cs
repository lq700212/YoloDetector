using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace YoloDetector
{
    // ============================================================
// 相机配置类
// 功能：定义所有可配置的相机参数，与配置文件一一对应
// 
// 结构说明：
//   - ActiveCameraConfig: 当前激活的品牌配置文件名（无扩展名）
//   - Connection:         连接参数（IP、账号密码、超时等）
//   - Api:                API接口参数（签名密钥、接口路径）
//   - Stream:             视频流参数（RTSP端口、地址格式）
//   - Preview:            预览参数（预览页面、刷新间隔）
//
// 多品牌配置解耦设计：
//   - 主配置文件 appsettings.json 存放全局配置和 ActiveCameraConfig 字段
//   - 品牌配置文件存放在 cameraConfigs/{品牌}.json
//   - 切换品牌时只需修改 ActiveCameraConfig 字段，无需改动其他配置
//
// 所有属性都有默认值，即使配置文件缺失或损坏，程序也能正常运行
// ============================================================
public class CameraConfig
{
    // === 当前激活的品牌配置 ===
    // 说明：指定当前使用的品牌配置文件（不含扩展名）
    //       例如 "XSW" 表示使用 cameraConfigs/XSW.json
    //       修改此字段即可切换到不同品牌的配置
    [JsonProperty("ActiveCameraConfig")]
    public string ActiveCameraConfig { get; set; } = "ANGEHUA";

    // === 连接参数 ===
    // 说明：存储相机连接相关的配置
    public ConnectionConfig Connection { get; set; } = new ConnectionConfig();

    // === API参数 ===
    // 说明：存储API接口相关的配置
    public ApiConfig Api { get; set; } = new ApiConfig();

    // === 视频流参数 ===
    // 说明：存储视频流相关的配置
    public StreamConfig Stream { get; set; } = new StreamConfig();

    // === 预览参数 ===
    // 说明：存储视频预览相关的配置
    public PreviewConfig Preview { get; set; } = new PreviewConfig();


        // ============================================================
        // 连接参数子类
        // 包含：相机IP、登录账号密码、HTTP超时时间、User-Agent
        // ============================================================
        public class ConnectionConfig
        {
            // 说明字段（JSON中的中文提示，方便用户理解配置项含义）
            // [JsonProperty("_说明")] 表示JSON文件中的键名是"_说明"
            [JsonProperty("_说明")]
            public string Description { get; set; } = "相机连接参数";

            // 默认相机IP地址（程序启动时自动填入输入框）
            [JsonProperty("DefaultIp")]
            public string DefaultIp { get; set; } = "192.168.0.15";

            // 相机登录用户名
            [JsonProperty("Username")]
            public string Username { get; set; } = "admin";

            // 相机登录密码
            [JsonProperty("Password")]
            public string Password { get; set; } = "admin";

            // HTTP请求超时时间（秒），超时后自动断开
            [JsonProperty("TimeoutSeconds")]
            public int TimeoutSeconds { get; set; } = 10;

            // HTTP请求头中的User-Agent标识
            // 说明：有些服务器会检查这个字段，用于识别客户端
            [JsonProperty("UserAgent")]
            public string UserAgent { get; set; } = "YoloDetector/1.0";
        }


        // ============================================================
        // API参数子类
        // 包含：签名密钥、新旧版控制接口路径、设备状态查询接口路径
        // ============================================================
        public class ApiConfig
        {
            [JsonProperty("_说明")]
            public string Description { get; set; } = "API接口参数";

            // 相机品牌标识（用于选择对应的API实现）
            // 当前支持的品牌：XSW（鑫视威）、HIK（海康威视）、DAHUA（大华）等
            // 添加新品牌时，需要：
            //  1. 在配置中添加对应品牌的API路径
            //  2. 创建对应的ICameraApi实现类
            [JsonProperty("CameraBrand")]
            public string CameraBrand { get; set; } = "ANGEHUA";

            // API签名密钥（换品牌时必须修改此值）
            // 说明：用于生成API请求的签名，确保请求的安全性
            //       不同品牌的相机可能使用不同的密钥
            [JsonProperty("SignSecret")]
            public string SignSecret { get; set; } = "f6fdffe48c908deb0f4c3bd36c032e72";

            // 旧版控制API路径（不带/xsw前缀，使用auth参数认证）
            // 说明：部分旧型号相机使用这个接口
            [JsonProperty("OldControlApiPath")]
            public string OldControlApiPath { get; set; } = "/control";

            // 新版控制API路径（带/xsw前缀，使用token签名认证）
            // 说明：部分新型号相机使用这个接口
            [JsonProperty("NewControlApiPath")]
            public string NewControlApiPath { get; set; } = "/xsw/control";

            // ============================================================
            // 设备状态查询接口路径（不同品牌路径不同）
            // ============================================================
            
            // 获取IP地址的API路径
            [JsonProperty("IpApiPath")]
            public string IpApiPath { get; set; } = "/jsonfile/ip";

            // 获取CPU使用率的API路径
            [JsonProperty("CpuApiPath")]
            public string CpuApiPath { get; set; } = "/jsonfile/cpu";

            // 获取内存使用率的API路径
            [JsonProperty("MemApiPath")]
            public string MemApiPath { get; set; } = "/jsonfile/mem";

            // 获取磁盘总量的API路径
            [JsonProperty("DiskTotalApiPath")]
            public string DiskTotalApiPath { get; set; } = "/jsonfile/disk_total";

            // 获取磁盘可用空间的API路径
            [JsonProperty("DiskFreeApiPath")]
            public string DiskFreeApiPath { get; set; } = "/jsonfile/disk_free";

            // 获取录像总数的API路径
            [JsonProperty("TotalCountApiPath")]
            public string TotalCountApiPath { get; set; } = "/jsonfile/totalcount";

            // 获取RTMP带宽的API路径模板（{channel}会替换为通道号）
            [JsonProperty("RtmpBandwidthApiPath")]
            public string RtmpBandwidthApiPath { get; set; } = "/jsonfile/rtmpband{channel}";

            // 获取RTSP带宽的API路径模板（{channel}会替换为通道号）
            [JsonProperty("RtspBandwidthApiPath")]
            public string RtspBandwidthApiPath { get; set; } = "/jsonfile/rtspband{channel}";
        }


        // ============================================================
        // 视频流参数子类
        // 包含：RTSP端口、流地址格式模板、最大通道数
        // ============================================================
        public class StreamConfig
        {
            [JsonProperty("_说明")]
            public string Description { get; set; } = "视频流参数";

            // RTSP端口号（标准端口554，部分品牌用8554）
            [JsonProperty("RtspPort")]
            public int RtspPort { get; set; } = 554;

            // RTSP流地址格式模板
            // 说明：{ip} 会替换为相机IP
            //       {port} 会替换为RTSP端口号
            //       {channel} 会替换为通道号
            //       例如: "rtsp://192.168.0.15:554/stream0"
            [JsonProperty("RtspUrlFormat")]
            public string RtspUrlFormat { get; set; } = "rtsp://{ip}:{port}/stream{channel}";

            // 最大通道数（通道选择控件的最大值）
            [JsonProperty("MaxChannel")]
            public int MaxChannel { get; set; } = 7;

            // ============================================================
            // 根据模板生成RTSP流地址
            // 参数：ip - 相机IP地址；channel - 通道号
            // 返回：完整的RTSP流地址（如"rtsp://192.168.0.15:554/stream0"）
            // ============================================================
            public string GetRtspUrl(string ip, int channel)
            {
                return RtspUrlFormat
                    .Replace("{ip}", ip)           // 替换IP占位符
                    .Replace("{port}", RtspPort.ToString())  // 替换端口占位符
                    .Replace("{channel}", channel.ToString());  // 替换通道号占位符
            }
        }


        // ============================================================
        // 预览参数子类
        // 包含：Web预览页面路径、状态刷新间隔
        // ============================================================
        public class PreviewConfig
        {
            [JsonProperty("_说明")]
            public string Description { get; set; } = "预览参数";

            // 相机Web预览页面的路径（WebBrowser控件加载此页面）
            // 说明：相机内置的网页，用于显示视频画面
            [JsonProperty("PreviewPagePath")]
            public string PreviewPagePath { get; set; } = "/draw.html";

            // 设备状态自动刷新间隔（毫秒），5000 = 每5秒刷新一次
            // 说明：连接相机后，定时器会每隔这个时间自动获取设备状态
            [JsonProperty("StatusRefreshIntervalMs")]
            public int StatusRefreshIntervalMs { get; set; } = 5000;
        }
    }

    // ============================================================
    // YOLO检测配置类（独立于相机品牌配置）
    // 包含：模型路径、置信度阈值、NMS阈值、启用开关
    // ============================================================
    public class YoloConfig
    {
        [JsonProperty("_说明")]
        public string Description { get; set; } = "YOLO目标检测参数";

        // YOLO模型文件路径（ONNX格式）
        // 说明：相对于程序.exe目录的路径
        [JsonProperty("ModelPath")]
        public string ModelPath { get; set; } = "yolo26n.onnx";

        // 置信度阈值（0-1），低于此值的检测结果会被过滤
        // 说明：值越小，检测到的目标越多，但误检率也越高
        //       值越大，检测结果越准确，但可能漏掉一些目标
        [JsonProperty("ConfidenceThreshold")]
        public float ConfidenceThreshold { get; set; } = 0.5f;

        // NMS（非极大值抑制）阈值（0-1）
        // 说明：用于去除重叠的检测框，值越小，保留的检测框越少
        [JsonProperty("NmsThreshold")]
        public float NmsThreshold { get; set; } = 0.45f;

        // 是否启用YOLO检测
        [JsonProperty("Enabled")]
        public bool Enabled { get; set; } = false;

        // YOLO调试日志开关（默认关闭，避免刷屏）
        // 说明：开启后会输出YOLO检测过程的详细日志，包括预处理参数、检测结果、过滤过程等
        //       用于调试时定位问题，正常使用时建议关闭
        [JsonProperty("YoloDebugLog")]
        public bool YoloDebugLog { get; set; } = false;

        // 检测结果日志开关（默认关闭，避免每帧输出导致卡顿）
        // 说明：开启后会输出每帧的检测结果，如"★检测#101: 1个, cls=0(person) conf=0.91"
        //       由于每帧都会输出，会产生大量日志，可能导致UI卡顿
        //       用于调试时查看检测结果，正常使用时建议关闭
        [JsonProperty("DetectionResultLog")]
        public bool DetectionResultLog { get; set; } = false;

        // 可视化方案类型（YoloBuiltin 或 OpenCV）
        [JsonProperty("VisualizerType")]
        public string VisualizerType { get; set; } = "YoloBuiltin";

        // ===== v2.0 性能优化参数 =====

        /// <summary>
        /// 检测间隔（每N帧触发一次YOLO推理）
        /// 说明：v2.0 优化新增，用于控制YOLO检测频率
        /// - 1 = 每帧都检测（最精确，但可能卡顿）
        /// - 3 = 每3帧检测一次（推荐，平衡性能和精度）
        /// - 5 = 每5帧检测一次（最流畅，检测框更新略慢）
        /// 在25fps下，3表示每秒约8次检测，对人眼来说足够流畅
        /// </summary>
        [JsonProperty("DetectInterval")]
        public int DetectInterval { get; set; } = 3;
    }


    // ============================================================
// 配置管理器（静态类）
// 功能：负责加载和保存配置文件，支持多品牌相机配置解耦
//
// 使用方式：
//   读取配置: AppConfig.Current.Connection.DefaultIp
//   保存配置: AppConfig.Save()
//   切换品牌: AppConfig.LoadBrandConfig("HIK")
//
// 设计说明：
//   - 使用静态类，全局只需一份配置，不需要创建实例
//   - 支持多品牌配置解耦：
//     * 主配置文件 appsettings.json 存放全局配置和激活的品牌
//     * 品牌配置文件存放在 cameraConfigs/{品牌}.json
//     * 切换品牌时只需修改主配置中的 ActiveCameraConfig 字段
//   - 程序启动时自动加载，配置文件不存在时使用代码中的默认值
//   - 配置文件损坏时不会崩溃，回退到默认值
// ============================================================
public static class AppConfig
{
    // 当前配置实例（程序全局共享这一份）
    private static CameraConfig _current;
    
    // YOLO配置实例（全局共享，独立于相机品牌配置）
    private static YoloConfig _yoloConfig;

    // 主配置文件路径（放在exe同目录下，方便用户找到和修改）
    // AppDomain.CurrentDomain.BaseDirectory 就是程序.exe所在的目录
    private static readonly string _configFilePath =
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");

    // 品牌配置目录路径（存放各品牌的独立配置文件）
    // 例如：cameraConfigs/XSW.json、cameraConfigs/HIK.json
    private static readonly string _brandConfigsDirectory =
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cameraConfigs");
    
    // YOLO配置文件路径（独立于相机品牌配置）
    // 例如：YoloDetection/yoloConfig.json
    private static readonly string _yoloConfigFilePath =
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "YoloDetection", "yoloConfig.json");


    // ============================================================
    // 静态构造函数（程序首次访问AppConfig类时自动执行）
    // 作用：加载配置文件
    // 说明：静态构造函数只会执行一次，确保配置只加载一次
    // ============================================================
    static AppConfig()
    {
        if (System.ComponentModel.LicenseManager.UsageMode != System.ComponentModel.LicenseUsageMode.Designtime)
        {
            Load();
        }
    }

    // ============================================================
    // 获取当前配置（只读属性）
    // 用法：AppConfig.Current.Connection.DefaultIp
    // 说明：如果配置为空，会自动重新加载
    // ============================================================
    public static CameraConfig Current
    {
        get
        {
            // 如果配置为空，尝试重新加载（防止意外清空）
            // 设计器模式下也会返回默认配置
            if (_current == null)
            {
                if (System.ComponentModel.LicenseManager.UsageMode != System.ComponentModel.LicenseUsageMode.Designtime)
                {
                    Load();
                }
                else
                {
                    _current = new CameraConfig();
                }
            }
            return _current;
        }
    }
    
    // ============================================================
    // 获取YOLO配置（只读属性）
    // 用法：AppConfig.Yolo.ModelPath
    // 说明：YOLO配置独立于相机品牌配置，全局共享一份
    // ============================================================
    public static YoloConfig Yolo
    {
        get
        {
            // 如果YOLO配置为空，尝试重新加载
            // 设计器模式下也会返回默认配置
            if (_yoloConfig == null)
            {
                if (System.ComponentModel.LicenseManager.UsageMode != System.ComponentModel.LicenseUsageMode.Designtime)
                {
                    LoadYoloConfig();
                }
                else
                {
                    _yoloConfig = new YoloConfig();
                }
            }
            return _yoloConfig;
        }
    }

    // ============================================================
    // 加载完整配置（主配置 + 品牌配置）
    // 逻辑：
    //   1. 先加载主配置文件 appsettings.json，只读取 ActiveCameraConfig 字段
    //   2. 根据 ActiveCameraConfig 字段，加载对应的品牌配置文件
    //   3. 如果品牌配置文件存在，直接使用品牌配置作为完整配置
    //   4. 如果品牌配置文件不存在或解析失败，使用代码中的默认值
    // ============================================================
    public static void Load()
    {
        string activeBrand = "ANGEHUA";

        // 步骤1：加载主配置文件（只读取 ActiveCameraConfig 字段）
        try
        {
            if (File.Exists(_configFilePath))
            {
                string json = File.ReadAllText(_configFilePath);
                var config = JsonConvert.DeserializeObject<CameraConfig>(json);
                if (config != null && !string.IsNullOrEmpty(config.ActiveCameraConfig))
                {
                    activeBrand = config.ActiveCameraConfig;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("主配置文件加载失败: " + ex.Message);
        }

        // 步骤2：加载品牌配置文件（相机品牌特定配置）
        LoadBrandConfig(activeBrand);
        
        // 步骤3：加载YOLO配置文件（独立于相机品牌配置）
        LoadYoloConfig();
    }

    // ============================================================
    // 加载指定品牌的配置文件
    // 参数：brand - 品牌标识（如 "XSW"、"HIK"、"DAHUA"）
    // 逻辑：
    //   1. 检查 cameraConfigs 目录是否存在，不存在则自动创建
    //   2. 构建品牌配置文件路径（cameraConfigs/{品牌}.json）
    //   3. 如果品牌配置文件存在，直接使用品牌配置作为完整配置
    //   4. 如果品牌配置文件不存在，使用代码默认值
    // ============================================================
    public static void LoadBrandConfig(string brand)
    {
        // 如果品牌为空，使用默认品牌
        if (string.IsNullOrEmpty(brand))
        {
            brand = "ANGEHUA";
        }

        // 确保品牌配置目录存在
        EnsureBrandConfigsDirectoryExists();

        // 构建品牌配置文件路径
        string brandConfigPath = Path.Combine(_brandConfigsDirectory, brand + ".json");

        try
        {
            // 检查品牌配置文件是否存在
            if (File.Exists(brandConfigPath))
            {
                // 读取品牌配置文件内容
                string json = File.ReadAllText(brandConfigPath);

                // 反序列化为配置对象（直接替换，不再合并）
                _current = JsonConvert.DeserializeObject<CameraConfig>(json);

                // 如果解析成功，设置当前激活的品牌标识
                if (_current != null)
                {
                    _current.ActiveCameraConfig = brand;
                    System.Diagnostics.Debug.WriteLine("品牌配置加载成功: " + brand);
                    return;
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("品牌配置文件不存在: " + brandConfigPath);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("品牌配置文件加载失败: " + ex.Message);
        }

        // 品牌配置文件不存在或解析失败，使用代码默认值
        _current = new CameraConfig();
        _current.ActiveCameraConfig = brand;
        System.Diagnostics.Debug.WriteLine("使用代码默认配置，品牌: " + brand);
    }

    // ============================================================
    // 加载YOLO配置文件（独立于相机品牌配置）
    // 逻辑：
    //   1. 读取 yoloConfig.json 文件
    //   2. 如果文件存在，解析为YoloConfig对象
    //   3. 如果文件不存在或解析失败，使用代码默认值
    // ============================================================
    public static void LoadYoloConfig()
    {
        try
        {
            // 检查YOLO配置文件是否存在
            if (File.Exists(_yoloConfigFilePath))
            {
                // 读取YOLO配置文件内容
                string json = File.ReadAllText(_yoloConfigFilePath);
                
                // 反序列化为YoloConfig对象
                _yoloConfig = JsonConvert.DeserializeObject<YoloConfig>(json);
                
                // 如果解析成功
                if (_yoloConfig != null)
                {
                    System.Diagnostics.Debug.WriteLine("YOLO配置加载成功");
                    return;
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("YOLO配置文件不存在: " + _yoloConfigFilePath);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("YOLO配置文件加载失败: " + ex.Message);
        }
        
        // YOLO配置文件不存在或解析失败，使用代码默认值
        _yoloConfig = new YoloConfig();
        System.Diagnostics.Debug.WriteLine("使用YOLO默认配置");
    }

    // ============================================================
    // 确保品牌配置目录存在
    // 逻辑：如果 cameraConfigs 目录不存在，自动创建
    // ============================================================
    private static void EnsureBrandConfigsDirectoryExists()
    {
        if (!Directory.Exists(_brandConfigsDirectory))
        {
            Directory.CreateDirectory(_brandConfigsDirectory);
            System.Diagnostics.Debug.WriteLine("创建品牌配置目录: " + _brandConfigsDirectory);
        }
    }

    // ============================================================
    // 获取所有可用的品牌配置文件列表
    // 返回：品牌标识数组（如 ["XSW", "HIK", "DAHUA"]）
    // 逻辑：扫描 cameraConfigs 目录，获取所有 .json 文件的文件名（不含扩展名）
    // ============================================================
    public static string[] GetAvailableBrandConfigs()
    {
        EnsureBrandConfigsDirectoryExists();

        try
        {
            // 获取目录中所有 .json 文件
            string[] jsonFiles = Directory.GetFiles(_brandConfigsDirectory, "*.json");

            // 提取文件名（不含扩展名）作为品牌标识
            return jsonFiles.Select(path => Path.GetFileNameWithoutExtension(path)).ToArray();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("获取品牌配置列表失败: " + ex.Message);
            return new string[0];
        }
    }

    // ============================================================
    // 保存配置到文件
    // 用途：用户在界面上修改了配置后，可以调用此方法保存
    // 说明：只保存主配置文件，品牌配置文件需要单独保存
    // ============================================================
    public static void Save()
    {
        try
        {
            // 将配置对象序列化为格式化的JSON字符串（缩进2空格，方便阅读）
            string json = JsonConvert.SerializeObject(_current, Formatting.Indented);

            // 写入主配置文件
            File.WriteAllText(_configFilePath, json);
        }
        catch (Exception ex)
        {
            // 如果保存失败，输出错误信息到调试日志
            System.Diagnostics.Debug.WriteLine("配置文件保存失败: " + ex.Message);
        }
    }

    // ============================================================
    // 保存品牌配置到独立文件
    // 参数：brand - 品牌标识
    // 用途：将当前配置保存为指定品牌的配置文件
    // 说明：方便用户备份和管理不同品牌的配置
    // ============================================================
    public static void SaveBrandConfig(string brand)
    {
        if (string.IsNullOrEmpty(brand))
        {
            brand = _current?.Api?.CameraBrand ?? "ANGEHUA";
        }

        EnsureBrandConfigsDirectoryExists();

        string brandConfigPath = Path.Combine(_brandConfigsDirectory, brand + ".json");

        try
        {
            CameraConfig brandConfig = new CameraConfig
            {
                Connection = _current.Connection,
                Api = _current.Api,
                Stream = _current.Stream,
                Preview = _current.Preview
            };

            string json = JsonConvert.SerializeObject(brandConfig, Formatting.Indented);
            File.WriteAllText(brandConfigPath, json);

            System.Diagnostics.Debug.WriteLine("品牌配置保存成功: " + brandConfigPath);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("品牌配置保存失败: " + ex.Message);
        }
    }
}
}