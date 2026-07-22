/*
 * 文件名: IDetectorFactory.cs
 * 作者: Auto Generated
 * 日期: 2026-07-16
 * 版本: 1.0
 * 
 * 功能说明:
 *     这个文件定义了检测器工厂接口和注册机制，实现了YOLO检测器的热插拔功能。
 *     
 *     设计模式：工厂模式 + 注册表模式
 *     - 工厂模式：通过工厂接口创建不同类型的检测器实例
 *     - 注册表模式：运行时动态注册和查找工厂，实现热插拔
 *     
 *     热插拔原理：
 *     1. 不同的检测器实现（如YoloV26、YoloV8）通过对应的工厂注册到注册表
 *     2. 主程序通过检测器类型名称从注册表获取工厂
 *     3. 工厂创建对应的检测器实例
 *     4. 支持运行时动态切换检测器，无需重启程序
 *     
 *     使用场景：
 *     - 切换不同版本的YOLO模型（V26 → V8）
 *     - 使用不同的检测算法（YOLO → EfficientDet）
 *     - A/B测试不同的检测器实现
 *     - 无需修改主程序代码，新增检测器只需添加工厂类并注册
 */

using System;
using System.Collections.Generic;

namespace YoloDetector.YoloDetection
{
    /// <summary>
    /// 检测器工厂接口
    /// 
    /// 这个接口定义了创建检测器的规范，所有检测器工厂都必须实现这个接口。
    /// 工厂模式的核心思想：将对象的创建过程封装起来，调用方只需要知道工厂类型，
    /// 不需要关心具体的创建细节。
    /// 
    /// 实现示例：
    /// public class YoloV8DetectorFactory : IDetectorFactory
    /// {
    ///     public string DetectorType => "YOLOV8";
    ///     
    ///     public IYoloDetector CreateDetector(Dictionary<string, object> config = null)
    ///     {
    ///         return new YoloV8Detector();
    ///     }
    ///     
    ///     public bool CanCreate(string detectorType)
    ///     {
    ///         return detectorType == "YOLOV8";
    ///     }
    /// }
    /// </summary>
    public interface IDetectorFactory
    {
        /// <summary>
        /// 检测器类型名称
        /// 
        /// 这个名称用于在注册表中标识工厂，调用方通过这个名称获取对应的工厂。
        /// 建议使用大写字母，如 "YOLOV26"、"YOLOV8"、"EFFICIENTDET" 等。
        /// </summary>
        string DetectorType { get; }

        /// <summary>
        /// 创建检测器实例
        /// 
        /// 根据配置参数创建对应的检测器实例。
        /// 配置参数可以包含：置信度阈值、NMS阈值、模型路径等。
        /// 
        /// 参数:
        /// config - 可选的配置参数字典，用于初始化检测器
        /// 
        /// 返回:
        /// 创建好的检测器实例，实现了IYoloDetector接口
        /// </summary>
        /// <param name="config">配置参数字典（可选）</param>
        /// <returns>检测器实例</returns>
        IYoloDetector CreateDetector(Dictionary<string, object> config = null);

        /// <summary>
        /// 判断是否能够创建指定类型的检测器
        /// 
        /// 用于在注册多个工厂时进行类型匹配，避免重复注册。
        /// 
        /// 参数:
        /// detectorType - 检测器类型名称
        /// 
        /// 返回:
        /// true 如果当前工厂能够创建该类型的检测器
        /// false 如果当前工厂不能创建该类型的检测器
        /// </summary>
        /// <param name="detectorType">检测器类型名称</param>
        /// <returns>是否能够创建</returns>
        bool CanCreate(string detectorType);
    }

