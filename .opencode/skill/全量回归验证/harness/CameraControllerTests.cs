using System.Threading.Tasks;
using YoloDetector.App;
using YoloDetector.Cameras;

namespace YoloDetector.Tests
{
    // ============================================================
    // 相机控制器与工厂测试（纯逻辑，不发真实网络请求）。
    // 网络类方法全部走"未连接"分支：契约要求返回 null/false 而非抛异常。
    // ============================================================

    internal static class CameraControllerTests
    {
        public static void RunAll()
        {
            T.Case("相机-未连接时各查询安全返回", NotConnectedSafe);
            T.Case("相机-Disconnect幂等", DisconnectIdempotent);
            T.Case("相机-空IP连接抛参数异常", ConnectEmptyIp);
            T.Case("相机-BuildStreamUrl未连接走配置模板", BuildUrlFallback);
            T.Case("相机-工厂返回Angehua实现且流地址正确", FactoryCreatesAngehua);
        }

        private static void NotConnectedSafe()
        {
            var controller = new CameraController();
            T.False(controller.IsConnected, "初始应未连接");

            var status = Task.Run(() => controller.TryGetStatusAsync()).GetAwaiter().GetResult();
            T.True(status == null, "未连接 TryGetStatusAsync 应返回 null");

            bool rtsp = Task.Run(() => controller.SetRtspAsync(0, true)).GetAwaiter().GetResult();
            T.False(rtsp, "未连接 SetRtspAsync 应返回 false");

            bool rtmp = Task.Run(() => controller.SetRtmpAsync(0, "rtmp://x", true)).GetAwaiter().GetResult();
            T.False(rtmp, "未连接 SetRtmpAsync 应返回 false");
        }

        private static void DisconnectIdempotent()
        {
            var controller = new CameraController();
            controller.Disconnect();
            controller.Disconnect(); // 幂等，不得抛异常
            T.False(controller.IsConnected, "断开后应保持未连接");
        }

        private static void ConnectEmptyIp()
        {
            var controller = new CameraController();
            T.Throws<System.ArgumentException>(
                () => Task.Run(() => controller.ConnectAsync(null)).GetAwaiter().GetResult(),
                "null IP 应抛 ArgumentException");
        }

        /// <summary>未连接时 BuildStreamUrl 应回退到配置模板并包含目标 IP</summary>
        private static void BuildUrlFallback()
        {
            var controller = new CameraController();

            string url = controller.BuildStreamUrl(2, "10.20.30.40");
            T.True(url.Contains("10.20.30.40"), "回退地址应包含传入IP: " + url);
            T.True(url.StartsWith("rtsp://"), "回退地址应以 rtsp:// 开头: " + url);

            // fallbackIp 为空时应使用配置默认 IP（不崩即可，具体值取决于现场配置）
            string url2 = controller.BuildStreamUrl(0, "");
            T.True(!string.IsNullOrEmpty(url2), "空 fallbackIp 也应产出非空地址");
        }

        private static void FactoryCreatesAngehua()
        {
            ICameraApi client = CameraApiFactory.Create("192.168.1.66");

            T.True(client is AngehuaCameraApiClient, "默认品牌应创建 Angehua 实现");

            string url = client.GetVideoStreamUrl(3);
            T.True(url.Contains("192.168.1.66"), "流地址应包含构造注入的 IP: " + url);
            T.True(url.StartsWith("rtsp://"), "流地址应以 rtsp:// 开头: " + url);

            // 工厂对任意输入都不返回 null（HIK/DAHUA/未知品牌均回退 Angehua，
            // 分支切换依赖现场配置的 CameraBrand，此处验证当前激活品牌路径稳定）
            ICameraApi another = CameraApiFactory.Create("0.0.0.0");
            T.True(another != null, "工厂对任意输入都不应返回 null");
        }
    }
}
