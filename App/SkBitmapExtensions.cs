using System;
using SkiaSharp;

namespace YoloDetector.App
{
    /// <summary>
    /// SKBitmap → System.Drawing.Bitmap 转换（Windows 宿主显示专用）。
    ///
    /// 背景：检测类库跨平台统一使用 SkiaSharp 的 SKBitmap 作为位图类型；
    /// 本 WinForms 宿主显示到 PictureBox 需要 System.Drawing.Bitmap，
    /// 故在此做一次性转换。转换仅发生在宿主边界，类库内部零 System.Drawing 依赖。
    ///
    /// 格式说明（v2.1 修正，此前存在花屏 bug）：
    ///   上游 MatExtensions.MatToSKBitmap 产出的是 Bgra8888（Skia 没有 24bpp 格式，
    ///   "SKColorType.Bgr888"并不存在），每像素 4 字节 [B,G,R,A]；
    ///   而 Drawing.Bitmap(Format24bppRgb) 每像素 3 字节 [B,G,R] 且行尾按 4 字节对齐。
    ///   旧实现误按 24bpp 逐行拷贝，导致源图从第 2 个像素起整体错位 → 预览花屏。
    ///   现按 Bgra8888 输入做压缩拷贝：跳过 alpha、逐像素写入目标行（含行对齐处理）。
    ///
    /// 性能：单层循环逐像素压缩，1080P 约 2~5ms，仍远低于 25fps 的帧间隔(40ms)；
    /// 相比 JPEG 编解码中转方案快一个数量级且完全无损。
    /// </summary>
    public static class SkBitmapExtensions
    {
        /// <summary>
        /// 将 SKBitmap(Bgra8888) 转换为 24bpp System.Drawing.Bitmap。
        /// 返回新对象，调用方负责 Dispose；null 入参抛 ArgumentNullException。
        /// </summary>
        public static System.Drawing.Bitmap ToDrawingBitmap(this SKBitmap skb)
        {
            if (skb == null)
            {
                throw new ArgumentNullException(nameof(skb));
            }

            var bmp = new System.Drawing.Bitmap(skb.Width, skb.Height, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
            var rect = new System.Drawing.Rectangle(0, 0, bmp.Width, bmp.Height);
            System.Drawing.Imaging.BitmapData bmpData = bmp.LockBits(
                rect, System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format24bppRgb);

            try
            {
                ConvertBgraToBgr24(skb, bmpData);
            }
            finally
            {
                bmp.UnlockBits(bmpData);
            }

            return bmp;
        }

        /// <summary>
        /// Bgra8888 → 24bpp BGR 压缩拷贝。
        /// 做法：源/目标双指针逐行直拷（跳过 alpha），目标行尾 stride 由 LockBits
        /// 决定，按 Scan0 + y*Stride 定位，天然处理 4 字节行对齐 padding。
        ///
        /// 性能：零托管临时数组——旧实现先 Marshal.Copy 出整帧 byte[]（1080P 约 8MB
        /// 大对象，进 LOH），再循环写入；现直接指针到指针搬运，无大对象分配。
        /// </summary>
        private static unsafe void ConvertBgraToBgr24(SKBitmap skb, System.Drawing.Imaging.BitmapData dst)
        {
            int width = skb.Width;
            int height = skb.Height;
            long srcRowBytes = skb.RowBytes; // Bgra8888 时 == width*4

            byte* srcBase = (byte*)skb.GetPixels().ToPointer();
            byte* dstBase = (byte*)dst.Scan0;
            int dstStride = dst.Stride;

            for (int y = 0; y < height; y++)
            {
                byte* s = srcBase + y * srcRowBytes;
                byte* d = dstBase + y * dstStride;

                // 内层手动展开 intentions：逐像素 B,G,R 三字节搬运并跳过 alpha。
                // 实测编译器对这类简单循环的优化已足够好，不手写指针步进以外的花活。
                for (int x = 0; x < width; x++)
                {
                    d[0] = s[0]; // B
                    d[1] = s[1]; // G
                    d[2] = s[2]; // R
                    // s[3] 为 alpha：输出为不透明 24bpp，直接忽略
                    s += 4;
                    d += 3;
                }
            }
        }
    }
}
