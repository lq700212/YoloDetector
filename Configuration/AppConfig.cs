using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;

namespace YoloDetector.Configuration
{
    /// <summary>
    /// 配置加载器（静态组合根）。
    /// 负责加载/保存配置文件，支持多品牌相机配置解耦：
    ///   - appsettings.json 存放全局配置与激活品牌名（ActiveCameraConfig）
    ///   - cameraConfigs/{品牌}.json 存放各品牌独立配置
    ///   - Detection/yoloConfig.json 存放 YOLO 检测配置
    /// 配置文件缺失或损坏时回退到代码默认值，不会导致崩溃。
    ///
    /// 注意：本类只做配置的装载与持久化；业务模块应通过构造函数/方法参数
    /// 接收所需的配置值，而不是直接依赖本类（检测模块即遵循此约定）。
    /// </summary>
    public static class AppConfig
    {
        private const string DefaultBrand = "ANGEHUA";

        private static CameraConfig _current;
        private static YoloConfig _yoloConfig;
        private static EsdConfig _esdConfig;
        private static readonly object _lockObj = new object();

        private static readonly string ConfigFilePath =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");

        private static readonly string BrandConfigsDirectory =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cameraConfigs");

        private static readonly string YoloConfigFilePath =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Detection", "yoloConfig.json");

        private static readonly string EsdConfigFilePath =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Detection", "esdConfig.json");

        static AppConfig()
        {
            if (!IsDesignMode())
            {
                Load();
            }
        }

        /// <summary>当前相机配置（首次访问时自动加载）</summary>
        public static CameraConfig Current
        {
            get
            {
                if (_current == null)
                {
                    lock (_lockObj)
                    {
                        if (_current == null)
                        {
                            if (IsDesignMode())
                            {
                                _current = new CameraConfig();
                            }
                            else
                            {
                                Load();
                            }
                        }
                    }
                }
                return _current;
            }
        }

        /// <summary>YOLO 检测配置（首次访问时自动加载）</summary>
        public static YoloConfig Yolo
        {
            get
            {
                if (_yoloConfig == null)
                {
                    lock (_lockObj)
                    {
                        if (_yoloConfig == null)
                        {
                            if (IsDesignMode())
                            {
                                _yoloConfig = new YoloConfig();
                            }
                            else
                            {
                                LoadYoloConfig();
                            }
                        }
                    }
                }
                return _yoloConfig;
            }
        }

        /// <summary>静电接触(ESD)检测配置（首次访问时自动加载）</summary>
        public static EsdConfig Esd
        {
            get
            {
                if (_esdConfig == null)
                {
                    lock (_lockObj)
                    {
                        if (_esdConfig == null)
                        {
                            if (IsDesignMode())
                            {
                                _esdConfig = new EsdConfig();
                            }
                            else
                            {
                                LoadEsdConfig();
                            }
                        }
                    }
                }
                return _esdConfig;
            }
        }

