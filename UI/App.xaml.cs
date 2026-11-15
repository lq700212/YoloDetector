using System.Windows;
using YoloDetector.Infrastructure.Logging;

namespace YoloDetector.UI
{
    /// <summary>
    /// WPF 应用入口（对应原 WinForms Program.cs）。
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 与 WinForms 版一致的启动时序：主窗口 OnLoaded 的首次 AddLog
            // 触发 Logger 惰性初始化（文件日志写"程序启动"分隔线）。
            // 此处不显式写日志，避免与主窗口的启动提示重复。
            var window = new MainWindow();
            MainWindow = window;
            window.Show();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // 兜底：窗口正常关闭路径在 MainWindow.Window_Closed 里已完成清理
            // （含 Logger.Close）。这里幂等再调一次，防御窗口异常路径漏关文件日志
            // （Logger.Close 内部有 _closed 标志，重复调用不会重复写"程序退出"分隔线）。
            Logger.Close();
            base.OnExit(e);
        }
    }
}
