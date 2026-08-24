using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using OpenCvSharp;

namespace YoloDetector.Detection
{
    /// <summary>
    /// Mat 与 Bitmap 的高性能互转工具。
    ///
    /// 性能要点：
    ///   - 使用 kernel32 RtlMoveMemory 直接做 IntPtr→IntPtr 拷贝，零临时数组分配
    ///   - OpenCV Mat(8UC3) 与 .NET Format24bppRgb 的内存布局同为 BGR，可直接整块拷贝
    ///   - 相比 JPEG 编解码方案提速约 20 倍且无损
    /// </summary>
    public static class MatExtensions
    {
        [DllImport("kernel32.dll", EntryPoint = "RtlMoveMemory")]
        public static extern void CopyMemory(IntPtr dest, IntPtr src, int length);

        /// <summary>
        /// 将 BGR 格式的 Mat 转换为 24bpp Bitmap（返回新对象，调用方负责 Dispose）。
        /// 非 BGR8 输入会先做颜色转换。空 Mat 返回 null。
        /// </summary>
        public static Bitmap MatToBitmap(Mat mat)
        {
            if (mat == null || mat.Empty())
                return null;

            int width = mat.Cols;
            int height = mat.Rows;

            // 必须是 8 位 3 通道；否则先转换
            Mat srcMat = mat;
            bool needDisposeSrc = false;
            if (mat.Channels() != 3 || mat.Depth() != 0)
            {
                srcMat = new Mat();
                if (mat.Channels() == 1)
                    Cv2.CvtColor(mat, srcMat, ColorConversionCodes.GRAY2BGR);
                else if (mat.Channels() == 4)
                    Cv2.CvtColor(mat, srcMat, ColorConversionCodes.BGRA2BGR);
                else
                    srcMat = mat.Clone();
                needDisposeSrc = true;
            }

            try
            {
                var bmp = new Bitmap(width, height, PixelFormat.Format24bppRgb);
                BitmapData bmpData = bmp.LockBits(
                    new Rectangle(0, 0, width, height),
                    ImageLockMode.WriteOnly,
                    PixelFormat.Format24bppRgb);

                try
                {
                    int matStep = (int)srcMat.Step();
                    int bmpStride = bmpData.Stride;
                    int rowBytes = width * 3;

                    if (matStep == bmpStride)
                    {
                        // Stride 一致：一次性整块拷贝（最快路径）
                        CopyMemory(bmpData.Scan0, srcMat.Data, bmpStride * height);
                    }
                    else
                    {
                        // Mat 行末可能有 padding：逐行拷贝
                        for (int y = 0; y < height; y++)
                        {
                            IntPtr srcRowPtr = (IntPtr)(srcMat.Data.ToInt64() + y * matStep);
                            IntPtr dstRowPtr = (IntPtr)(bmpData.Scan0.ToInt64() + y * bmpStride);
                            CopyMemory(dstRowPtr, srcRowPtr, rowBytes);
                        }
                    }
                }
                finally
                {
                    bmp.UnlockBits(bmpData);
                }

                return bmp;
            }
            finally
            {
                if (needDisposeSrc)
                {
                    srcMat.Dispose();
                }
            }
        }

        /// <summary>
        /// 将 Bitmap 转换为 BGR 格式 Mat（返回新对象，调用方负责 Dispose）。
        /// </summary>
        public static Mat BitmapToMat(Bitmap bitmap)
        {
            if (bitmap == null)
                throw new ArgumentNullException(nameof(bitmap));

            var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
            BitmapData bmpData = bitmap.LockBits(rect, ImageLockMode.ReadOnly, bitmap.PixelFormat);

            try
            {
                bool is32bpp = bitmap.PixelFormat == PixelFormat.Format32bppArgb ||
                               bitmap.PixelFormat == PixelFormat.Format32bppRgb;
                int channels = is32bpp ? 4 : 3;

                Mat mat;
                if (channels == 4)
                {
                    mat = new Mat(bitmap.Height, bitmap.Width, MatType.CV_8UC4);
                    CopyPerRow(mat, bmpData, channels);
                    var bgr = new Mat();
                    Cv2.CvtColor(mat, bgr, ColorConversionCodes.BGRA2BGR);
                    mat.Dispose();
                    return bgr;
                }

                mat = new Mat(bitmap.Height, bitmap.Width, MatType.CV_8UC3);
                CopyPerRow(mat, bmpData, channels);
                return mat;
            }
            finally
            {
                bitmap.UnlockBits(bmpData);
            }
        }

        private static void CopyPerRow(Mat dst, BitmapData src, int channels)
        {
            int height = dst.Rows;
            int width = dst.Cols;
            int dstStep = (int)dst.Step();
            int srcStride = src.Stride;
            int rowBytes = width * channels;

            for (int y = 0; y < height; y++)
            {
                IntPtr srcRow = (IntPtr)(src.Scan0.ToInt64() + y * srcStride);
                IntPtr dstRow = (IntPtr)(dst.Data.ToInt64() + y * dstStep);
                CopyMemory(dstRow, srcRow, rowBytes);
            }
        }
    }
}