        /// <summary>加载完整配置（主配置 + 品牌配置 + YOLO 配置 + ESD 配置）</summary>
        public static void Load()
        {
            string activeBrand = DefaultBrand;

            // 步骤1：从主配置读取激活的品牌名
            try
            {
                if (File.Exists(ConfigFilePath))
                {
                    string json = File.ReadAllText(ConfigFilePath);
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

            // 步骤2：加载品牌配置；步骤3：加载YOLO配置；步骤4：加载ESD配置
            LoadBrandConfig(activeBrand);
            LoadYoloConfig();
            LoadEsdConfig();
        }

        /// <summary>加载指定品牌的配置文件（失败时回退到代码默认值）</summary>
        public static void LoadBrandConfig(string brand)
        {
            if (string.IsNullOrEmpty(brand))
            {
                brand = DefaultBrand;
            }

            EnsureBrandConfigsDirectoryExists();

            string brandConfigPath = Path.Combine(BrandConfigsDirectory, brand + ".json");

            try
            {
                if (File.Exists(brandConfigPath))
                {
                    string json = File.ReadAllText(brandConfigPath);
                    var config = JsonConvert.DeserializeObject<CameraConfig>(json);
                    if (config != null)
                    {
                        config.ActiveCameraConfig = brand;
                        lock (_lockObj)
                        {
                            _current = config;
                        }
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

            lock (_lockObj)
            {
                _current = new CameraConfig { ActiveCameraConfig = brand };
            }
            System.Diagnostics.Debug.WriteLine("使用代码默认配置，品牌: " + brand);
        }

        /// <summary>加载 YOLO 配置文件（失败时回退到代码默认值）</summary>
        public static void LoadYoloConfig()
        {
            try
            {
                if (File.Exists(YoloConfigFilePath))
                {
                    string json = File.ReadAllText(YoloConfigFilePath);
                    var config = JsonConvert.DeserializeObject<YoloConfig>(json);
                    if (config != null)
                    {
                        lock (_lockObj)
                        {
                            _yoloConfig = config;
                        }
                        System.Diagnostics.Debug.WriteLine("YOLO配置加载成功");
                        return;
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("YOLO配置文件不存在: " + YoloConfigFilePath);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("YOLO配置文件加载失败: " + ex.Message);
            }

            lock (_lockObj)
            {
                _yoloConfig = new YoloConfig();
            }
            System.Diagnostics.Debug.WriteLine("使用YOLO默认配置");
        }

        /// <summary>
        /// 把拖拽标定出的静电杆 ROI 写回 Detection/esdConfig.json 并同步内存单例。
        ///
        /// 双通道更新：
        ///   1. 内存单例就地夹紧更新——下一次 StartVideoPreview 构造 EsdOptions 时直接用新值；
        ///   2. JSON 文件局部更新（EsdConfig.UpdateRoiJson）——保留 "_说明" 等注释字段，
        ///      文件缺失/损坏时退化为整对象序列化（丢注释但保证功能可用）。
        ///
        /// 写盘失败只记调试日志不抛异常：调用方（UI 标定流程）已先行热更新运行链路，
        /// 配置落盘失败不影响本次预览效果，下次标定可再覆盖。
        /// </summary>
        public static void SaveEsdRoi(float roiX, float roiY, float roiW, float roiH)
        {
            EsdConfig config;
            lock (_lockObj)
            {
                config = _esdConfig;
            }

            if (config == null)
            {
                return; // 静态构造尚未加载过（理论上不可能：属性访问会触发加载），防御跳过
            }

            config.ApplyNormalizedRoi(roiX, roiY, roiW, roiH);

            try
            {
                string updated = null;
                if (File.Exists(EsdConfigFilePath))
                {
                    string original = File.ReadAllText(EsdConfigFilePath);
                    updated = EsdConfig.UpdateRoiJson(
                        original, config.RoiX, config.RoiY, config.RoiW, config.RoiH);
                }

                if (updated == null)
                {
                    updated = JsonConvert.SerializeObject(config, Formatting.Indented);
                    System.Diagnostics.Debug.WriteLine("ESD配置文件缺失或损坏，已用整对象序列化重建");
                }

                // 无 BOM UTF-8（与现有文件编码一致；JSON 解析对 BOM 不敏感，统一无 BOM）
                File.WriteAllText(EsdConfigFilePath, updated, new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("ESD ROI 配置写回失败: " + ex.Message);
            }
        }

        /// <summary>加载静电接触(ESD)配置文件（失败时回退到代码默认值）</summary>
        public static void LoadEsdConfig()
        {
            try
            {
                if (File.Exists(EsdConfigFilePath))
                {
                    string json = File.ReadAllText(EsdConfigFilePath);
                    var config = JsonConvert.DeserializeObject<EsdConfig>(json);
                    if (config != null)
                    {
                        lock (_lockObj)
                        {
                            _esdConfig = config;
                        }
                        System.Diagnostics.Debug.WriteLine("ESD配置加载成功");
                        return;
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("ESD配置文件不存在: " + EsdConfigFilePath);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("ESD配置文件加载失败: " + ex.Message);
            }

            lock (_lockObj)
            {
                _esdConfig = new EsdConfig();
            }
            System.Diagnostics.Debug.WriteLine("使用ESD默认配置");
        }

        private static void EnsureBrandConfigsDirectoryExists()
        {
            if (!Directory.Exists(BrandConfigsDirectory))
            {
                Directory.CreateDirectory(BrandConfigsDirectory);
            }
        }

        private static bool IsDesignMode()
        {
            return System.ComponentModel.LicenseManager.UsageMode ==
                   System.ComponentModel.LicenseUsageMode.Designtime;
        }
    }
}
