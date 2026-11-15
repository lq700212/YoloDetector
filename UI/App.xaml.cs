using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using YoloDetector.App;
using YoloDetector.Configuration;
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

            // 渲染模式决策必须在首个窗口创建之前完成（进程级设置影响后续所有渲染）。
            // 详见 SoftwareRenderingPolicy：远程串流软件（UU远程/向日葵等）下 WPF 的
            // D3D 高频呈现会触发其"游戏采集"路径，画面出现双指针，切软件渲染规避。
            ApplyRenderingModePolicy();

            // 与 WinForms 版一致的启动时序：主窗口 OnLoaded 的首次 AddLog
            // 触发 Logger 惰性初始化（文件日志写"程序启动"分隔线）。
            // 此处不显式写日志，避免与主窗口的启动提示重复。
            var window = new MainWindow();
            MainWindow = window;
            window.Show();
        }

        /// <summary>
        /// 渲染模式决策（v2.10.3 远程串流双指针修复的接线点）：
        ///   - appsettings.json 的 ForceSoftwareRendering 显式 true/false 直接生效；
        ///   - 缺省 null 时自动检测远程控制软件进程（UU远程/向日葵/ToDesk 等），
        ///     命中即切软件渲染。决策失败不阻断启动（保持默认硬件渲染）。
        /// </summary>
        private static void ApplyRenderingModePolicy()
        {
            try
            {
                bool? configOverride = AppConfig.ForceSoftwareRendering;
                bool forceSoftware = SoftwareRenderingPolicy.ShouldForceSoftwareRendering(
                    configOverride, SoftwareRenderingPolicy.GetRunningProcessNames());

                if (forceSoftware)
                {
                    // 进程级软件渲染：milcore 不再走 D3D，远程软件回到普通桌面采集路径
                    RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
                }
            }
            catch (Exception ex)
            {
                // 渲染模式决策属"优化体验"性质，任何异常都不应阻断启动
                System.Diagnostics.Trace.WriteLine("渲染模式决策失败（按默认硬件渲染继续）: " + ex.Message);
            }
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
