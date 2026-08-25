using System.Collections.Generic;
using YoloDetection;

namespace YoloDetector.Tests
{
    // ============================================================
    // 检测模块日志门面测试。
    // 验证三个通道（YOLO详细/通用/每帧结果）开关独立、双 sink 独立送达；
    // 用例结束把全局状态复位为默认，避免影响后续用例与 GUI 冒烟。
    // ============================================================

    internal static class LogManagerTests
    {
        public static void RunAll()
        {
            T.Case("日志-三通道独立开关", ChannelSwitches);
        }

        private static void ChannelSwitches()
        {
            try
            {
                var fileSink = new List<string>();
                var uiSink = new List<string>();

                LogManager.Initialize(
                    enableYoloLog: false,
                    enableGeneralLog: true,
                    enableDetectionResultLog: false,
                    outputSink: msg => { lock (fileSink) fileSink.Add("F:" + msg); },
                    uiSink: msg => { lock (uiSink) uiSink.Add("U:" + msg); });

                LogManager.YoloLog("yolo-line");               // 关 → 不出
                LogManager.GeneralLog("general-line");         // 开 → 出
                LogManager.DetectionResultLog("result-line");  // 关 → 不出

                lock (fileSink)
                {
                    T.Eq(1, fileSink.Count, "关闭通道的日志不应进入落地 sink");
                    T.True(fileSink[0].Contains("general-line"), "开启通道应正常落地");
                }
                lock (uiSink)
                {
                    T.Eq(1, uiSink.Count, "UI sink 与落地 sink 应同步收到同一份");
                    T.True(uiSink[0].Contains("general-line"), "UI sink 内容正确");
                }

                // 全开：三通道都应通过
                LogManager.Initialize(true, true, true,
                    msg => { lock (fileSink) fileSink.Add(msg); }, null);
                LogManager.YoloLog("y2");
                LogManager.GeneralLog("g2");
                LogManager.DetectionResultLog("r2");
                lock (fileSink)
                {
                    T.Eq(4, fileSink.Count, "全开后三条新日志全部落地（1+3=4）");
                }
            }
            finally
            {
                // 复位默认状态：通用开、其余关；sink 清空回 Debug 输出
                LogManager.Initialize(false, true, false, null, null);
                LogManager.ClearUiSink();
            }
        }
    }
}
