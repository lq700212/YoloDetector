using System;
using System.IO;
using YoloDetector.Configuration;

namespace YoloDetector.Tests
{
    // ============================================================
    // 配置层测试。
    //
    // 注意：AppConfig 是静态组合根，LoadBrandConfig 会改写静态 _current。
    // 本分区跑在最前面，且每个污染静态状态的用例都在 finally 里
    // 通过 AppConfig.Load() 恢复真实配置，保证后续用例环境干净。
    //
    // 铁律约束：绝不改写现场的 appsettings.json / cameraConfigs\ANGEHUA.json；
    // 损坏文件场景用一次性临时品牌文件（ZZTEST_ 前缀）注入，测完即删。
    // ============================================================

    internal static class ConfigTests
    {
        private static readonly string BrandDir = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "cameraConfigs");

        public static void RunAll()
        {
            T.Case("配置-当前配置加载成功且字段合理", CurrentConfigSane);
            T.Case("配置-RtspUrl模板占位符替换", RtspUrlTemplate);
            T.Case("配置-加载不存在品牌回退默认值", BrandFallbackOnMissing);
            T.Case("配置-加载损坏JSON回退默认值", BrandFallbackOnBrokenJson);
            T.Case("配置-EsdConfig现场加载且模型存在", EsdConfigSane);
            T.Case("配置-EsdConfig.ToOptions非法值夹紧", EsdToOptionsClamps);
            T.Case("配置-EsdConfig.UpdateRoiJson局部更新保留注释", EsdUpdateRoiJsonPreservesComments);
            T.Case("配置-EsdConfig.UpdateRoiJson缺失字段补建与坏JSON兜底", EsdUpdateRoiJsonEdgeCases);
            T.Case("配置-EsdConfig.ApplyNormalizedRoi就地夹紧", EsdApplyNormalizedRoiClamps);

            T.Case("配置-EsdConfig.DrawNoContactBoxes缺省false且可配置", EsdDrawNoContactBoxesOption);
        }

        /// <summary>验证真实现场配置能正常加载且取值在合法范围</summary>
        private static void CurrentConfigSane()
        {
            var cam = AppConfig.Current;
            T.True(cam != null, "Current 不应为 null");
            T.False(string.IsNullOrEmpty(cam.ActiveCameraConfig), "ActiveCameraConfig 应非空");
            T.True(cam.Connection.TimeoutSeconds > 0, "TimeoutSeconds 应为正数");
            T.True(cam.Stream.RtspPort > 0 && cam.Stream.RtspPort < 65536, "RtspPort 应在合法端口范围");

            var yolo = AppConfig.Yolo;
            T.True(yolo != null, "Yolo 配置不应为 null");
            T.True(yolo.ConfidenceThreshold > 0f && yolo.ConfidenceThreshold < 1f,
                "置信度阈值应在(0,1)，实际=" + yolo.ConfidenceThreshold);
            T.True(yolo.NmsThreshold > 0f && yolo.NmsThreshold < 1f,
                "NMS 阈值应在(0,1)，实际=" + yolo.NmsThreshold);
            T.False(string.IsNullOrEmpty(yolo.ModelPath), "ModelPath 应非空");
            T.Info("现场配置: 品牌=" + cam.ActiveCameraConfig +
                   " 阈值=" + yolo.ConfidenceThreshold +
                   " 模型=" + yolo.ModelPath);

            // 配置里指向的模型必须真实存在（否则启动预览必失败）
            string modelPath = TestUtil.BinPath(yolo.ModelPath.Replace('/', '\\'));
            T.True(File.Exists(modelPath), "配置的模型文件应存在: " + modelPath);
        }

        /// <summary>StreamConfig.GetRtspUrl 的三个占位符都要被替换</summary>
        private static void RtspUrlTemplate()
        {
            var stream = new CameraConfig.StreamConfig
            {
                RtspPort = 8554,
                RtspUrlFormat = "rtsp://{ip}:{port}/stream{channel}"
            };
            string url = stream.GetRtspUrl("10.1.2.3", 2);
            T.Eq("rtsp://10.1.2.3:8554/stream2", url, "模板替换结果");

            // 自定义模板：缺占位符时应保持原样不抛异常
            var weird = new CameraConfig.StreamConfig { RtspUrlFormat = "rtsp://fixed" };
            T.Eq("rtsp://fixed", weird.GetRtspUrl("1.1.1.1", 0), "无占位符模板原样返回");
        }

