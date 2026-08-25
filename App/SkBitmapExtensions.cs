using System;
using OpenCvSharp;
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
    /// 性能：两者同为 24bpp BGR（Format24bppRgb / Bgr888），走 Buffer.MemoryCopy
    /// 整块/逐行内存拷贝，1080P 约 0.5ms，无格式转换损耗。
    /// </summary>
    public static class SkBitmapExtensions
    {
        /// <summary>将 Bgr888 SKBitmap 转换为 24bpp System.Drawing.Bitmap（新对象，调用方负责 Dispose）</summary>
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
                IntPtr srcBase = skb.GetPixels();
                long srcStride = skb.RowBytes;
                long dstStride = bmpData.Stride;
                int rowBytes = skb.Width * 3;

                for (int y = 0; y < skb.Height; y++)
                {
                    IntPtr srcRow = (IntPtr)(srcBase.ToInt64() + y * srcStride);
                    IntPtr dstRow = (IntPtr)(bmpData.Scan0.ToInt64() + y * dstStride);
                    unsafe
                    {
                        Buffer.MemoryCopy(srcRow.ToPointer(), dstRow.ToPointer(), rowBytes, rowBytes);
                    }
                }
            }
            finally
            {
                bmp.UnlockBits(bmpData);
            }

            return bmp;
        }
    }
}
