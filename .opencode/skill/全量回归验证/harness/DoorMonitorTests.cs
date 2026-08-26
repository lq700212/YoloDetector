using System;
using System.Collections.Generic;
using System.IO;
using OpenCvSharp;
using YoloDetection;

namespace YoloDetector.Tests
{
    // ============================================================
    // 门状态监测测试（关门基准比对）。
    //
    // 核心验证点：
    //   - 亮度归一化：整体明暗漂移（昼夜/开关灯）不得误报为"门开"
    //   - 结构变化（门开了露出新内容）必须触发翻转 + 事件
    //   - 人遮挡排除：门 ROI 被人体框盖住时跳过判定
    //   - 防抖：候选状态持续不足 StateHoldMs 不得翻转
    //   - 基准持久化：落盘 PNG + 重启（新实例）加载
    //
    // 全部用合成帧（纯灰背景 + 矩形块），虚拟时钟注入，无时序脆弱性。
    // 基准路径用 ZZTEST_ 前缀临时文件，用完即删，不污染现场 door_baseline.png。
    // ============================================================

    internal static class DoorMonitorTests
    {
        // 统一测试参数：ROI=(0.1,0.1,0.5,0.5)，帧 200x200 → ROI 像素 (20,20,100,100)；
        // 差异阈值 18；防抖 1000ms；基准路径临时文件
        private static DoorMonitorOptions MakeOptions()
        {
            return new DoorMonitorOptions
            {
                RoiX = 0.1f, RoiY = 0.1f, RoiW = 0.5f, RoiH = 0.5f,
                DiffThreshold = 18f,
                StateHoldMs = 1000,
                BaselinePath = "Detection/ZZTEST_door_baseline.png"
            };
        }

        /// <summary>造一帧 200x200 纯灰背景；withDoorOpenContent=true 时在 ROI 内画白/黑块模拟门开露出新内容</summary>
        private static Mat MakeFrame(byte gray, bool withDoorOpenContent)
        {
            var frame = new Mat(200, 200, MatType.CV_8UC3, new Scalar(gray, gray, gray));
            if (withDoorOpenContent)
            {
                Cv2.Rectangle(frame, new Rect(30, 30, 80, 80), new Scalar(240, 240, 240), -1);
                Cv2.Rectangle(frame, new Rect(50, 50, 40, 40), new Scalar(20, 20, 20), -1);
            }
            return frame;
        }

        /// <summary>造一个人体框（中心点+尺寸，盖住 ROI 用）</summary>
        private static DetectionResult Person(float cx, float cy, float w, float h)
        {
            return FakeDetector.Box(cx, cy, w, h, 0.9f);
        }

