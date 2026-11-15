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
            // 顺序：ChromelessWindowConfig 也要关窗口（会触发 Logger.Close），
            // 必须都排在 LoggerTests 之后、且本分区内部保持"配置断言 → 生命周期"次序
            T.Case("UI-无边框窗口WindowChrome配置", ChromelessWindowConfig);
            T.Case("UI-MainWindow构造显示关闭全流程", MainWindowLifecycle);
        }

        /// <summary>
        /// 无边框窗口（WindowStyle=None + WindowChrome）配置回归（v2.10.2 沉淀，
        /// v2.10.3 追加圆角窗口断言）：
        ///   系统标题栏已移除，窗口外壳行为全部压在 WindowChrome 配置上——
        ///   CaptionHeight 被误改会丢"顶栏空白拖动/双击最大化"，ResizeBorderThickness
        ///   被误改会丢边缘缩放；而顶栏交互控件漏配 IsHitTestVisibleInChrome 是
        ///   无边框改造最经典的翻车点：点击输入框/按钮会被 WindowChrome 当成
        ///   "拖动标题栏"吞掉（UIA/InvokePattern 探针测不到，只能靠配置断言锁死）。
        ///   圆角窗口（v2.10.3）：真圆角依赖 AllowsTransparency 分层窗口 +
        ///   透明窗口背景 + 内容 rootFrame 14px 圆角（设计稿 .mock 的
        ///   border-radius:14px），三件套缺一不可——AllowsTransparency 被去掉
        ///   则四角重新变直角，rootFrame 圆角被改则与设计稿不符。
        /// </summary>
        private static void ChromelessWindowConfig()
        {
            EnsureWpfApplication();

            var window = new MainWindow();
            try
            {
                window.Show();
                DoEvents();

                T.True(window.WindowStyle == WindowStyle.None, "WindowStyle 应为 None（无系统标题栏）");

                // 圆角窗口三件套（v2.10.3）：分层窗口 + 透明背景 + 内容圆角框架
                T.True(window.AllowsTransparency,
                       "AllowsTransparency 应=true（真圆角依赖分层窗口，且强制软件渲染避开远程串流 D3D 采集）");
                var bgBrush = window.Background as System.Windows.Media.SolidColorBrush;
                T.True(bgBrush != null && bgBrush.Color.A == 0,
                       "窗口背景应透明（圆角四角透出桌面）");
                var rootFrame = window.FindName("rootFrame") as System.Windows.Controls.Border;
                T.True(rootFrame != null, "圆角框架 rootFrame 应存在");
                if (rootFrame != null)
                {
                    T.True(rootFrame.CornerRadius.TopLeft == 14,
                           "rootFrame 圆角应=14（照设计稿 .mock 的 border-radius:14px）");
                    T.True(rootFrame.Clip is System.Windows.Media.RectangleGeometry,
                           "rootFrame 应挂圆角矩形 Clip（等效设计稿 overflow:hidden，裁光斑溢出）");
                }

                var chrome = System.Windows.Shell.WindowChrome.GetWindowChrome(window);
                T.True(chrome != null, "应挂 WindowChrome（无边框窗口外壳）");
                if (chrome != null)
                {
                    T.True(chrome.CaptionHeight == 62, "CaptionHeight 应=顶栏玻璃条高度 62");
                    T.True(chrome.ResizeBorderThickness.Left == 6, "ResizeBorderThickness 应=6（边缘缩放热区）");
                    T.True(chrome.GlassFrameThickness.Left == 0, "GlassFrameThickness 应=0（无 DWM 玻璃帧）");
                }

                // 标题区内的交互控件必须声明 IsHitTestVisibleInChrome=True，
                // 否则点击被 WindowChrome 转成拖动窗口（logo/状态徽标故意不声明=拖动热区）
                // 窗口钮三键的标记在容器 pnlWindowButtons 上（命中测试沿可视树向上遍历生效）
                foreach (var name in new[] { "txtIp", "txtStreamUrl", "btnTheme", "pnlWindowButtons" })
                {
                    var fe = window.FindName(name) as FrameworkElement;
                    T.True(fe != null, "控件存在: " + name);
                    if (fe != null)
                    {
                        T.True(System.Windows.Shell.WindowChrome.GetIsHitTestVisibleInChrome(fe),
                               "标题区内控件应可命中: " + name);
                    }
                }

                var badge = window.FindName("badgeStatus") as FrameworkElement;
                T.True(badge != null && !System.Windows.Shell.WindowChrome.GetIsHitTestVisibleInChrome(badge),
                       "状态徽标应保持 False（留作标题区拖动热区）");
            }
            finally
            {
                window.Close();
                DoEvents();
            }
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
