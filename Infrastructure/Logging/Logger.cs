using System;
using System.IO;

namespace YoloDetector.Infrastructure.Logging
{
    /// <summary>
    /// 文件日志工具（静态类）。
    /// 特性：
    ///   1. 自动创建 logs 目录（程序运行目录下）
    ///   2. 按日期分割日志文件（log_yyyy-MM-dd.txt）
    ///   3. 线程安全（lock 串行化写入）
    ///   4. 启动/退出自动写入分隔线
    /// </summary>
    public static class Logger
    {
        private static StreamWriter _streamWriter;
        private static bool _isInitialized;
        private static bool _closed;
        private static readonly object _lockObj = new object();

        private static string LogDirectory => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
        private static string LogFilePath => Path.Combine(LogDirectory, $"log_{DateTime.Now:yyyy-MM-dd}.txt");

        /// <summary>写入一行日志（首次调用时惰性初始化文件写入器）</summary>
        public static void Write(string message)
        {
            if (message == null) return;

            lock (_lockObj)
            {
                try
                {
                    // 已关闭后不再重开（防止退出阶段在途回调重新打开文件句柄）
                    if (_closed)
                    {
                        return;
                    }

                    if (!_isInitialized)
                    {
                        Initialize();
                    }

                    if (_streamWriter != null)
                    {
                        string time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                        _streamWriter.WriteLine($"[{time}] {message}");
                        _streamWriter.Flush();
                    }
                }
                catch (Exception ex)
                {
                    // 写日志失败绝不影响主流程
                    System.Diagnostics.Debug.WriteLine("日志写入失败: " + ex.Message);
                }
            }
        }

        /// <summary>关闭日志系统（程序退出时调用）</summary>
        public static void Close()
        {
            lock (_lockObj)
            {
                try
                {
                    if (_streamWriter != null)
                    {
                        _streamWriter.WriteLine($"================== 程序退出 [{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ==================");
                        _streamWriter.Flush();
                        _streamWriter.Dispose();
                        _streamWriter = null;
                    }
                    _isInitialized = false;
                    _closed = true;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("日志关闭失败: " + ex.Message);
                }
            }
        }

        private static void Initialize()
        {
            try
            {
                if (!Directory.Exists(LogDirectory))
                {
                    Directory.CreateDirectory(LogDirectory);
                }

                // 追加模式，UTF-8 支持中文
                _streamWriter = new StreamWriter(LogFilePath, true, System.Text.Encoding.UTF8);
                _streamWriter.WriteLine();
                _streamWriter.WriteLine($"================== 程序启动 [{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ==================");
                _streamWriter.Flush();
                _isInitialized = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("日志初始化失败: " + ex.Message);
            }
        }
    }
}
