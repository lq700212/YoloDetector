using System;
using OpenCvSharp;
using YoloDetection;

namespace YoloDetector.Tests
{
    // ============================================================
    // YoloV26Detector 真实模型推理契约测试（使用 Detection\model 现场模型）。
    //
    // 说明：不强制要求"合成图必须检出人"（检测效果依赖现场调参），
    // 但推理流程必须稳定、输出坐标必须落在画面内——任何崩溃或
    // 坐标越界都是 P0 级 bug。
    // ============================================================

    internal static class DetectorTests
    {
        public static void RunAll()
        {
            T.Case("检测器-Initialize空路径抛参数异常", Init_NullPath);
            T.Case("检测器-Initialize不存在文件抛FileNotFound", Init_MissingFile);
            T.Case("检测器-未初始化Detect抛InvalidOperation", Detect_BeforeInit);
            T.Case("检测器-真实模型加载成功", Init_RealModel);
            T.Case("检测器-null与空Mat返回空列表", Detect_NullAndEmpty);
            T.Case("检测器-合成图推理稳定且坐标合法", Detect_SyntheticPerson);
            T.Case("检测器-重复Detect结果确定性", Detect_Deterministic);
            T.Case("检测器-Dispose后调用抛ObjectDisposed", AfterDispose);
        }

        /// <summary>用现场配置的模型新建一个已初始化检测器的公共入口</summary>
        internal static YoloV26Detector CreateInitialized()
        {
            var detector = new YoloV26Detector();
            detector.Initialize(TestUtil.BinPath("Detection", "model", "yolo26n.onnx"));
            return detector;
        }

        private static void Init_NullPath()
        {
            using (var d = new YoloV26Detector())
            {
                T.Throws<ArgumentNullException>(() => d.Initialize(null), "null 路径应抛 ArgumentNullException");
                T.Throws<ArgumentNullException>(() => d.Initialize(""), "空路径应抛 ArgumentNullException");
            }
        }

        private static void Init_MissingFile()
        {
            using (var d = new YoloV26Detector())
            {
                T.Throws<System.IO.FileNotFoundException>(
                    () => d.Initialize(TestUtil.BinPath("Detection", "model", "no_such_model.onnx")),
                    "模型不存在应抛 FileNotFoundException");
                T.False(d.IsInitialized, "失败后 IsInitialized 应保持 false");
            }
        }

        private static void Detect_BeforeInit()
        {
            using (var d = new YoloV26Detector())
            {
                T.False(d.IsInitialized, "初始应为未初始化");
                T.Throws<InvalidOperationException>(
                    () => d.Detect(TestUtil.SyntheticPersonMat()),
                    "未初始化 Detect 应抛 InvalidOperationException");
            }
        }

        private static void Init_RealModel()
        {
            using (var d = CreateInitialized())
            {
                T.True(d.IsInitialized, "现场 yolo26n.onnx 应加载成功");

                // 默认目标类别应只含 person(0)（行为约定，改动会影响业务过滤）
                T.True(d.TargetClassIds.Contains(0), "默认目标类别应包含 person=0");
                T.Eq(1, d.TargetClassIds.Count, "默认目标类别应只有1个");
            }
        }

        private static void Detect_NullAndEmpty()
        {
            using (var d = CreateInitialized())
            {
                T.Eq(0, d.Detect(null).Count, "null 输入 → 空列表（契约：永不返回 null）");
                using (var empty = new Mat())
                {
                    T.Eq(0, d.Detect(empty).Count, "空 Mat → 空列表");
                }
            }
        }

        private static void Detect_SyntheticPerson()
        {
            using (var d = CreateInitialized())
            using (var frame = TestUtil.SyntheticPersonMat(640, 480))
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var results = d.Detect(frame);
                sw.Stop();

                T.True(results != null, "Detect 结果不得为 null（接口契约）");
                T.Info("合成图检出 " + results.Count + " 个目标, 推理耗时 " + sw.ElapsedMilliseconds + "ms");

                foreach (var r in results)
                {
                    // 坐标合法性：框允许贴边但不得整体出画（TryAdd 已做边界裁剪）
                    T.True(r.Left >= -0.01f && r.Right <= 640.01f,
                        "框X范围应在画面内: Left=" + r.Left + " Right=" + r.Right);
                    T.True(r.Top >= -0.01f && r.Bottom <= 480.01f,
                        "框Y范围应在画面内: Top=" + r.Top + " Bottom=" + r.Bottom);
                    T.True(r.Width > 0 && r.Height > 0, "框尺寸应为正");
                    T.True(r.Confidence >= 0f && r.Confidence <= 1f,
                        "置信度应在[0,1]: " + r.Confidence);
                    T.False(string.IsNullOrEmpty(r.ClassName), "ClassName 不应为空");
                }

                // 数量上限：RunInference 里 Take(5) 是硬约束
                T.True(results.Count <= 5, "单帧最多5个目标（Take(5)契约），实际=" + results.Count);
            }
        }

        /// <summary>同一帧两次 Detect 结果应完全一致（推理无随机性）</summary>
        private static void Detect_Deterministic()
        {
            using (var d = CreateInitialized())
            using (var frame = TestUtil.SyntheticPersonMat())
            {
                var a = d.Detect(frame);
                var b = d.Detect(frame);

                T.Eq(a.Count, b.Count, "两次检测数量一致");
                for (int i = 0; i < a.Count; i++)
                {
                    T.Eq(a[i].Confidence, b[i].Confidence, "第" + i + "个置信度一致");
                    T.Eq(a[i].X, b[i].X, "第" + i + "个中心X一致");
                    T.Eq(a[i].Y, b[i].Y, "第" + i + "个中心Y一致");
                }
            }
        }

        private static void AfterDispose()
        {
            var d = CreateInitialized();
            d.Dispose();
            d.Dispose(); // Dispose 幂等

            T.Throws<ObjectDisposedException>(() => d.Detect(TestUtil.SyntheticPersonMat()),
                "Dispose 后 Detect 应抛 ObjectDisposedException");
            T.Throws<ObjectDisposedException>(() => d.Initialize("x.onnx"),
                "Dispose 后 Initialize 应抛 ObjectDisposedException");
        }
    }
}