    /// <summary>
    /// 检测器工厂注册表（静态类）
    /// 
    /// 这个类负责管理所有注册的检测器工厂，提供注册、查找和创建检测器的功能。
    /// 
    /// 注册表模式的核心思想：
    /// 1. 将工厂实例存储在一个字典中，键是检测器类型名称
    /// 2. 提供注册方法将工厂添加到字典
    /// 3. 提供创建方法根据类型名称从字典获取工厂并创建检测器
    /// 4. 使用锁保护共享资源，确保线程安全
    /// 
    /// 使用示例：
    /// // 注册工厂
    /// DetectorFactoryRegistry.RegisterFactory(new YoloV26DetectorFactory());
    /// 
    /// // 创建检测器
    /// var detector = DetectorFactoryRegistry.CreateDetector("YOLOV26");
    /// detector.Initialize(modelPath);
    /// </summary>
    public static class DetectorFactoryRegistry
    {
        /// <summary>
        /// 工厂字典，存储所有注册的检测器工厂
        /// 
        /// 键：检测器类型名称（如 "YOLOV26"）
        /// 值：对应的工厂实例（实现了IDetectorFactory接口）
        /// </summary>
        private static readonly Dictionary<string, IDetectorFactory> _factories = new Dictionary<string, IDetectorFactory>();

        /// <summary>
        /// 锁对象，用于保护工厂字典的线程安全访问
        /// 
        /// 在多线程环境下，注册和查找工厂可能同时发生，
        /// 必须使用锁确保字典操作的原子性，避免竞态条件。
        /// </summary>
        private static readonly object _lockObj = new object();

        /// <summary>
        /// 注册检测器工厂
        /// 
        /// 将一个工厂实例添加到注册表中，之后可以通过检测器类型名称获取它。
        /// 如果同一类型的工厂已经注册，新的工厂不会覆盖旧的。
        /// 
        /// 参数:
        /// factory - 要注册的工厂实例，必须实现IDetectorFactory接口
        /// 
        /// 示例:
        /// DetectorFactoryRegistry.RegisterFactory(new YoloV26DetectorFactory());
        /// </summary>
        /// <param name="factory">检测器工厂实例</param>
        public static void RegisterFactory(IDetectorFactory factory)
        {
            lock (_lockObj)
            {
                if (!_factories.ContainsKey(factory.DetectorType))
                {
                    _factories[factory.DetectorType] = factory;
                }
            }
        }

        /// <summary>
        /// 注销检测器工厂
        /// 
        /// 从注册表中移除指定类型的工厂。
        /// 如果该类型的工厂不存在，则什么都不做。
        /// 
        /// 参数:
        /// detectorType - 要注销的检测器类型名称
        /// 
        /// 示例:
        /// DetectorFactoryRegistry.UnregisterFactory("YOLOV26");
        /// </summary>
        /// <param name="detectorType">检测器类型名称</param>
        public static void UnregisterFactory(string detectorType)
        {
            lock (_lockObj)
            {
                _factories.Remove(detectorType);
            }
        }

        /// <summary>
        /// 根据类型名称创建检测器实例
        /// 
        /// 这是注册表最核心的方法，调用方只需要知道检测器类型名称，
        /// 就能获取对应的检测器实例，无需关心具体的创建细节。
        /// 
        /// 参数:
        /// detectorType - 检测器类型名称（如 "YOLOV26"）
        /// config - 可选的配置参数字典，传递给工厂的CreateDetector方法
        /// 
        /// 返回:
        /// 创建好的检测器实例
        /// 
        /// 异常:
        /// InvalidOperationException - 如果未找到指定类型的工厂
        /// 
        /// 示例:
        /// var detector = DetectorFactoryRegistry.CreateDetector("YOLOV26");
        /// detector.Initialize("yolo26n.onnx");
        /// </summary>
        /// <param name="detectorType">检测器类型名称</param>
        /// <param name="config">配置参数（可选）</param>
        /// <returns>检测器实例</returns>
        public static IYoloDetector CreateDetector(string detectorType, Dictionary<string, object> config = null)
        {
            lock (_lockObj)
            {
                if (_factories.TryGetValue(detectorType, out var factory))
                {
                    return factory.CreateDetector(config);
                }
            }
            throw new InvalidOperationException($"未找到类型为 {detectorType} 的检测器工厂");
        }