        /// <summary>请求不存在的品牌配置 → 回退代码默认值，不抛异常</summary>
        private static void BrandFallbackOnMissing()
        {
            try
            {
                AppConfig.LoadBrandConfig("ZZTEST_NO_SUCH_BRAND");

                var cfg = AppConfig.Current;
                T.Eq("ZZTEST_NO_SUCH_BRAND", cfg.ActiveCameraConfig, "回退后品牌名保留请求值");
                T.Eq("192.168.0.15", cfg.Connection.DefaultIp, "回退到代码默认 DefaultIp");
                T.Eq(554, cfg.Stream.RtspPort, "回退到代码默认端口");
            }
            finally
            {
                AppConfig.Load(); // 恢复真实现场配置
            }
        }

        /// <summary>品牌配置文件内容是坏 JSON → 回退代码默认值，不崩溃</summary>
        private static void BrandFallbackOnBrokenJson()
        {
            string brokenPath = Path.Combine(BrandDir, "ZZTEST_BROKEN.json");
            try
            {
                File.WriteAllText(brokenPath, "{ 这不是合法JSON !!!");
                AppConfig.LoadBrandConfig("ZZTEST_BROKEN");

                var cfg = AppConfig.Current;
                T.Eq("192.168.0.15", cfg.Connection.DefaultIp, "坏 JSON 应回退到默认 DefaultIp");
                T.True(cfg.Stream != null, "回退后 Stream 子配置应可用");
            }
            finally
            {
                if (File.Exists(brokenPath))
                {
                    File.Delete(brokenPath); // 清理测试痕迹，不留垃圾在现场目录
                }
                AppConfig.Load();
            }
        }

        /// <summary>
        /// 现场静电触摸配置能正常加载，且指向的姿态模型真实存在
        /// （模型缺失时控制器会降级为纯人员检测——但现场配置错误应当在此暴露）。
        /// 只读访问 AppConfig.Esd 不改静态状态，无需恢复。
        /// </summary>
        private static void EsdConfigSane()
        {
            var esd = AppConfig.Esd;
            T.True(esd != null, "Esd 配置不应为 null（懒加载兜底）");

            // 模型路径非空且文件存在（与 yolo.ModelPath 同一检查标准）
            T.False(string.IsNullOrEmpty(esd.PoseModelPath), "PoseModelPath 应非空");
            string poseModel = TestUtil.BinPath(esd.PoseModelPath.Replace('/', '\\'));
            T.True(File.Exists(poseModel), "姿态模型文件应存在: " + poseModel);

            // 时序参数必须为正（0 或负会让状态机行为异常：Hold=0 一碰就算、Grace=0 无防抖）
            T.True(esd.HoldDurationMs > 0, "HoldDurationMs 应为正数，实际=" + esd.HoldDurationMs);
            T.True(esd.ReleaseGraceMs >= 0, "ReleaseGraceMs 应非负");

            // ROI 归一化坐标在 [0,1]（现场手改 JSON 最容易出界）
            T.True(esd.RoiX >= 0f && esd.RoiX <= 1f, "RoiX 应在[0,1]，实际=" + esd.RoiX);
            T.True(esd.RoiY >= 0f && esd.RoiY <= 1f, "RoiY 应在[0,1]，实际=" + esd.RoiY);

            T.Info("现场ESD配置: Enabled=" + esd.Enabled +
                   " ROI=(" + esd.RoiX + "," + esd.RoiY + "," + esd.RoiW + "," + esd.RoiH + ")" +
                   " Hold=" + esd.HoldDurationMs + "ms");
        }

