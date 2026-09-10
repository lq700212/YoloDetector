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
            T.Case("检测器-TryAdd类别与置信度过滤", TryAdd_Filters);
            T.Case("检测器-TryAdd边界裁剪与出界丢弃", TryAdd_Clipping);
            T.Case("检测器-ParseOutput小N原始格式正确解析", ParseOutput_SmallN_Raw);
            T.Case("检测器-ParseOutput的NMS格式与非法形状", ParseOutput_NmsAndInvalid);
            T.Case("检测器-ApplyNms重叠去重", ApplyNms_Dedup);
            T.Case("检测器-奇数尺寸推理稳定", Detect_OddSize);
            T.Case("检测器-连续推理耗时平稳(性能观察)", Detect_SteadyPerf);
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

        // ---------- 私有方法反射辅助（过滤/NMS/解析是纯逻辑，反射直测不依赖模型输出） ----------

        private static System.Reflection.MethodInfo TryAddMethod()
        {
            return typeof(YoloV26Detector).GetMethod("TryAdd",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        }

        /// <summary>
        /// 类别过滤（默认仅 person=0）+ 置信度阈值（默认 0.35）：
        /// 检测器层面的第一道过滤，行为变化会直接改变检出内容，必须锁定。
        /// </summary>
        private static void TryAdd_Filters()
        {
            using (var d = CreateInitialized())
            {
                var m = TryAddMethod();
                var results = new System.Collections.Generic.List<DetectionResult>();

                // 非 person 类别直接丢弃（画面中央大框也不要）
                m.Invoke(d, new object[] { results, 0.95f, 2, 320f, 240f, 100f, 200f, 640, 480 });
                T.Eq(0, results.Count, "非目标类别(classId=2)应被过滤");

                // person 但置信度低于阈值丢弃
                m.Invoke(d, new object[] { results, 0.10f, 0, 320f, 240f, 100f, 200f, 640, 480 });
                T.Eq(0, results.Count, "低置信度(0.10<0.35)应被过滤");

                // person + 达标置信度保留，且名称映射为 person
                m.Invoke(d, new object[] { results, 0.90f, 0, 320f, 240f, 100f, 200f, 640, 480 });
                T.Eq(1, results.Count, "达标 person 应保留");
                T.Eq("person", results[0].ClassName, "classId=0 应映射为 person");
                T.Eq(320f, results[0].X, "中心X保持");
                T.Eq(100f, results[0].Width, "宽度保持");
            }
        }

        /// <summary>边界裁剪：部分出界保留可见部分；整体出界丢弃（贴边大目标是正常场景）</summary>
        private static void TryAdd_Clipping()
        {
            using (var d = CreateInitialized())
            {
                var m = TryAddMethod();
                var results = new System.Collections.Generic.List<DetectionResult>();

                // 左半出界：中心(0,240) 宽200 → left=-100 出界，裁后 left=0 宽100 中心50
                m.Invoke(d, new object[] { results, 0.9f, 0, 0f, 240f, 200f, 200f, 640, 480 });
                T.Eq(1, results.Count, "部分出界框应保留");
                T.Eq(0f, results[0].Left, "Left 应裁到 0");
                T.Eq(100f, results[0].Width, "裁后宽=100");
                T.Eq(50f, results[0].X, "裁后中心X=50");

                // 整体在画面右侧外：丢弃
                m.Invoke(d, new object[] { results, 0.9f, 0, 900f, 240f, 100f, 100f, 640, 480 });
                T.Eq(1, results.Count, "完全出界框应丢弃（数量不变）");
            }
        }

        /// <summary>
        /// 回归防线（v2.9.2 布局误判修复）：[1,5,84] 是"5 个候选的 84 维原始预测"，
        /// 旧逻辑只看 shape[1]=5<=7 会误判为转置布局导致越界、整帧变空结果。
        /// 用合成张量直接喂 ParseOutput，验证正确解析出候选。
        /// </summary>
        private static void ParseOutput_SmallN_Raw()
        {
            using (var d = CreateInitialized())
            {
                var m = typeof(YoloV26Detector).GetMethod("ParseOutput",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                // 5 候选 x 84 维：候选0 为 person 高置信（模型输入空间坐标直通：pad=0 scale=1）
                float[] data = new float[5 * 84];
                data[0 * 84 + 0] = 320f; // cx
                data[0 * 84 + 1] = 240f; // cy
                data[0 * 84 + 2] = 100f; // w
                data[0 * 84 + 3] = 200f; // h
                data[0 * 84 + 4] = 0.9f; // obj
                data[0 * 84 + 5] = 0.8f; // cls0 person
                // 候选1：类别为车（cls2 高），应被类别过滤
                data[1 * 84 + 0] = 100f; data[1 * 84 + 1] = 100f;
                data[1 * 84 + 2] = 50f; data[1 * 84 + 3] = 50f;
                data[1 * 84 + 4] = 0.9f; data[1 * 84 + 7] = 0.9f;
                var tensor = new Microsoft.ML.OnnxRuntime.Tensors.DenseTensor<float>(
                    data, new int[] { 1, 5, 84 });

                var results = new System.Collections.Generic.List<DetectionResult>();
                m.Invoke(d, new object[] { tensor, results, 1f, 1f, 0f, 0f, 640, 480 });

                T.Eq(1, results.Count, "5候选中只有 person 高置信应被检出（旧逻辑此处为空）");
                T.Eq(320f, results[0].X, "中心X=320");
                T.Eq(240f, results[0].Y, "中心Y=240");
                T.Eq(100f, results[0].Width, "宽=100");
                T.Eq(200f, results[0].Height, "高=200");
            }
        }

        /// <summary>NMS 角点格式 [1,6,N] 转置解析 + 非法形状显式拒绝（不抛、空列表）</summary>
        private static void ParseOutput_NmsAndInvalid()
        {
            using (var d = CreateInitialized())
            {
                var m = typeof(YoloV26Detector).GetMethod("ParseOutput",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                // NMS 格式 [1,6,2]：候选0 person(320,240,100,200 conf0.9)，候选1 低置信丢弃
                float[] nms = new float[6 * 2];
                nms[0 * 2 + 0] = 270f; nms[1 * 2 + 0] = 140f; // x1,y1
                nms[2 * 2 + 0] = 370f; nms[3 * 2 + 0] = 340f; // x2,y2
                nms[4 * 2 + 0] = 0.9f; nms[5 * 2 + 0] = 0f;   // conf, cls
                nms[0 * 2 + 1] = 10f; nms[1 * 2 + 1] = 10f;
                nms[2 * 2 + 1] = 60f; nms[3 * 2 + 1] = 60f;
                nms[4 * 2 + 1] = 0.1f; nms[5 * 2 + 1] = 0f;
                var nmsTensor = new Microsoft.ML.OnnxRuntime.Tensors.DenseTensor<float>(
                    nms, new int[] { 1, 6, 2 });

                var results = new System.Collections.Generic.List<DetectionResult>();
                m.Invoke(d, new object[] { nmsTensor, results, 1f, 1f, 0f, 0f, 640, 480 });
                T.Eq(1, results.Count, "NMS 格式应解析出 1 个达标框");
                T.Eq(320f, results[0].X, "角点转中心X=(270+370)/2=320");

                // 非法形状 [1,3,4]：特征维 4（<6）→ 显式拒绝，不抛异常
                var bad = new Microsoft.ML.OnnxRuntime.Tensors.DenseTensor<float>(
                    new float[12], new int[] { 1, 3, 4 });
                var badResults = new System.Collections.Generic.List<DetectionResult>();
                try
                {
                    m.Invoke(d, new object[] { bad, badResults, 1f, 1f, 0f, 0f, 640, 480 });
                    T.Eq(0, badResults.Count, "非法形状应返回空列表");
                }
                catch (Exception ex)
                {
                    T.Fail("非法形状不应抛异常: " + ex.GetType().Name);
                }
            }
        }

        /// <summary>NMS 去重：完全重叠取置信度最高；不重叠全部保留</summary>
        private static void ApplyNms_Dedup()
        {
            using (var d = CreateInitialized())
            {
                var m = typeof(YoloV26Detector).GetMethod("ApplyNms",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                var dup = new System.Collections.Generic.List<DetectionResult>();
                for (int i = 0; i < 5; i++)
                {
                    dup.Add(FakeDetector.Box(100, 100, 60, 120, 0.9f - i * 0.1f));
                }
                var one = m.Invoke(d, new object[] { dup }) as System.Collections.Generic.List<DetectionResult>;
                T.Eq(1, one.Count, "5 个全重叠框应去重为 1");
                T.Eq(0.9f, one[0].Confidence, "保留置信度最高者");

                var apart = new System.Collections.Generic.List<DetectionResult>
                {
                    FakeDetector.Box(50, 50, 30, 40),
                    FakeDetector.Box(400, 300, 30, 40)
                };
                var two = m.Invoke(d, new object[] { apart }) as System.Collections.Generic.List<DetectionResult>;
                T.Eq(2, two.Count, "不重叠框应全部保留");
            }
        }

        /// <summary>奇数/非标准尺寸走 letterbox+pad 路径：推理稳定、坐标合法（防 stride 取整 bug）</summary>
        private static void Detect_OddSize()
        {
            using (var d = CreateInitialized())
            using (var frame = TestUtil.SyntheticPersonMat(321, 241))
            {
                var results = d.Detect(frame);
                T.True(results != null, "奇数尺寸结果不得为 null");
                foreach (var r in results)
                {
                    T.True(r.Left >= -0.01f && r.Right <= 321.01f, "框X应在画面内");
                    T.True(r.Top >= -0.01f && r.Bottom <= 241.01f, "框Y应在画面内");
                }
                T.True(results.Count <= 5, "Top5 上限仍生效");
            }
        }

        /// <summary>
        /// 性能观察（只打印不断言阈值，避免机器差异导致 flaky）：
        /// 同一帧连续推理 5 次（跳过首次预热），打印平均耗时供长期跟踪。
        /// v2.9.2 优化点：预处理大数组复用 + 输出张量整块展平（替代 70 万次索引器调用）。
        /// </summary>
        private static void Detect_SteadyPerf()
        {
            using (var d = CreateInitialized())
            using (var frame = TestUtil.SyntheticPersonMat(640, 480))
            {
                d.Detect(frame); // 预热（含模型首次执行、缓冲分配）
                var sw = System.Diagnostics.Stopwatch.StartNew();
                const int rounds = 5;
                for (int i = 0; i < rounds; i++)
                {
                    d.Detect(frame);
                }
                sw.Stop();
                T.Info("连续推理平均耗时 " + (sw.ElapsedMilliseconds / (double)rounds).ToString("F1") +
                       "ms/帧（640x480合成图，CPU，含预处理+NMS）");
            }
        }
    }
}
