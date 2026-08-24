using System;
using System.IO;
using System.Linq;
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
        private static readonly object _lockObj = new object();

        private static readonly string ConfigFilePath =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");

        private static readonly string BrandConfigsDirectory =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cameraConfigs");

        private static readonly string YoloConfigFilePath =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Detection", "yoloConfig.json");

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

        /// <summary>加载完整配置（主配置 + 品牌配置 + YOLO 配置）</summary>
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

            // 步骤2：加载品牌配置；步骤3：加载YOLO配置
            LoadBrandConfig(activeBrand);
            LoadYoloConfig();
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

        /// <summary>获取所有可用的品牌配置名（扫描 cameraConfigs 目录）</summary>
        public static string[] GetAvailableBrandConfigs()
        {
            EnsureBrandConfigsDirectoryExists();

            try
            {
                string[] jsonFiles = Directory.GetFiles(BrandConfigsDirectory, "*.json");
                return jsonFiles.Select(Path.GetFileNameWithoutExtension).ToArray();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("获取品牌配置列表失败: " + ex.Message);
                return new string[0];
            }
        }

        /// <summary>保存当前配置到主配置文件</summary>
        public static void Save()
        {
            try
            {
                string json = JsonConvert.SerializeObject(_current, Formatting.Indented);
                File.WriteAllText(ConfigFilePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("配置文件保存失败: " + ex.Message);
            }
        }

        /// <summary>将当前配置另存为指定品牌的独立配置文件</summary>
        public static void SaveBrandConfig(string brand)
        {
            if (string.IsNullOrEmpty(brand))
            {
                brand = _current?.Api?.CameraBrand ?? DefaultBrand;
            }

            EnsureBrandConfigsDirectoryExists();
            string brandConfigPath = Path.Combine(BrandConfigsDirectory, brand + ".json");

            try
            {
                var snapshot = Current;
                var brandConfig = new CameraConfig
                {
                    Connection = snapshot.Connection,
                    Api = snapshot.Api,
                    Stream = snapshot.Stream,
                    Preview = snapshot.Preview
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
