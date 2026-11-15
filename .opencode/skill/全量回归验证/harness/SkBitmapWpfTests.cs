using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OpenCvSharp;
using SkiaSharp;
using YoloDetection;
using YoloDetector.App;

namespace YoloDetector.Tests
{
    // ============================================================
    // WPF 宿主边界转换 SkBitmapWpfExtensions（SKBitmap → WriteableBitmap）测试。
    //
    // 背景（防线平移自 v2.1 的 SkBitmapExtensionTests）：
    //   旧 WinForms 宿主转换曾因 Bgra8888(32bpp)/Bgr888(24bpp) 混用导致
    //   从第 2 像素起内存错位、预览花屏。WPF 版的对应契约是：
    //   SKBitmap(Bgra8888) 与 PixelFormats.Bgra32 字节布局完全对齐，
    //   WritePixels 一次整块拷贝——本分区用已知渐变图案逐像素比对锁定。
    //
    // 重点覆盖：
    //   1. Bgra8888 输入（主链路实际格式）像素无损
    //   2. 尺寸不匹配 / 非 Bgra 格式 / null → 返回 false（绝不部分写入）
    //   3. CreateMatchingWriteableBitmap 规格与格式拒绝
    //   4. 与 MatExtensions 真实衔接：Mat → SKBitmap → WriteableBitmap 全链一致
    // ============================================================

    internal static class SkBitmapWpfTests
    {
        public static void RunAll()
        {
            T.Case("WPF宿主转换-Bgra8888像素无损", BgraPixelsPreserved);
            T.Case("WPF宿主转换-尺寸不匹配拒绝", SizeMismatchRejected);
            T.Case("WPF宿主转换-非Bgra格式拒绝", WrongFormatRejected);
            T.Case("WPF宿主转换-null契约", NullContract);
            T.Case("WPF宿主转换-Mat全链一致", MatChainConsistent);
        }

