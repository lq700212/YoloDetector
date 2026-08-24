using System;
using System.Collections.Generic;
using YoloDetector.Configuration;

namespace YoloDetector.Cameras
{
    /// <summary>
    /// 相机 API 工厂（简单工厂模式）。
    /// 根据配置中的 CameraBrand 创建对应的 ICameraApi 实现。
    /// 新增品牌：实现 ICameraApi 并在此注册分支即可，调用方代码无需修改。
    /// </summary>
    public static class CameraApiFactory
    {
        /// <summary>根据配置创建相机 API 客户端实例</summary>
        public static ICameraApi Create(string ip)
        {
            var config = AppConfig.Current;

            int port = config.Stream.RtspPort;
            int timeout = config.Connection.TimeoutSeconds;
            string urlFormat = config.Stream.RtspUrlFormat;
            string brand = (config.Api.CameraBrand ?? string.Empty).ToUpperInvariant();

            switch (brand)
            {
                case "ANGEHUA":
                    return new AngehuaCameraApiClient(ip, port, timeout, urlFormat);

                case "HIK":
                case "DAHUA":
                    // 品牌客户端尚未实现，回退到默认实现
                    Infrastructure.Logging.Logger.Write($"品牌 {brand} 尚未实现专用客户端，回退到默认实现(ANGEHUA)");
                    return new AngehuaCameraApiClient(ip, port, timeout, urlFormat);

                default:
                    Infrastructure.Logging.Logger.Write($"未知相机品牌: {brand}，使用默认实现(ANGEHUA)");
                    return new AngehuaCameraApiClient(ip, port, timeout, urlFormat);
            }
        }

        /// <summary>当前支持（含回退支持）的品牌列表</summary>
        public static string[] GetSupportedBrands()
        {
            return new[] { "ANGEHUA", "HIK", "DAHUA" };
        }

        public static bool IsBrandSupported(string brand)
        {
            return Array.Exists(GetSupportedBrands(), b =>
                b.Equals(brand, StringComparison.OrdinalIgnoreCase));
        }
    }
}