        /// <summary>ToOptions 对非法值就地夹紧到安全范围（防手改 JSON 让检测逻辑跑飞）</summary>
        private static void EsdToOptionsClamps()
        {
            var bad = new EsdConfig
            {
                RoiX = 1.5f,          // >1 → 夹到 1
                RoiY = -0.5f,         // <0 → 夹到 0
                RoiW = 0f,            // <0.01 → 夹到 0.01
                RoiH = 2f,            // >1 → 夹到 1
                MarginPx = -10f,      // 负容差 → 夹到 0
                HoldDurationMs = -500,
                ReleaseGraceMs = -100,
                WristConfidenceThreshold = 2f // >0.95 → 夹到 0.95
            };

            var opts = bad.ToOptions();

            T.Eq(1f, opts.RoiX, "RoiX>1 夹紧到 1");
            T.Eq(0f, opts.RoiY, "RoiY<0 夹紧到 0");
            T.Eq(0.01f, opts.RoiW, "RoiW=0 夹紧到最小 0.01");
            T.Eq(1f, opts.RoiH, "RoiH>1 夹紧到 1");
            T.Eq(0f, opts.MarginPx, "负 MarginPx 夹紧到 0");
            T.Eq(0.0, opts.HoldDurationMs, "负 Hold 夹紧到 0");
            T.Eq(0.0, opts.ReleaseGraceMs, "负 Grace 夹紧到 0");
            T.Eq(0.95f, opts.WristConfidenceThreshold, "置信度>0.95 夹紧到 0.95");

            // 合法值应原样通过（夹紧逻辑不得误伤正常配置）
            var good = new EsdConfig { RoiX = 0.4f, RoiY = 0.25f, RoiW = 0.2f, RoiH = 0.35f };
            var gopts = good.ToOptions();
            T.Eq(0.4f, gopts.RoiX, "合法 RoiX 原样保留");
            T.Eq(0.35f, gopts.RoiH, "合法 RoiH 原样保留");
        }

        /// <summary>
        /// UI 拖拽标定写回的核心契约：JObject 局部更新只动四个 ROI 字段，
        /// "_说明"/"_现场标定" 等模型外的中文注释字段与其他参数原样保留
        /// （整体序列化会抹掉它们，现场调参指南就丢了——这是本方法的存亡线）。
        /// </summary>
        private static void EsdUpdateRoiJsonPreservesComments()
        {
            const string original = @"{
  ""_说明"": ""静电杆触摸检测配置"",
  ""_现场标定"": ""启动预览后拖拽标定"",
  ""Enabled"": true,
  ""PoseModelPath"": ""Detection/model/yolo11n-pose.onnx"",
  ""RoiX"": 0.40,
  ""RoiY"": 0.25,
  ""RoiW"": 0.20,
  ""RoiH"": 0.35,
  ""HoldDurationMs"": 1500
}";

            string updated = EsdConfig.UpdateRoiJson(original, 0.1f, 0.2f, 0.3f, 0.4f);
            T.True(updated != null, "合法输入应返回更新后的文本");

            // 注释字段必须原样保留
            T.True(updated.Contains("\"_说明\""), "_说明 注释字段应保留");
            T.True(updated.Contains("\"_现场标定\""), "_现场标定 注释字段应保留");
            T.True(updated.Contains("静电杆触摸检测配置"), "注释内容应保留");

            var parsed = Newtonsoft.Json.Linq.JObject.Parse(updated);

            // 四个 ROI 字段已更新（JSON 文本读回带 double 尾差，必须用容差断言）
            T.True(Math.Abs((float)parsed["RoiX"] - 0.1f) < 0.0001f, "RoiX 应更新为 0.1，实际=" + parsed["RoiX"]);
            T.True(Math.Abs((float)parsed["RoiY"] - 0.2f) < 0.0001f, "RoiY 应更新为 0.2");
            T.True(Math.Abs((float)parsed["RoiW"] - 0.3f) < 0.0001f, "RoiW 应更新为 0.3");
            T.True(Math.Abs((float)parsed["RoiH"] - 0.4f) < 0.0001f, "RoiH 应更新为 0.4");

            // 其他参数不得被波及
            T.Eq(true, (bool)parsed["Enabled"], "Enabled 不应被改动");
            T.Eq(1500.0, (double)parsed["HoldDurationMs"], "HoldDurationMs 不应被改动");

