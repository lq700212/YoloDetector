using System;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using YoloDetector.UI;

namespace YoloDetector.Tests
{
    // ============================================================
    // UI 层进程内冒烟（STA）。
    //
    // 进程级 GUI 冒烟（启动 exe→存活→关窗→退出码）由
    // scripts\Invoke-SmokeTest.ps1 负责；这里补上进程内才能做的：
    // MainWindow 构造/显示/关闭全流程不抛异常（含 App 级主题资源加载）。
    //
    // WPF 版与 WinForms 版的差异：
    //   1. WPF 控件的 StaticResource（样式）与 DynamicResource（画刷）默认解析到
    //      Application.Current.Resources——harness 没有跑 App.xaml，必须先手工
    //      构造 Application 并合并主 exe 的主题字典（显式组件 pack URI），
    //      否则 MainWindow 构造即 XamlParseException；
    //   2. DoEvents 等价物为 Dispatcher.PushFrame（见 DoEvents 方法）；
    //   3. 窗口存活判定用 IsLoaded（WPF 无 IsDisposed）。
    //
    // 顺序要求：本分区必须最后跑——MainWindow.Close 会触发 Logger.Close()，
    // 之后整个进程的文件日志静默失效（Logger 设计如此，防退出期重开句柄）。
    // ============================================================

    internal static class UiSmokeTests
    {
        public static void RunAll()
        {
            T.Case("UI-MainWindow构造显示关闭全流程", MainWindowLifecycle);
        }

        private static void MainWindowLifecycle()
        {
            EnsureWpfApplication();

            Exception threadError = null;
            var window = new MainWindow(); // STA 线程（Main 已标注）

            try
            {
                window.Show();        // 触发 Loaded：日志初始化 + 控件可见性
                DoEvents();
                Thread.Sleep(300);
                DoEvents();

                T.True(window.IsLoaded, "Show 后窗口应存活");
            }
            finally
            {
                try
                {
                    window.Close();   // 触发 Closed：清理链路 + Logger.Close
                    DoEvents();
                }
                catch (Exception ex)
                {
                    threadError = ex; // 关闭链路异常按用例失败处理
                }
            }

            if (threadError != null)
            {
                T.Fail("关闭链路抛异常: " + threadError.GetType().Name + ": " + threadError.Message);
            }
        }

        /// <summary>
        /// 构造进程内 Application 并合并主 exe（YoloDetector 程序集）的主题资源。
        /// 必须用"程序集名;component"形式的显式 pack URI：默认形式解析到入口程序集，
        /// 而本 harness 的入口程序集是 YoloDetector.Tests.exe，没有这些资源。
        /// ShutdownMode 设为显式关闭：防止窗口 Close 后 Dispatcher 关停影响收尾。
        /// </summary>
        private static void EnsureWpfApplication()
        {
            if (Application.Current != null)
            {
                return;
            }

            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/YoloDetector;component/UI/Themes/Palette.Dark.xaml")
            });
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/YoloDetector;component/UI/Themes/GalaxyTheme.xaml")
            });
        }

        /// <summary>WPF 版 DoEvents：推进 Dispatcher 队列（对应 WinForms Application.DoEvents）</summary>
        private static void DoEvents()
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(
                DispatcherPriority.Background,
                new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
        }
    }
}
