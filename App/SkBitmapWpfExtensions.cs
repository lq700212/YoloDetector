using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SkiaSharp;

namespace YoloDetector.App
{
    /// <summary>
    /// SKBitmap → WPF WriteableBitmap 的显示边界转换（WPF 宿主专用）。
    ///
    /// 为什么是这一步：检测类库统一输出 SKBitmap（跨平台位图，App 层不依赖任何 UI 框架），
    /// WPF 显示需要位图是 ImageSource 体系——本扩展就是这条边界的唯一转换入口。
    /// （WinForms 宿主的对应物是 SKBitmap→Drawing.Bitmap，替换为 WPF 后已随旧宿主移除。）
    ///
    /// 性能要点：
    ///   1. SKBitmap 统一为 Bgra8888（检测类库 MatToSKBitmap 的约定格式），与 WPF 的
    ///      PixelFormats.Bgra32 字节布局完全对齐——WritePixels 一次整块内存拷贝即可，
    ///      1080P 约 2~3ms，无逐像素访问、无 GDI 句柄、无编解码中转；
    ///   2. WriteableBitmap 实例由 UI 侧复用（分辨率不变时只创建一次），WritePixels
    ///      原地更新像素、不产生新位图对象，逐帧零大对象分配（LOH 友好，见 AGENTS.md 性能约定）。
    ///
    /// 线程契约：
    ///   WritePixels 要求与 WriteableBitmap 的创建线程相同（WPF 位图的线程亲和性），
    ///   因此本方法必须在 UI 线程调用——调用方负责先把 SKBitmap 调度到 UI 线程
    ///   （参照 MainWindow.SafeDispatcherInvoke），并保证用完 Dispose。
    /// </summary>
    public static class SkBitmapWpfExtensions
    {
        /// <summary>
        /// 按源图规格创建配套的 WriteableBitmap（首帧或分辨率变化时调用一次）。
        /// 返回 null 表示源图格式不受支持，调用方应丢弃该帧。
        /// </summary>
        public static WriteableBitmap CreateMatchingWriteableBitmap(this SKBitmap source)
        {
            if (source == null || source.ColorType != SKColorType.Bgra8888)
            {
                return null;
            }

            // DPI 固定 96：检测帧的像素尺寸即显示逻辑尺寸，缩放交给 Image 控件的
            // Stretch=Uniform（letterbox 等比缩放），与 WinForms 版 SizeMode=Zoom 语义一致
            return new WriteableBitmap(source.Width, source.Height, 96, 96, PixelFormats.Bgra32, null);
        }

        /// <summary>
        /// 把 SKBitmap 的像素整块写入目标 WriteableBitmap（UI 线程调用）。
        /// 返回 false 表示参数/格式/尺寸不匹配（调用方应重建位图或丢弃该帧），绝不部分写入。
        /// </summary>
        public static bool TryWriteTo(this SKBitmap source, WriteableBitmap target)
        {
            if (source == null || target == null)
            {
                return false;
            }

            // 格式防御：Bgra8888 与 Bgra32 才能逐字节对齐。上游格式万一变化，
            // 宁可整帧丢弃也不能把颜色通道错位画成花屏（v2.x SkBitmapExtensionTests 的教训平移）
            if (source.ColorType != SKColorType.Bgra8888)
            {
                return false;
            }

            // 尺寸校验：分辨率变化时必须重建 WriteableBitmap，直接写入会抛异常
            if (source.Width != target.PixelWidth || source.Height != target.PixelHeight)
            {
                return false;
            }

            // 整块拷贝：源缓冲 = SKBitmap 像素区（Bgra8888），步距 = RowBytes
            // WritePixels(sourceRect, sourceBuffer, bufferSize, sourceBufferStride)
            long byteCount = (long)source.RowBytes * source.Height;
            target.WritePixels(
                new Int32Rect(0, 0, source.Width, source.Height),
                source.GetPixels(),
                checked((int)byteCount),
                source.RowBytes);
            return true;
        }
    }
}