        /// <summary>基准临时文件的绝对路径（用例 finally 清理用）</summary>
        private static string BaselinePath()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                MakeOptions().BaselinePath.Replace('/', '\\'));
        }

        public static void RunAll()
        {
            T.Case("门检-基准采集与持久化重载", BaselineCaptureAndReload);
            T.Case("门检-画面一致与整体亮度漂移都不误报", NoFalseAlarmOnLighting);
            T.Case("门检-结构变化防抖后翻转并触发事件", DoorOpenFlipWithDebounce);
            T.Case("门检-人遮挡时跳过判定保持原状态", PersonOcclusionSkips);
            T.Case("门检-短暂变化不足防抖不翻转", ShortGlitchNoFlip);
            T.Case("门检-DoorConfig默认值与ToOptions夹紧", DoorConfigDefaults);
        }

        // ---------- 用例实现 ----------

        /// <summary>基准采集 → 落盘 → 新实例（模拟重启）自动加载</summary>
        private static void BaselineCaptureAndReload()
        {
            string path = BaselinePath();
            try
            {
                var analyzer = new DoorMonitorAnalyzer(MakeOptions());
                T.False(analyzer.HasBaseline, "初始应无基准");

                using (var frame = MakeFrame(100, false))
                {
                    analyzer.SetBaselineFromFrame(frame);
                }
                T.True(analyzer.HasBaseline, "采集后应有基准");
                T.True(File.Exists(path), "基准图应落盘: " + path);

                // 新实例模拟重启：自动从文件加载
                var reloaded = new DoorMonitorAnalyzer(MakeOptions());
                T.True(reloaded.HasBaseline, "重启后应自动加载基准");
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        /// <summary>
        /// 两图一致 → 关；整体亮度漂移（全帧变亮/变暗）→ 亮度归一化后仍判关（核心抗光照断言）
        /// </summary>
        private static void NoFalseAlarmOnLighting()
        {
            string path = BaselinePath();
            try
            {
                var analyzer = new DoorMonitorAnalyzer(MakeOptions());
                using (var baseFrame = MakeFrame(100, false))
                {
                    analyzer.SetBaselineFromFrame(baseFrame);
                }

                using (var same = MakeFrame(100, false))
                {
                    var s = analyzer.Update(same, new List<DetectionResult>(), 0);
                    T.False(s.IsOpen, "画面一致应判门关");
                    T.True(s.HasBaseline, "应有基准");
                }

                // 整体变亮（+80）：归一化消除亮度平移后结构差异约为 0
                using (var brighter = MakeFrame(180, false))
                {
                    var s = analyzer.Update(brighter, new List<DetectionResult>(), 500);
                    T.False(s.IsOpen, "整体亮度漂移不得误报门开(归一化)");
                    T.True(s.DiffValue < 18f, "归一化后差异应低于阈值, 实际=" + s.DiffValue);
                }

                // 整体变暗（-60）：同上
                using (var darker = MakeFrame(40, false))
                {
                    var s = analyzer.Update(darker, new List<DetectionResult>(), 1000);
                    T.False(s.IsOpen, "整体变暗不得误报门开");
                }
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        /// <summary>
        /// ROI 内出现结构变化（门外内容）→ 候选"开"持续 StateHoldMs → 翻转 + 事件 true；
        /// 恢复关门画面 → 防抖后翻回 + 事件 false
        /// </summary>
        private static void DoorOpenFlipWithDebounce()
        {
            string path = BaselinePath();
            try
            {
                var analyzer = new DoorMonitorAnalyzer(MakeOptions());
                var events = new List<bool>();
                analyzer.StateChanged += isOpen => events.Add(isOpen);

                using (var baseFrame = MakeFrame(100, false))
                {
                    analyzer.SetBaselineFromFrame(baseFrame);
                }

                using (var openFrame = MakeFrame(100, true))
                {
                    // 防抖期内（<1000ms）：状态未翻转
                    var s1 = analyzer.Update(openFrame, new List<DetectionResult>(), 0);
                    T.False(s1.IsOpen, "防抖期内不得翻转");
                    var s2 = analyzer.Update(openFrame, new List<DetectionResult>(), 800);
                    T.False(s2.IsOpen, "防抖期内(800ms)不得翻转");
                    T.True(s2.DiffValue > 18f, "结构变化差异应超阈值, 实际=" + s2.DiffValue);

                    // 达到 StateHoldMs：翻转为开 + 事件
                    var s3 = analyzer.Update(openFrame, new List<DetectionResult>(), 1200);
                    T.True(s3.IsOpen, "防抖期满应翻转为门开");
                    T.True(events.Count == 1 && events[0], "应触发一次开门事件");

                    // 恢复关门画面：防抖后翻回 + 事件
                    using (var closed = MakeFrame(100, false))
                    {
                        analyzer.Update(closed, new List<DetectionResult>(), 1500);
                        var s5 = analyzer.Update(closed, new List<DetectionResult>(), 3000);
                        T.False(s5.IsOpen, "恢复关门画面应翻回门关");
                        T.True(events.Count == 2 && !events[1], "应触发一次关门事件");
                    }
                }
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        /// <summary>门 ROI 被人体框盖住：跳过判定（PersonOccluded=true），且遮挡期间防抖计时不得累积</summary>
        private static void PersonOcclusionSkips()
        {
            string path = BaselinePath();
            try
            {
                var analyzer = new DoorMonitorAnalyzer(MakeOptions());
                using (var baseFrame = MakeFrame(100, false))
                {
                    analyzer.SetBaselineFromFrame(baseFrame);
                }

                // 人体框盖住 ROI (20,20,100,100)：中心 (70,70) 尺寸 140x140
                var persons = new List<DetectionResult> { Person(70, 70, 140, 140) };

                using (var openFrame = MakeFrame(100, true))
                {
                    // 人站在门前 + 门开画面：本帧跳过判定，状态保持关
                    var s1 = analyzer.Update(openFrame, persons, 0);
                    T.True(s1.PersonOccluded, "被人体遮挡应标记 PersonOccluded");
                    T.False(s1.IsOpen, "遮挡帧不得翻转状态");

                    // 遮挡期间防抖计时不得累积：遮挡 800ms 后人离开，需重新计满才翻转
                    var s2 = analyzer.Update(openFrame, persons, 800);
                    T.True(s2.PersonOccluded, "持续遮挡仍应标记");

                    var noPerson = new List<DetectionResult>();
                    var s3 = analyzer.Update(openFrame, noPerson, 900);
                    T.False(s3.PersonOccluded, "人离开后恢复判定");
                    T.False(s3.IsOpen, "人刚离开(防抖重新计时)不得立即翻转");
                    var s4 = analyzer.Update(openFrame, noPerson, 2000);
                    T.True(s4.IsOpen, "人离开后防抖计满应翻转为门开");
                }
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        /// <summary>门开画面短暂出现（不足 StateHoldMs）后恢复 → 不得翻转（防单帧误判/快速晃过）</summary>
        private static void ShortGlitchNoFlip()
        {
            string path = BaselinePath();
            try
            {
                var analyzer = new DoorMonitorAnalyzer(MakeOptions());
                var events = new List<bool>();
                analyzer.StateChanged += isOpen => events.Add(isOpen);

                using (var baseFrame = MakeFrame(100, false))
                {
                    analyzer.SetBaselineFromFrame(baseFrame);
                }

                using (var openFrame = MakeFrame(100, true))
                {
                    analyzer.Update(openFrame, new List<DetectionResult>(), 0);
                }
                using (var closed = MakeFrame(100, false))
                {
                    analyzer.Update(closed, new List<DetectionResult>(), 500);
                    analyzer.Update(closed, new List<DetectionResult>(), 2000);
                }

                T.True(events.Count == 0, "短暂变化不得触发任何状态事件, 实际=" + events.Count);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        /// <summary>DoorConfig 默认值合法、ToOptions 夹紧、UpdateRoiJson 与 EsdConfig 同一公共实现</summary>
        private static void DoorConfigDefaults()
        {
            // 默认值合法性（与 doorConfig.json 模板一致）
            var cfg = new YoloDetector.Configuration.DoorConfig();
            T.True(cfg.Enabled, "门检测默认应启用");
            T.True(cfg.DiffThreshold > 0, "差异阈值应为正");
            T.True(cfg.StateHoldMs >= 0, "防抖时长应非负");
            T.False(string.IsNullOrEmpty(cfg.BaselinePath), "基准路径应非空");

            // ToOptions 夹紧：ROI 出界收拢、阈值下限保护
            cfg.RoiX = 1.5f; cfg.RoiY = -0.5f; cfg.RoiW = 0f; cfg.RoiH = 2f; cfg.DiffThreshold = -5f;
            var opts = cfg.ToOptions();
            T.Eq(1f, opts.RoiX, "RoiX>1 夹紧到 1");
            T.Eq(0f, opts.RoiY, "RoiY<0 夹紧到 0");
            T.Eq(0.01f, opts.RoiW, "RoiW=0 夹紧到最小 0.01");
            T.Eq(1f, opts.RoiH, "RoiH>1 夹紧到 1");
            T.Eq(1f, opts.DiffThreshold, "负差异阈值夹紧到 1");

            // UpdateRoiJson：与 ESD 共用 RoiJsonUpdater——局部更新保注释 + 坏 JSON 返 null
            string original = "{\n  \"_说明\": \"测试注释\",\n  \"RoiX\": 0.5,\n  \"Enabled\": true\n}";
            string updated = YoloDetector.Configuration.DoorConfig.UpdateRoiJson(original, 0.1f, 0.2f, 0.3f, 0.4f);
            T.False(string.IsNullOrEmpty(updated), "合法 JSON 应更新成功");
            T.True(updated.Contains("_说明"), "注释字段应保留");
            T.True(updated.Contains("0.1"), "RoiX 应已更新");

            T.True(YoloDetector.Configuration.DoorConfig.UpdateRoiJson("{不是JSON", 0f, 0f, 0f, 0f) == null,
                "坏 JSON 应返回 null");
        }
    }
}
