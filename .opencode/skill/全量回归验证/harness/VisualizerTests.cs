using System.Collections.Generic;
using OpenCvSharp;
using YoloDetection;

namespace YoloDetector.Tests
{
    // ============================================================
    // 可视化器测试。
    //
    // 核心契约（IDetectionVisualizer 注释约定）：
    //   1. 不得修改传入的原始 frame（管道还要用它做其他输出）
    //   2. 返回新 Mat 归调用方所有
    //   3. 空帧返回 null 表示绘制失败，调用方回退显示原帧
    //   4. 无结果时输出应与原帧逐像素一致（不能有底噪/花屏）
    // ============================================================

    internal static class VisualizerTests
    {
        public static void RunAll()
        {
            T.Case("可视化-OpenCV空结果输出与原帧一致", OpenCV_NoResults);
            T.Case("可视化-OpenCV不污染原始帧", OpenCV_NoMutateSource);
            T.Case("可视化-YoloBuiltin空结果与原帧一致", Builtin_NoResults);
            T.Case("可视化-YoloBuiltin带结果尺寸不变", Builtin_WithResults);
            T.Case("可视化-两者空帧null契约", EmptyFrameContract);
            T.Case("可视化-工厂按类型创建正确实例", FactoryTypes);
        }

        private static void OpenCV_NoResults()
        {
            using (var frame = TestUtil.RandomBgrMat(320, 240))
            using (var drawn = new OpenCVVisualizer().Draw(frame, new List<DetectionResult>()))
            {
                T.True(drawn != null, "无结果绘制不应返回 null");
                T.Eq(0L, TestUtil.DiffBytes(frame, drawn), "无结果时输出应与原帧逐像素一致");
            }
        }

        /// <summary>Draw 前后对原帧做快照比对：原帧像素必须纹丝不动</summary>
        private static void OpenCV_NoMutateSource()
        {
            using (var frame = TestUtil.RandomBgrMat(320, 240))
            {
                var results = new List<DetectionResult> { FakeDetector.Box(50, 50, 100, 120) };

                Mat before;
                using (before = frame.Clone())
                using (var drawn = new OpenCVVisualizer().Draw(frame, results))
                {
                    T.True(drawn != null, "带结果绘制不应返回 null");
                    T.Eq(0L, TestUtil.DiffBytes(before, frame), "原始帧不得被修改");
                    // 绘制帧必须与原帧不同（框真的画上去了）
                    T.True(TestUtil.DiffBytes(before, drawn) > 0, "绘制帧应与原帧存在差异");
                }
            }
        }

        private static void Builtin_NoResults()
        {
            using (var frame = TestUtil.RandomBgrMat(200, 150))
            using (var skbRoundTrip = MatExtensions.SKBitmapToMat(MatExtensions.MatToSKBitmap(frame)))
            using (var drawn = new YoloBuiltinVisualizer().Draw(frame, null))
            {
                T.True(drawn != null, "null 结果绘制不应返回 null");
                T.Eq(0L, TestUtil.DiffBytes(skbRoundTrip, drawn),
                    "无结果时输出应等于一次无损互转的结果（Skia 路径不改变像素）");
            }
        }

        private static void Builtin_WithResults()
        {
            using (var frame = TestUtil.SyntheticPersonMat(640, 480))
            {
                var results = new List<DetectionResult>
                {
                    FakeDetector.Box(320, 240, 180, 360),
                    FakeDetector.Box(100, 100, 60, 140)
                };
                using (var drawn = new YoloBuiltinVisualizer().Draw(frame, results))
                {
                    T.True(drawn != null, "多目标绘制不应返回 null");
                    T.Eq(640, drawn.Cols, "绘制后宽度不变");
                    T.Eq(480, drawn.Rows, "绘制后高度不变");
                    T.Eq(3, drawn.Channels(), "绘制后仍为 3 通道 BGR");
                    T.True(TestUtil.DiffBytes(frame, drawn) > 0, "检测框应真实画上");
                }
            }
        }

        /// <summary>null 帧与空帧都必须返回 null（调用方据此回退）</summary>
        private static void EmptyFrameContract()
        {
            T.Eq<object>(null, new OpenCVVisualizer().Draw(null, null), "OpenCV: null帧→null");
            T.Eq<object>(null, new YoloBuiltinVisualizer().Draw(null, null), "Builtin: null帧→null");

            using (var empty = new Mat())
            {
                T.Eq<object>(null, new OpenCVVisualizer().Draw(empty, null), "OpenCV: 空帧→null");
                T.Eq<object>(null, new YoloBuiltinVisualizer().Draw(empty, null), "Builtin: 空帧→null");
            }
        }

        private static void FactoryTypes()
        {
            T.True(VisualizerFactory.Create(VisualizerType.YoloBuiltin) is YoloBuiltinVisualizer,
                "YoloBuiltin 应创建内置红框可视化器");
            T.True(VisualizerFactory.Create(VisualizerType.OpenCV) is OpenCVVisualizer,
                "OpenCV 应创建绿框可视化器");

            // 未定义的枚举值 → 回退 OpenCV 实现（强转越界值模拟历史配置脏数据）
            T.True(VisualizerFactory.Create((VisualizerType)999) is OpenCVVisualizer,
                "未知类型应回退到 OpenCV 可视化器");
        }
    }
}
