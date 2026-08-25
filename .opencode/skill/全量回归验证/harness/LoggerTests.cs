using System;
using System.IO;

namespace YoloDetector.Tests
{
    // ============================================================
    // 文件日志 Logger 直接契约测试。
    //
    // 顺序要求：必须放在 UiSmokeTests 之前——Logger.Close() 置位 _closed
    // 后整个进程的日志静默失效（防退出期重开句柄的设计），MainForm 关闭
    // 也会调 Close，故本分区自身 Close 的副作用不影响后续用例功能。
    //
    // 注意：harness 运行目录=主 bin，本分区写入的 logs\log_*.txt 与主程序
    // 日志同目录（bin 已被 .gitignore 忽略，无入库风险）。
    // ============================================================

    internal static class LoggerTests
    {
        public static void RunAll()
        {
            T.Case("文件日志-写入创建目录与内容", WriteCreatesFileAndContent);
            T.Case("文件日志-Close幂等且关闭后静默", CloseContract);
        }

        private static void WriteCreatesFileAndContent()
        {
            string logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
            string logFile = Path.Combine(logDir,
                "log_" + DateTime.Now.ToString("yyyy-MM-dd") + ".txt");

            string marker = "[HarnessTest-" + Guid.NewGuid().ToString("N").Substring(0, 8) + "]";
            YoloDetector.Infrastructure.Logging.Logger.Write("测试日志条目 " + marker);

            T.True(Directory.Exists(logDir), "Write 应自动创建 logs 目录");
            T.True(File.Exists(logFile), "Write 应惰性初始化当日日志文件");

            // Flush 在 Write 内完成，可立即读到。
            // 注意必须用共享读打开：Logger 的 StreamWriter 常驻持有写句柄（设计如此），
            // File.ReadAllText 默认独占打开会抛 IOException
            string content;
            using (var fs = new FileStream(logFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(fs, System.Text.Encoding.UTF8))
            {
                content = reader.ReadToEnd();
            }
            T.True(content.Contains(marker), "写入的标记文本应立即落地可见");
            T.True(content.Contains("程序启动"), "首次初始化应写『程序启动』分隔标记");
        }

        private static void CloseContract()
        {
            YoloDetector.Infrastructure.Logging.Logger.Close();
            YoloDetector.Infrastructure.Logging.Logger.Close(); // 幂等，不得抛异常

            // Close 后再 Write：设计为静默丢弃（防退出阶段在途回调重开句柄），绝不能抛异常
            try
            {
                YoloDetector.Infrastructure.Logging.Logger.Write("关闭后的写入应被静默丢弃");
                T.True(true, "Close 后 Write 不抛异常即为通过");
            }
            catch (Exception ex)
            {
                T.Fail("Close 后 Write 抛异常: " + ex.GetType().Name);
            }

            // null 消息防御（契约：直接忽略）
            try
            {
                YoloDetector.Infrastructure.Logging.Logger.Write(null);
                T.True(true, "null 消息不抛异常即为通过");
            }
            catch (Exception ex)
            {
                T.Fail("null 消息抛异常: " + ex.GetType().Name);
            }
        }
    }
}
