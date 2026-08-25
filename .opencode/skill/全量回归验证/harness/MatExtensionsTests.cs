using System;
using OpenCvSharp;
using SkiaSharp;
using YoloDetection;

namespace YoloDetector.Tests
{
    // ============================================================
    // Mat ↔ SKBitmap 互转测试（性能与无损的关键路径）。
    //
    // 往返一致性是本项目显示链路的根基：任何像素差异都意味着
    // 预览画面失真。用随机噪声图逐字节比对，差异必须为 0。
    // ============================================================

    internal static class MatExtensionsTests
    {
        public static void RunAll()
        {
            T.Case("MatSK-往返无损-常规尺寸320x240", Roundtrip_320x240);
            T.Case("MatSK-往返无损-奇数尺寸7x5", Roundtrip_OddSize);
            T.Case("MatSK-往返无损-1x1最小图", Roundtrip_1x1);
            T.Case("MatSK-往返无损-1080P大图", Roundtrip_1080p);
            T.Case("MatSK-灰度输入转换正确", GrayInput);
            T.Case("MatSK-BGRA输入直通", BgraInput);
            T.Case("MatSK-空与null入参契约", NullAndEmptyContract);
        }

        private static void Roundtrip(Mat src, string label)
        {
            using (var skb = MatExtensions.MatToSKBitmap(src))
            {
                T.True(skb != null, label + ": SKBitmap 不应为 null");
                T.Eq(src.Cols, skb.Width, label + ": 宽度一致");
                T.Eq(src.Rows, skb.Height, label + ": 高度一致");

                using (var back = MatExtensions.SKBitmapToMat(skb))
                {
                    T.Eq(0L, TestUtil.DiffBytes(src, back), label + ": 往返像素差异应为0");
                }
            }
        }

        private static void Roundtrip_320x240()
        {
            using (var m = TestUtil.RandomBgrMat(320, 240)) { Roundtrip(m, "320x240"); }
        }

        /// <summary>奇数尺寸最容易暴露步长(stride)对齐类 bug</summary>
        private static void Roundtrip_OddSize()
        {
            using (var m = TestUtil.RandomBgrMat(7, 5)) { Roundtrip(m, "7x5"); }
            using (var m2 = TestUtil.RandomBgrMat(333, 177)) { Roundtrip(m2, "333x177"); }
        }

        private static void Roundtrip_1x1()
        {
            using (var m = TestUtil.RandomBgrMat(1, 1)) { Roundtrip(m, "1x1"); }
        }

        private static void Roundtrip_1080p()
        {
            using (var m = TestUtil.RandomBgrMat(1920, 1080))
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                Roundtrip(m, "1080P");
                sw.Stop();
                T.Info("1080P 往返耗时 " + sw.ElapsedMilliseconds + "ms（参考值应<20ms）");
            }
        }

        /// <summary>灰度 Mat 应被正确扩展为 3 通道且亮度值保留</summary>
        private static void GrayInput()
        {
            using (var gray = new Mat(60, 80, MatType.CV_8UC1, new Scalar(137)))
            using (var skb = MatExtensions.MatToSKBitmap(gray))
            {
                T.True(skb != null, "灰度转换不应返回 null");
                // 转出的 SKBitmap 每个像素 BGRA 均应约等于 (137,137,137,255)
                // 注意 OpenCV GRAY2BGRA 后 alpha=255
                using (var back = MatExtensions.SKBitmapToMat(skb))
                {
                    T.Eq(3, back.Channels(), "回转后应为 3 通道");
                    Cv2.MeanStdDev(back, out var mean, out var std);
                    T.True(Math.Abs(mean.Val0 - 137) < 1.5,
                        "均值应≈137，实际=" + mean.Val0.ToString("F2"));
                    T.True(std.Val0 < 1.5, "标准差应≈0（全图同色），实际=" + std.Val0.ToString("F2"));
                }
            }
        }

        /// <summary>已是 BGRA 的 Mat 应直通使用（不重复转换），结果与输入一致</summary>
        private static void BgraInput()
        {
            using (var bgra = new Mat())
            {
                Cv2.CvtColor(TestUtil.RandomBgrMat(100, 50), bgra, ColorConversionCodes.BGR2BGRA);
                using (var skb = MatExtensions.MatToSKBitmap(bgra))
                using (var back = MatExtensions.SKBitmapToMat(skb))
                {
                    // 回转后变回 BGR：与原始 BGR 数据比对——用重新生成的同源数据不行，
                    // 这里直接比对"bgra→skb→mat(BGR)"与"bgra 去 alpha 后的 BGR"
                    using (var expected = new Mat())
                    {
                        Cv2.CvtColor(bgra, expected, ColorConversionCodes.BGRA2BGR);
                        T.Eq(0L, TestUtil.DiffBytes(expected, back), "BGRA 直通往返应无差异");
                    }
                }
            }
        }

        private static void NullAndEmptyContract()
        {
            T.Eq<object>(null, MatExtensions.MatToSKBitmap(null), "null Mat → null SKBitmap");

            using (var empty = new Mat())
            {
                T.Eq<object>(null, MatExtensions.MatToSKBitmap(empty), "空 Mat → null SKBitmap");
            }

            T.Throws<ArgumentNullException>(() => MatExtensions.SKBitmapToMat(null),
                "null SKBitmap 应抛 ArgumentNullException");
        }
    }
}
