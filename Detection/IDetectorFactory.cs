using System;
using System.Collections.Generic;

namespace YoloDetector.Detection
{
    /// <summary>
    /// 检测器工厂接口（工厂模式）。
    /// 新增检测器：实现本接口并在 DetectorFactoryRegistry 注册，主程序无需修改。
    /// </summary>
    public interface IDetectorFactory
    {
        /// <summary>检测器类型名称（如 "YOLOV26"），作为注册表的键</summary>
        string DetectorType { get; }

        /// <summary>创建检测器实例。config 可包含 "ConfidenceThreshold"/"NmsThreshold"(float)。</summary>
        IYoloDetector CreateDetector(Dictionary<string, object> config = null);

        /// <summary>判断是否能创建指定类型的检测器</summary>
        bool CanCreate(string detectorType);
    }

    /// <summary>
    /// 检测器工厂注册表（注册表模式）。
    /// 支持运行时动态注册/查找工厂，实现检测器热插拔。线程安全。
    /// </summary>
    public static class DetectorFactoryRegistry
    {
        private static readonly Dictionary<string, IDetectorFactory> Factories =
            new Dictionary<string, IDetectorFactory>(StringComparer.OrdinalIgnoreCase);

        private static readonly object LockObj = new object();

        public static void RegisterFactory(IDetectorFactory factory)
        {
            if (factory == null) throw new ArgumentNullException(nameof(factory));

            lock (LockObj)
            {
                if (!Factories.ContainsKey(factory.DetectorType))
                {
                    Factories[factory.DetectorType] = factory;
                }
            }
        }

        public static void UnregisterFactory(string detectorType)
        {
            lock (LockObj)
            {
                Factories.Remove(detectorType);
            }
        }

        public static IYoloDetector CreateDetector(string detectorType, Dictionary<string, object> config = null)
        {
            lock (LockObj)
            {
                IDetectorFactory factory;
                if (Factories.TryGetValue(detectorType, out factory))
                {
                    return factory.CreateDetector(config);
                }
            }
            throw new InvalidOperationException($"未找到类型为 {detectorType} 的检测器工厂");
        }

        public static bool IsRegistered(string detectorType)
        {
            lock (LockObj)
            {
                return Factories.ContainsKey(detectorType);
            }
        }

        public static IEnumerable<string> GetRegisteredTypes()
        {
            lock (LockObj)
            {
                return new List<string>(Factories.Keys);
            }
        }
    }

    /// <summary>YOLO V26 检测器工厂（默认实现）</summary>
    public class YoloV26DetectorFactory : IDetectorFactory
    {
        public string DetectorType => "YOLOV26";

        public bool CanCreate(string detectorType)
        {
            return string.Equals(detectorType, DetectorType, StringComparison.OrdinalIgnoreCase);
        }

        public IYoloDetector CreateDetector(Dictionary<string, object> config = null)
        {
            var detector = new YoloV26Detector();

            if (config != null)
            {
                object conf;
                if (config.TryGetValue("ConfidenceThreshold", out conf) && conf is float)
                    detector.ConfidenceThreshold = (float)conf;

                object nms;
                if (config.TryGetValue("NmsThreshold", out nms) && nms is float)
                    detector.NmsThreshold = (float)nms;
            }

            return detector;
        }
    }
}