        /// <summary>造一张每个像素都不同的 Bgra8888 渐变 SKBitmap，返回后调用方 Dispose</summary>
        private static SKBitmap MakeGradient(int width, int height)
        {
            var skb = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque);
            unsafe
            {
                byte* p = (byte*)skb.GetPixels();
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        long o = (long)y * skb.RowBytes + x * 4;
                        // 三通道取不同模数，保证相邻像素必然不同
                        p[o + 0] = (byte)((x * 7 + y) % 256);       // B
                        p[o + 1] = (byte)((x + y * 13) % 256);      // G
                        p[o + 2] = (byte)((x * 3 + y * 5) % 256);   // R
                        p[o + 3] = 255;                             // A
                    }
                }
            }
            return skb;
        }

        /// <summary>读回 WriteableBitmap 全部像素（Bgra32 布局：每像素 4 字节 BGRA）</summary>
        private static byte[] ReadPixels(WriteableBitmap wb)
        {
            int stride = wb.BackBufferStride;
            var buf = new byte[stride * wb.PixelHeight];
            wb.CopyPixels(buf, stride, 0);
            return buf;
        }

        /// <summary>主链路实际格式：Bgra8888 → Bgra32 整块拷贝逐像素无损（含奇数宽度）</summary>
        private static void BgraPixelsPreserved()
        {
            // 宽 37（奇数，非 4 字节整行倍数也无妨——Bgra32 每像素 4 字节天然对齐）
            int width = 37, height = 23;
            using (var skb = MakeGradient(width, height))
            {
                // 注意：WriteableBitmap 不实现 IDisposable，生命周期交给 GC（WPF 位图无释放语义）
                var wb = skb.CreateMatchingWriteableBitmap();
                T.True(wb != null, "Bgra8888 应能创建配套位图");
                T.Eq(width, wb.PixelWidth, "宽度一致");
                T.Eq(height, wb.PixelHeight, "高度一致");
                T.Eq(PixelFormats.Bgra32, wb.Format, "输出格式应为 Bgra32");

                T.True(skb.TryWriteTo(wb), "TryWriteTo 应成功");

                byte[] buf = ReadPixels(wb);
                int stride = wb.BackBufferStride;
                int mismatch = 0, firstBadX = -1, firstBadY = -1;
                for (int y = 0; y < height && mismatch < 5; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        int o = y * stride + x * 4;
                        int expB = (x * 7 + y) % 256;
                        int expG = (x + y * 13) % 256;
                        int expR = (x * 3 + y * 5) % 256;
                        if (buf[o + 0] != expB || buf[o + 1] != expG || buf[o + 2] != expR)
                        {
                            mismatch++;
                            if (firstBadX < 0) { firstBadX = x; firstBadY = y; }
                        }
                    }
                }
                T.Eq(0, mismatch,
                    "像素应全部一致（首个错位点: " + firstBadX + "," + firstBadY + "）");
            }
        }

        /// <summary>分辨率变化时必须重建位图：尺寸不匹配的写入应返回 false 而非抛异常</summary>
        private static void SizeMismatchRejected()
        {
            using (var skb = MakeGradient(37, 23))
            {
                var wb = new WriteableBitmap(10, 10, 96, 96, PixelFormats.Bgra32, null);
                T.False(skb.TryWriteTo(wb), "尺寸不匹配应返回 false");
            }
        }

        /// <summary>非 Bgra8888 源宁可整帧丢弃也不能把颜色通道错位画成花屏</summary>
        private static void WrongFormatRejected()
        {
            using (var rgba = new SKBitmap(8, 6, SKColorType.Rgba8888, SKAlphaType.Opaque))
            {
                T.True(rgba.CreateMatchingWriteableBitmap() == null,
                    "非 Bgra8888 源的 CreateMatching 应返回 null");

                var wb = new WriteableBitmap(8, 6, 96, 96, PixelFormats.Bgra32, null);
                T.False(rgba.TryWriteTo(wb), "非 Bgra8888 源的 TryWriteTo 应返回 false");
            }
        }

        private static void NullContract()
        {
            using (var skb = MakeGradient(4, 4))
            {
                var wb = new WriteableBitmap(4, 4, 96, 96, PixelFormats.Bgra32, null);
                T.False(((SKBitmap)null).TryWriteTo(wb), "null SKBitmap 应返回 false");
                T.False(skb.TryWriteTo(null), "null WriteableBitmap 应返回 false");
                T.True(((SKBitmap)null).CreateMatchingWriteableBitmap() == null,
                    "null SKBitmap 的 CreateMatching 应返回 null");
            }
        }

        /// <summary>与 MatExtensions 真实衔接：Mat → SKBitmap → WriteableBitmap 全链像素一致</summary>
        private static void MatChainConsistent()
        {
            using (var mat = TestUtil.RandomBgrMat(101, 53))
            using (var skb = MatExtensions.MatToSKBitmap(mat))
            {
                var wb = skb.CreateMatchingWriteableBitmap();
                T.True(skb.ColorType == SKColorType.Bgra8888, "MatToSKBitmap 应产出 Bgra8888");
                T.True(wb != null, "配套位图创建应成功");
                T.True(skb.TryWriteTo(wb), "TryWriteTo 应成功");

                byte[] buf = ReadPixels(wb);
                int stride = wb.BackBufferStride;
                int mismatch = 0, firstBadX = -1, firstBadY = -1;
                for (int y = 0; y < mat.Rows && mismatch < 5; y++)
                {
                    for (int x = 0; x < mat.Cols; x++)
                    {
                        // OpenCV Mat 为 BGR 3 字节；WriteableBitmap Bgra32 为 BGRA 4 字节
                        var v = mat.Get<Vec3b>(y, x);
                        int o = y * stride + x * 4;
                        if (buf[o + 0] != v.Item0 || buf[o + 1] != v.Item1 || buf[o + 2] != v.Item2)
                        {
                            mismatch++;
                            if (firstBadX < 0) { firstBadX = x; firstBadY = y; }
                        }
                    }
                }
                T.Eq(0, mismatch,
                    "全链像素应一致（首个错位点: " + firstBadX + "," + firstBadY + "）");
            }
        }
    }
}