            // 反序列化往返：更新后的文本仍是合法的 EsdConfig 载体
            var config = Newtonsoft.Json.JsonConvert.DeserializeObject<EsdConfig>(updated);
            T.True(config != null && Math.Abs(config.RoiX - 0.1f) < 0.0001f, "更新后文本可正常反序列化");
        }

        /// <summary>手工删过字段的文件要能自动补建 ROI 键；坏输入一律返回 null 由调用方兜底重建</summary>
        private static void EsdUpdateRoiJsonEdgeCases()
        {
            // 缺失的 ROI 字段自动追加到末尾
            string updated = EsdConfig.UpdateRoiJson(
                "{\n  \"Enabled\": true\n}", 0.5f, 0.5f, 0.25f, 0.25f);
            T.True(updated != null, "缺字段输入应成功");
            var parsed = Newtonsoft.Json.Linq.JObject.Parse(updated);
            T.True(Math.Abs((float)parsed["RoiX"] - 0.5f) < 0.0001f, "缺失的 RoiX 应被补建");
            T.Eq(true, (bool)parsed["Enabled"], "原有字段应保留");

            // 非法值内部夹紧（双保险：调用方可能没先夹紧）
            string clamped = EsdConfig.UpdateRoiJson(
                "{\"RoiX\":0.4}", -1f, 2f, 0f, 1f);
            var c = Newtonsoft.Json.Linq.JObject.Parse(clamped);
            T.True(Math.Abs((float)c["RoiX"]) < 0.0001f, "负 X 夹紧到 0");
            T.True(Math.Abs((float)c["RoiY"] - 1f) < 0.0001f, ">1 的 Y 夹紧到 1");
            T.True(Math.Abs((float)c["RoiW"] - 0.01f) < 0.0001f, "0 宽夹紧到最小 0.01");

            // 坏输入返回 null（调用方 SaveEsdRoi 会退化为整对象序列化重建文件）
            T.True(EsdConfig.UpdateRoiJson(null, 0f, 0f, 0f, 0f) == null, "null 输入应返回 null");
            T.True(EsdConfig.UpdateRoiJson("   ", 0f, 0f, 0f, 0f) == null, "空白输入应返回 null");
            T.True(EsdConfig.UpdateRoiJson("{不是JSON", 0f, 0f, 0f, 0f) == null, "坏 JSON 应返回 null");
        }

        /// <summary>EsdConfig 实例方法就地夹紧：内存单例热更新路径（SaveEsdRoi 内部走这里）</summary>
        private static void EsdApplyNormalizedRoiClamps()
        {
            var config = new EsdConfig { RoiX = 0.4f, RoiY = 0.25f, RoiW = 0.2f, RoiH = 0.35f };

            config.ApplyNormalizedRoi(-1f, 2f, 0f, 5f);
            T.Eq(0f, config.RoiX, "负 X 夹紧到 0");
            T.Eq(1f, config.RoiY, ">1 的 Y 夹紧到 1");
            T.Eq(0.01f, config.RoiW, "0 宽夹紧到最小 0.01");
            T.Eq(1f, config.RoiH, ">1 的高夹紧到 1");

            config.ApplyNormalizedRoi(0.42f, 0.31f, 0.18f, 0.27f);
            T.True(Math.Abs(config.RoiX - 0.42f) < 0.0001f, "合法 X 就地写入");
            T.True(Math.Abs(config.RoiY - 0.31f) < 0.0001f, "合法 Y 就地写入");
            T.True(Math.Abs(config.RoiW - 0.18f) < 0.0001f, "合法 W 就地写入");
            T.True(Math.Abs(config.RoiH - 0.27f) < 0.0001f, "合法 H 就地写入");
        }

        /// <summary>
        /// v2.7 新增的未接触灰框显示开关：旧现场 JSON 无此字段时必须反序列化为
        /// false（升级后画面行为不变，不突然冒出灰色 NO GND 框）；显式配置 true
        /// 时经 ToOptions 原样传入模块渲染参数。
        /// </summary>
        private static void EsdDrawNoContactBoxesOption()
        {
            // 旧版 JSON（无 DrawNoContactBoxes 字段）→ 默认 false，ToOptions 同步传递
            var legacy = Newtonsoft.Json.JsonConvert.DeserializeObject<EsdConfig>(
                "{\"Enabled\":true,\"HoldDurationMs\":1500}");
            T.False(legacy.DrawNoContactBoxes, "旧 JSON 缺字段应默认 false（升级画面不变）");
            T.False(legacy.ToOptions().DrawNoContactBoxes, "ToOptions 应传递默认 false");

            // 显式开启 → 生效并透传给检测模块
            var on = Newtonsoft.Json.JsonConvert.DeserializeObject<EsdConfig>(
                "{\"DrawNoContactBoxes\":true}");
            T.True(on.DrawNoContactBoxes, "显式 true 应反序列化生效");
            T.True(on.ToOptions().DrawNoContactBoxes, "ToOptions 应透传 true 给渲染开关");
        }
    }
}
