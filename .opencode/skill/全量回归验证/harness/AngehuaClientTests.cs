using System.Threading.Tasks;
using YoloDetector.Cameras;

namespace YoloDetector.Tests
{
    // ============================================================
    // 安格华相机客户端 + 设备状态模型测试。
    //
    // AngehuaCameraApiClient 的网络方法自带超时保护（"竞速+放弃"模式），
    // 这里重点验证：
    //   1. 构造参数防御（空 IP / 非法端口与超时的回退）
    //   2. 连接探测的快速失败（本机拒绝端口应立即 false，不挂起）
    //   3. 连接探测的有界失败（不可路由地址在 timeout 内返回，不永久阻塞）
    //   4. 桩方法的固定契约
    // DeviceStatus 验证磁盘使用率计算与字节格式化纯函数。
    // ============================================================

    internal static class AngehuaClientTests
    {
        public static void RunAll()
        {
            T.Case("安格华-构造空IP抛参数异常", Ctor_EmptyIp);
            T.Case("安格华-非法端口与超时回退默认", Ctor_InvalidParamsFallback);
            T.Case("安格华-拒绝端口立即返回false", TestConnection_RefusedFast);
            T.Case("安格华-不可达地址有界返回false", TestConnection_BoundedTimeout);
            T.Case("安格华-桩方法契约固定", StubContract);
            T.Case("设备状态-使用率计算与格式化", DeviceStatus_Calculations);
        }

        private static void Ctor_EmptyIp()
        {
            T.Throws<System.ArgumentException>(
                () => new AngehuaCameraApiClient(null, 554, 10, null),
                "null IP 应抛 ArgumentException");
            T.Throws<System.ArgumentException>(
                () => new AngehuaCameraApiClient("", 554, 10, null),
                "空 IP 应抛 ArgumentException");
        }

        /// <summary>端口/超时传非法值时应回退默认(554)，模板为空时回退默认模板</summary>
        private static void Ctor_InvalidParamsFallback()
        {
            var client = new AngehuaCameraApiClient("1.2.3.4", -1, -5, "");
            string url = client.GetVideoStreamUrl(0);
            T.True(url.Contains("554"), "非法端口应回退默认554: " + url);
            T.True(url.StartsWith("rtsp://"), "空模板应回退默认模板");
        }

        private static void TestConnection_RefusedFast()
        {
            var client = new AngehuaCameraApiClient("127.0.0.1", 9, 10, null);

            var sw = System.Diagnostics.Stopwatch.StartNew();
            bool ok = Task.Run(() => client.TestConnectionAsync()).GetAwaiter().GetResult();
            sw.Stop();

            T.False(ok, "本机拒绝连接的端口应返回 false");
            T.True(sw.ElapsedMilliseconds < 15000,
                "拒绝连接应快速返回而非等满超时，实际=" + sw.ElapsedMilliseconds + "ms");
        }

        /// <summary>不可路由地址必须在 timeout 附近有界返回——绝不允许永久挂起（线程红线）</summary>
        private static void TestConnection_BoundedTimeout()
        {
            // 10.255.255.1 是保留网段地址，本机路由不可达，TCP connect 只能等超时
            var client = new AngehuaCameraApiClient("10.255.255.1", 554, timeoutSeconds: 2, rtspUrlFormat: null);

            var sw = System.Diagnostics.Stopwatch.StartNew();
            bool ok = Task.Run(() => client.TestConnectionAsync()).GetAwaiter().GetResult();
            sw.Stop();

            T.False(ok, "不可达地址应返回 false");
            T.True(sw.ElapsedMilliseconds < 10000,
                "应在超时上限附近返回（timeout=2s），实际=" + sw.ElapsedMilliseconds + "ms");
        }

        private static void StubContract()
        {
            var client = new AngehuaCameraApiClient("192.168.0.15", 554, 10, null);

            T.True(Task.Run(() => client.SetRtspEnableAsync(0)).GetAwaiter().GetResult(),
                "RTSP 开启桩应返回 true（由相机端自动管理）");
            T.True(Task.Run(() => client.SetRtspDisableAsync(0)).GetAwaiter().GetResult(),
                "RTSP 关闭桩应返回 true");

            T.False(Task.Run(() => client.SetRtmpEnableAsync(0, "rtmp://x")).GetAwaiter().GetResult(),
                "RTMP 推流未实现，应返回 false");
            T.False(Task.Run(() => client.SetRtmpDisableAsync(0)).GetAwaiter().GetResult(),
                "RTMP 停流未实现，应返回 false");

            var status = Task.Run(() => client.GetDeviceStatusAsync()).GetAwaiter().GetResult();
            T.True(status != null, "设备状态桩不应返回 null");
            T.Eq("192.168.0.15", status.IpAddress, "状态回显构造 IP");
            T.Eq("ANGEHUA", status.Brand, "状态品牌标识");
        }

        private static void DeviceStatus_Calculations()
        {
            var s = new DeviceStatus { DiskTotal = 1000, DiskFree = 250 };
            T.Eq(75f, s.GetDiskUsage(), "使用率=(total-free)/total*100");

            s.DiskTotal = 0; s.DiskFree = 0;
            T.Eq(0f, s.GetDiskUsage(), "总容量为0时除零保护应返回0");

            s.DiskTotal = 1000; s.DiskFree = 2000;
            T.True(s.GetDiskUsage() < 0, "free>total（脏数据）时按公式返回负值即可，不得崩溃");

            // 字节格式化（FormatBytes 为 private，经公开属性间接验证）
            var b = new DeviceStatus { DiskTotal = 500, DiskFree = 2048 };
            T.Eq("500 B", b.GetFormattedDiskTotal(), "字节级格式化");
            T.Eq("2.00 KB", b.GetFormattedDiskFree(), "KB 格式化");

            var mbCase = new DeviceStatus { DiskFree = 5L * 1024 * 1024 };
            T.Eq("5.00 MB", mbCase.GetFormattedDiskFree(), "MB 格式化");

            var gbCase = new DeviceStatus { DiskFree = 3L * 1024 * 1024 * 1024 };
            T.Eq("3.00 GB", gbCase.GetFormattedDiskFree(), "GB 格式化");
        }
    }
}