        /// <summary>
        /// 检查指定类型的检测器工厂是否已注册
        /// 
        /// 参数:
        /// detectorType - 检测器类型名称
        /// 
        /// 返回:
        /// true 如果已注册
        /// false 如果未注册
        /// 
        /// 示例:
        /// if (DetectorFactoryRegistry.IsRegistered("YOLOV26"))
        /// {
        ///     // 已经注册，可以创建检测器
        /// }
        /// </summary>
        /// <param name="detectorType">检测器类型名称</param>
        /// <returns>是否已注册</returns>
        public static bool IsRegistered(string detectorType)
        {
            lock (_lockObj)
            {
                return _factories.ContainsKey(detectorType);
            }
        }

        /// <summary>
        /// 获取所有已注册的检测器类型名称
        /// 
        /// 返回一个包含所有已注册类型名称的列表，用于动态显示可用的检测器。
        /// 
        /// 返回:
        /// 已注册的检测器类型名称列表
        /// 
        /// 示例:
        /// var types = DetectorFactoryRegistry.GetRegisteredTypes();
        /// foreach (var type in types)
        /// {
        ///     Console.WriteLine($"可用检测器: {type}");
        /// }
        /// </summary>
        /// <returns>已注册的检测器类型名称列表</returns>
        public static IEnumerable<string> GetRegisteredTypes()
        {
            lock (_lockObj)
            {
                return new List<string>(_factories.Keys);
            }
        }
    }

    /// <summary>
    /// YOLO V26检测器工厂（默认实现）
    /// 
    /// 这个工厂负责创建YoloV26Detector实例，是当前项目的默认检测器工厂。
    /// 
    /// 使用示例:
    /// var factory = new YoloV26DetectorFactory();
    /// var detector = factory.CreateDetector();
    /// detector.Initialize("yolo26n.onnx");
    /// </summary>
    public class YoloV26DetectorFactory : IDetectorFactory
    {
        /// <summary>
        /// 检测器类型名称，固定为 "YOLOV26"
        /// </summary>
        public string DetectorType => "YOLOV26";

        /// <summary>
        /// 判断是否能够创建指定类型的检测器
        /// 
        /// 参数:
        /// detectorType - 检测器类型名称
        /// 
        /// 返回:
        /// true 如果类型名称是 "YOLOV26"（不区分大小写）
        /// false 其他情况
        /// </summary>
        /// <param name="detectorType">检测器类型名称</param>
        /// <returns>是否能够创建</returns>
        public bool CanCreate(string detectorType)
        {
            return string.Equals(detectorType, DetectorType, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 创建YOLO V26检测器实例
        /// 
        /// 如果提供了配置参数，可以通过配置参数设置检测器的阈值。
        /// 
        /// 参数:
        /// config - 可选的配置参数字典，支持以下键：
        ///          - "ConfidenceThreshold": float 类型，置信度阈值
        ///          - "NmsThreshold": float 类型，NMS阈值
        /// 
        /// 返回:
        /// 创建好的YoloV26Detector实例
        /// 
        /// 示例:
        /// var config = new Dictionary<string, object>
        /// {
        ///     { "ConfidenceThreshold", 0.5f },
        ///     { "NmsThreshold", 0.45f }
        /// };
        /// var detector = factory.CreateDetector(config);
        /// </summary>
        /// <param name="config">配置参数（可选）</param>
        /// <returns>YOLO V26检测器实例</returns>
        public IYoloDetector CreateDetector(Dictionary<string, object> config = null)
        {
            var detector = new YoloV26Detector();
            
            // 如果提供了配置参数，应用到检测器
            if (config != null)
            {
                if (config.TryGetValue("ConfidenceThreshold", out var conf) && conf is float c)
                    detector.ConfidenceThreshold = c;
                if (config.TryGetValue("NmsThreshold", out var nms) && nms is float n)
                    detector.NmsThreshold = n;
            }
            
            return detector;
        }
    }
}