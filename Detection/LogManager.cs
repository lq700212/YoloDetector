using System;
using YoloDetector.Infrastructure.Logging;

namespace YoloDetector.Detection
{
    /// <summary>
    /// 检测模块统一日志门面（静态）。
    ///
    /// 设计说明：
    ///   - 检测模块内部只通过本类输出日志，不直接依赖 UI 或文件实现
    ///   - 所有日志最终写入 Logger（文件）；UI 可通过 UiSink 注入回调同步显示
    ///   - 各通道开关独立，避免调试日志刷屏影响性能
    /// </summary>
    public static class LogManager
    {
        /// <summary>YOLO 推理过程详细日志开关（默认关闭）</summary>
        public static bool EnableYoloLog { get; set; } = false;

        /// <summary>检测模块通用日志开关（默认开启）</summary>
        public static bool EnableGeneralLog { get; set; } = true;

        /// <summary>每帧检测结果日志开关（会产生大量输出，默认关闭）</summary>
        public static bool EnableDetectionResultLog { get; set; } = false;

        /// <summary>UI 日志回调（可选）。由宿主程序注入，如显示到界面日志面板。</summary>
        private static Action<string> _uiSink;

        /// <summary>
        /// 初始化各日志开关与 UI 回调。
        /// </summary>
        public static void Initialize(bool enableYoloLog, bool enableGeneralLog,
            bool enableDetectionResultLog, Action<string> uiSink = null)
        {
            EnableYoloLog = enableYoloLog;
            EnableGeneralLog = enableGeneralLog;
            EnableDetectionResultLog = enableDetectionResultLog;
            _uiSink = uiSink;
        }

        /// <summary>清除 UI 回调（窗体关闭时调用，防止持有已释放控件引用）</summary>
        public static void ClearUiSink()
        {
            _uiSink = null;
        }

        /// <summary>YOLO 详细日志</summary>
        public static void YoloLog(string message)
        {
            if (EnableYoloLog)
            {
                Write(message);
            }
        }

        /// <summary>通用日志</summary>
        public static void GeneralLog(string message)
        {
            if (EnableGeneralLog)
            {
                Write(message);
            }
        }

        /// <summary>每帧检测结果日志</summary>
        public static void DetectionResultLog(string message)
        {
            if (EnableDetectionResultLog)
            {
                Write(message);
            }
        }

        private static void Write(string message)
        {
            // 文件日志始终落盘；UI 回调仅用于同步显示
            Logger.Write(message);
            var sink = _uiSink;
            if (sink != null)
            {
                sink(message);
            }
        }
    }
}
