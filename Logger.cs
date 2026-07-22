using System;
using System.IO;

namespace YoloDetector
{
    // ============================================================
    // 日志工具类
    // 功能：将程序运行时的日志信息写入本地文件，方便问题排查和记录
    // 使用方式：直接调用 Logger.Write("日志内容")，不需要创建实例
    // 特点：
    //   1. 自动创建logs目录（在程序运行目录下）
    //   2. 按日期分割日志文件（每天一个文件，如log_2026-07-11.txt）
    //   3. 线程安全（多线程同时写日志不会出错）
    //   4. 每次启动和退出会自动添加分隔线，方便区分不同运行会话
    // ============================================================
    public static class Logger
    {
        // --------------------------
        // 成员变量
        // --------------------------
        
        // 日志文件写入器，负责向文件写入内容
        private static StreamWriter _streamWriter;
        
        // 锁对象，用于线程安全，防止多个线程同时写日志导致文件错乱
        private static readonly object _lockObj = new object();
        
        // 是否已初始化的标志，确保初始化只执行一次
        private static bool _isInitialized = false;

        // --------------------------
        // 日志路径相关属性（只读）
        // --------------------------
        
        // 日志目录路径：程序运行目录 + "/logs"
        // AppDomain.CurrentDomain.BaseDirectory 就是程序.exe所在的目录
        private static string LogDirectory => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");

        // 日志文件名：格式为 "log_yyyy-MM-dd.txt"，每天自动生成新文件
        private static string LogFileName => $"log_{DateTime.Now:yyyy-MM-dd}.txt";

        // 完整的日志文件路径：日志目录 + 日志文件名
        private static string LogFilePath => Path.Combine(LogDirectory, LogFileName);

        // ============================================================
        // 写入日志（核心方法）
        // 参数：message - 要记录的日志内容
        // 使用：Logger.Write("相机连接成功");
        // ============================================================
        public static void Write(string message)
        {
            // 使用lock确保线程安全：同一时间只有一个线程能执行这段代码
            lock (_lockObj)
            {
                try
                {
                    // 如果还没初始化，先初始化
                    if (!_isInitialized)
                    {
                        Initialize();
                    }

                    // 如果写入器创建成功，就写入日志
                    if (_streamWriter != null)
                    {
                        // 获取当前时间，格式：年-月-日 时:分:秒
                        string time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                        
                        // 写入一行日志，格式：[时间] 内容
                        _streamWriter.WriteLine($"[{time}] {message}");
                        
                        // 立即刷新到文件（确保日志不会丢失）
                        _streamWriter.Flush();
                    }
                }
                catch (Exception ex)
                {
                    // 如果写入失败，输出到调试窗口（不会影响程序运行）
                    System.Diagnostics.Debug.WriteLine("日志写入失败: " + ex.Message);
                }
            }
        }

        // ============================================================
        // 初始化日志系统（内部方法）
        // 功能：创建logs目录，打开日志文件，写入启动标记
        // ============================================================
        private static void Initialize()
        {
            try
            {
                // 检查logs目录是否存在，如果不存在就创建
                if (!Directory.Exists(LogDirectory))
                {
                    Directory.CreateDirectory(LogDirectory);
                }

                // 创建文件写入器：
                //   参数1：文件路径
                //   参数2：true表示追加模式（不会覆盖已有内容）
                //   参数3：UTF-8编码（支持中文）
                _streamWriter = new StreamWriter(LogFilePath, true, System.Text.Encoding.UTF8);
                
                // 写入空行（分隔不同会话）
                _streamWriter.WriteLine();
                
                // 写入启动标记（方便查找每次启动的日志）
                _streamWriter.WriteLine($"================== 程序启动 [{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ==================");
                
                // 立即刷新到文件
                _streamWriter.Flush();
                
                // 标记为已初始化
                _isInitialized = true;
            }
            catch (Exception ex)
            {
                // 如果初始化失败，输出到调试窗口
                System.Diagnostics.Debug.WriteLine("日志初始化失败: " + ex.Message);
            }
        }

        // ============================================================
        // 关闭日志系统（核心方法）
        // 功能：写入退出标记，关闭文件写入器，释放资源
        // 使用：在程序退出时调用（如MainForm_FormClosing事件）
        // ============================================================
        public static void Close()
        {
            // 使用lock确保线程安全
            lock (_lockObj)
            {
                try
                {
                    // 如果写入器存在，就关闭它
                    if (_streamWriter != null)
                    {
                        // 写入退出标记
                        _streamWriter.WriteLine($"================== 程序退出 [{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ==================");
                        
                        // 写入空行（分隔不同会话）
                        _streamWriter.WriteLine();
                        
                        // 立即刷新到文件
                        _streamWriter.Flush();
                        
                        // 关闭写入器
                        _streamWriter.Close();
                        
                        // 释放资源
                        _streamWriter.Dispose();
                        
                        // 清空引用
                        _streamWriter = null;
                    }
                    
                    // 重置初始化标志
                    _isInitialized = false;
                }
                catch (Exception ex)
                {
                    // 如果关闭失败，输出到调试窗口
                    System.Diagnostics.Debug.WriteLine("日志关闭失败: " + ex.Message);
                }
            }
        }
    }
}