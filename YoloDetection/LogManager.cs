using System;

namespace YoloDetector.YoloDetection
{
    /// <summary>
    /// 日志管理器
    /// 
    /// 功能：统一管理不同类型日志的输出开关
    /// 设计思路：
    /// 1. 将日志按类型分类（YOLO日志、通用日志）
    /// 2. 每种日志都有独立的开关控制
    /// 3. 默认关闭详细日志，避免刷屏
    /// 4. 支持运行时动态切换开关状态
    /// 5. 支持通过配置文件初始化开关
    /// 
    /// 使用方式：
    /// // 初始化
    /// LogManager.Initialize(true, false); // 开启YOLO日志，关闭通用日志
    /// LogManager.SetLogWriter(msg => Console.WriteLine(msg));
    /// 
    /// // 输出日志
    /// LogManager.YoloLog("[YOLO] 检测开始");
    /// LogManager.GeneralLog("[系统] 服务启动");
    /// 
    /// // 运行时切换
    /// LogManager.EnableYoloLog = false; // 关闭YOLO日志
    /// </summary>
    public static class LogManager
    {
        /// <summary>
        /// YOLO日志开关（默认关闭，避免刷屏）
        /// 开启后会输出YOLO检测过程的详细日志，包括：
        /// - 模型初始化信息
        /// - 每帧检测的预处理参数
        /// - 检测结果详细信息
        /// - 过滤过程信息
        /// </summary>
        public static bool EnableYoloLog { get; set; } = false;

        /// <summary>
        /// 通用日志开关（默认开启）
        /// 开启后会输出系统级别的日志，包括：
        /// - 服务启动/停止
        /// - 连接状态
        /// - 错误信息
        /// </summary>
        public static bool EnableGeneralLog { get; set; } = true;

        /// <summary>
        /// 检测结果日志开关（默认关闭，避免每帧输出导致卡顿）
        /// 开启后会输出每帧的检测结果，包括：
        /// - ★检测#101: 1个, cls=0(person) conf=0.91 pos=(xxx,xxx) size=xxx
        /// - ☆帧#100: 0个目标(阈值=0.2)
        /// 说明：这个日志每帧都会输出，会导致画面卡顿，建议关闭
        /// </summary>
        public static bool EnableDetectionResultLog { get; set; } = false;

        /// <summary>
        /// 日志输出委托
        /// 由外部设置，负责将日志输出到具体位置（如控制台、文件、UI）
        /// </summary>
        private static Action<string> _logWriter;

        /// <summary>
        /// 设置日志输出委托
        /// </summary>
        /// <param name="logWriter">日志输出方法，接收日志字符串参数</param>
        public static void SetLogWriter(Action<string> logWriter)
        {
            _logWriter = logWriter;
        }

        /// <summary>
        /// 初始化日志管理器
        /// </summary>
        /// <param name="enableYoloLog">是否开启YOLO日志</param>
        /// <param name="enableGeneralLog">是否开启通用日志</param>
        /// <param name="enableDetectionResultLog">是否开启检测结果日志（每帧输出，会导致卡顿）</param>
        /// <param name="logWriter">日志输出方法（可选）</param>
        public static void Initialize(bool enableYoloLog = false, bool enableGeneralLog = true, bool enableDetectionResultLog = false, Action<string> logWriter = null)
        {
            EnableYoloLog = enableYoloLog;
            EnableGeneralLog = enableGeneralLog;
            EnableDetectionResultLog = enableDetectionResultLog;
            if (logWriter != null)
                _logWriter = logWriter;
        }

        /// <summary>
        /// 输出YOLO日志
        /// </summary>
        /// <param name="message">日志内容</param>
        public static void YoloLog(string message)
        {
            if (EnableYoloLog && _logWriter != null)
            {
                _logWriter(message);
            }
        }

        /// <summary>
        /// 输出通用日志
        /// </summary>
        /// <param name="message">日志内容</param>
        public static void GeneralLog(string message)
        {
            if (EnableGeneralLog && _logWriter != null)
            {
                _logWriter(message);
            }
        }

        /// <summary>
        /// 输出检测结果日志（每帧输出）
        /// 说明：这个方法用于输出每帧的检测结果，如"★检测#101: 1个"
        ///       由于每帧都会调用，会产生大量日志，可能导致UI卡顿
        ///       使用EnableDetectionResultLog开关控制
        /// </summary>
        /// <param name="message">日志内容</param>
        public static void DetectionResultLog(string message)
        {
            if (EnableDetectionResultLog && _logWriter != null)
            {
                _logWriter(message);
            }
        }

        /// <summary>
        /// 输出日志（根据前缀自动判断类型）
        /// 如果消息以[YOLO]开头，则使用YOLO日志开关
        /// 否则使用通用日志开关
        /// </summary>
        /// <param name="message">日志内容</param>
        public static void Log(string message)
        {
            if (message.StartsWith("[YOLO"))
            {
                YoloLog(message);
            }
            else
            {
                GeneralLog(message);
            }
        }

        /// <summary>
        /// 切换所有日志开关
        /// </summary>
        /// <param name="enable">是否开启所有日志</param>
        public static void ToggleAllLogs(bool enable)
        {
            EnableYoloLog = enable;
            EnableGeneralLog = enable;
            EnableDetectionResultLog = enable;
        }

        /// <summary>
        /// 获取当前日志开关状态描述
        /// </summary>
        /// <returns>状态描述字符串</returns>
        public static string GetStatusDescription()
        {
            return $"日志状态: YOLO日志={(EnableYoloLog ? "开启" : "关闭")}, 通用日志={(EnableGeneralLog ? "开启" : "关闭")}, 检测结果日志={(EnableDetectionResultLog ? "开启" : "关闭")}";
        }
    }
}