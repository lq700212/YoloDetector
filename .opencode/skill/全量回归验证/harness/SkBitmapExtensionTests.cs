using System;
using System.Drawing;
using OpenCvSharp;
using SkiaSharp;
using YoloDetection;
using YoloDetector.App;

namespace YoloDetector.Tests
{
    // ============================================================
    // 宿主边界转换 SkBitmapExtensions.ToDrawingBitmap 测试。
    //
    // 背景（v2.1 回归发现的真实 bug 现场）：
    //   MatToSKBitmap 产出的是 Bgra8888(32bpp)，而本转换早期版本按
    //   Bgr888(24bpp) 逐行拷贝——从第 2 像素起内存布局错位，预览画面
    //   必然花屏。本分区用已知渐变图案逐像素比对锁定该契约。
    //
    // 重点覆盖：
    //   1. Bgra8888 输入（主链路实际格式）像素无损
    //   2. 奇数宽度（24bpp 行尾 4 字节对齐 padding 不破坏最后一列）
    //   3. null 契约
    // ============================================================

    internal static class SkBitmapExtensionTests
    {
        public static void RunAll()
        {
            T.Case("宿主转换-Bgra8888输入像素无损", BgraPixelsPreserved);
            T.Case("宿主转换-奇数宽度行对齐", OddWidthStride);
            T.Case("宿主转换-null抛参数异常", NullContract);
        }

        /// <summary>造一张每个像素都不同的渐变 SKBitmap（指定颜色类型），返回后调用方 Dispose</summary>
        private static SKBitmap MakeGradient(int width, int height, SKColorType colorType)
        {
            var skb = new SKBitmap(width, height, colorType, SKAlphaType.Opaque);
            IntPtr basePtr = skb.GetPixels();
            long rowBytes = skb.RowBytes;
            int bpp = colorType == SKColorType.Bgra8888 ? 4 : 3;

            unsafe
            {
                byte* p = (byte*)basePtr;
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        long offset = y * rowBytes + x * bpp;
                        // 三通道取不同模数，保证相邻像素必然不同
                        p[offset + 0] = (byte)((x * 7 + y) % 256);       // B
                        p[offset + 1] = (byte)((x + y * 13) % 256);      // G
                        p[offset + 2] = (byte)((x * 3 + y * 5) % 256);   // R
                        if (bpp == 4) p[offset + 3] = 255;               // A
                    }
                }
            }
            return skb;
        }

        /// <summary>通用断言：SKBitmap 经 ToDrawingBitmap 后逐像素等于预期 BGR 值</summary>
        private static void AssertPixelsPreserved(SKColorType colorType)
        {
            int width = 37, height = 23;
            using (var skb = MakeGradient(width, height, colorType))
            using (Bitmap bmp = skb.ToDrawingBitmap())
            {
                T.Eq(width, bmp.Width, "宽度一致");
                T.Eq(height, bmp.Height, "高度一致");
                T.Eq(System.Drawing.Imaging.PixelFormat.Format24bppRgb, bmp.PixelFormat, "输出应为 24bpp");

                // 锁定整幅图逐像素一致（首像素碰巧对、后续全错的错位模式会被这里抓出）
                var rect = new Rectangle(0, 0, width, height);
                var data = bmp.LockBits(rect,
                    System.Drawing.Imaging.ImageLockMode.ReadOnly,
                    System.Drawing.Imaging.PixelFormat.Format24bppRgb);
                try
                {
                    unsafe
                    {
                        byte* basePtr = (byte*)data.Scan0;
                        int mismatch = 0, firstBadX = -1, firstBadY = -1;
                        for (int y = 0; y < height && mismatch < 5; y++)
                        {
                            for (int x = 0; x < width; x++)
                            {
                                byte* px = basePtr + y * data.Stride + x * 3;
                                int expB = (x * 7 + y) % 256;
                                int expG = (x + y * 13) % 256;
                                int expR = (x * 3 + y * 5) % 256;

                                if (px[0] != expB || px[1] != expG || px[2] != expR)
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
                finally
                {
                    bmp.UnlockBits(data);
                }
            }
        }

        /// <summary>主链路实际格式：MatToSKBitmap 产出的就是 Bgra8888</summary>
        private static void BgraPixelsPreserved()
        {
            AssertPixelsPreserved(SKColorType.Bgra8888);
            // 再验证与 MatExtensions 的真实衔接：Mat → SKBitmap → Drawing.Bitmap 全链一致
            using (var mat = TestUtil.RandomBgrMat(101, 53))
            using (var skb = MatExtensions.MatToSKBitmap(mat))
            using (var bmp = skb.ToDrawingBitmap())
            {
                T.True(skb.ColorType == SKColorType.Bgra8888, "MatToSKBitmap 应产出 Bgra8888");
                T.Eq(mat.Cols, bmp.Width, "全链宽度一致");
                T.Eq(mat.Rows, bmp.Height, "全链高度一致");
            }
        }

        /// <summary>宽 7 时 24bpp 行宽 21 字节非 4 倍数，Bitmap 行有 padding——最后一列不得被污染</summary>
        private static void OddWidthStride()
        {
            int width = 7, height = 5;
            using (var skb = MakeGradient(width, height, SKColorType.Bgra8888))
            using (var bmp = skb.ToDrawingBitmap())
            {
                // 抽查最右一列（紧邻行尾 padding 区）：padding 不得污染像素
                bool allMatch = true;
                for (int y = 0; y < height; y++)
                {
                    var c = bmp.GetPixel(width - 1, y);
                    int expB = ((width - 1) * 7 + y) % 256;
                    int expG = ((width - 1) + y * 13) % 256;
                    int expR = (((width - 1) * 3) + y * 5) % 256;
                    if (c.B != expB || c.G != expG || c.R != expR)
                    {
                        allMatch = false;
                    }
                }
                T.True(allMatch, "奇数宽度最后一列像素应正确（stride 对齐不被破坏）");
            }
        }

        private static void NullContract()
        {
            T.Throws<ArgumentNullException>(() => ((SKBitmap)null).ToDrawingBitmap(),
                "null SKBitmap 应抛 ArgumentNullException");
        }
    }
}
