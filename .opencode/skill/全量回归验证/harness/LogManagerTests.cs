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
            T.Case("日志-UI回调接收与清除", UiSinkDelivery);
            T.Case("日志-回调异常被隔离", SinkExceptionIsolation);
            T.Case("日志-Initialize空通道保持原落地", NullSinkKeepsOutput);
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

        /// <summary>UI 回调能收到日志；ClearUiSink 后不再送达（防关闭后持悬空控件）</summary>
        private static void UiSinkDelivery()
        {
            try
            {
                var ui = new List<string>();
                LogManager.Initialize(false, true, false, msg => { }, msg => { lock (ui) ui.Add(msg); });

                LogManager.GeneralLog("hello-ui");
                lock (ui)
                {
                    T.Eq(1, ui.Count, "UI 回调应收到日志");
                }

                LogManager.ClearUiSink();
                LogManager.GeneralLog("after-clear");
                lock (ui)
                {
                    T.Eq(1, ui.Count, "清除后 UI 不得再收到");
                }
            }
            finally
            {
                LogManager.Initialize(false, true, false, null, null);
                LogManager.ClearUiSink();
            }
        }

        /// <summary>
        /// 回归防线（v2.9.2 隔离修复）：宿主回调抛异常不得上抛——否则检测线程
        /// （catch 块里记日志时抛）或看门狗 Timer 线程会被杀死/终结进程。
        /// </summary>
        private static void SinkExceptionIsolation()
        {
            try
            {
                var ui = new List<string>();
                LogManager.Initialize(false, true, false,
                    outputSink: msg => { throw new System.InvalidOperationException("落地 boom"); },
                    uiSink: msg => { lock (ui) ui.Add(msg); });

                try
                {
                    LogManager.GeneralLog("落地抛异常");
                    T.True(true, "落地通道抛异常应被吞掉");
                }
                catch
                {
                    T.Fail("落地通道异常不得上抛");
                }
                lock (ui)
                {
                    T.Eq(1, ui.Count, "落地异常不得影响 UI 通道送达");
                }

                LogManager.Initialize(false, true, false,
                    outputSink: msg => { },
                    uiSink: msg => { throw new System.InvalidOperationException("UI boom"); });
                try
                {
                    LogManager.GeneralLog("UI抛异常");
                    T.True(true, "UI 通道抛异常应被吞掉");
                }
                catch
                {
                    T.Fail("UI 通道异常不得上抛");
                }
            }
            finally
            {
                LogManager.Initialize(false, true, false, null, null);
                LogManager.ClearUiSink();
            }
        }

        /// <summary>
        /// Initialize 传 null 输出通道 = 保持原通道（只改开关与 UI 回调）；
        /// UI 回调传 null = 清空（语义锁定，防止将来误改）。
        /// </summary>
        private static void NullSinkKeepsOutput()
        {
            try
            {
                var kept = new List<string>();
                LogManager.Initialize(false, true, false, msg => { lock (kept) kept.Add(msg); }, null);

                LogManager.Initialize(false, true, false, null, null); // 空通道调用
                LogManager.GeneralLog("still-here");
                lock (kept)
                {
                    T.Eq(1, kept.Count, "null 输出通道应保持原落地通道");
                }
            }
            finally
            {
                LogManager.Initialize(false, true, false, null, null);
                LogManager.ClearUiSink();
            }
        }
    }
}
