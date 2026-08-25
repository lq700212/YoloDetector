using System;
using System.Threading;
using System.Windows.Forms;
using YoloDetector.UI;

namespace YoloDetector.Tests
{
    // ============================================================
    // UI 层进程内冒烟（STA）。
    //
    // 进程级 GUI 冒烟（启动 exe→存活→关窗→退出码）由
    // scripts\Invoke-SmokeTest.ps1 负责；这里补上进程内才能做的：
    // MainForm 构造/显示/关闭全流程不抛异常、SafeBeginInvoke 契约。
    //
    // 顺序要求：本分区必须最后跑——MainForm.Close 会触发 Logger.Close()，
    // 之后整个进程的文件日志静默失效（Logger 设计如此，防退出期重开句柄）。
    // ============================================================

    internal static class UiSmokeTests
    {
        public static void RunAll()
        {
            T.Case("UI-MainForm构造显示关闭全流程", MainFormLifecycle);
        }

        private static void MainFormLifecycle()
        {
            Exception threadError = null;
            var form = new MainForm(); // STA 线程（Main 已标注）

            try
            {
                form.Show();          // 触发 OnShown：日志初始化 + 控件可见性
                Application.DoEvents();
                Thread.Sleep(300);
                Application.DoEvents();

                T.False(form.IsDisposed, "Show 后窗体应存活");

                // SafeBeginInvoke 私有方法无法直测；等价行为经公开路径验证：
                // 关闭过程中不再抛出生命周期类异常即为通过。
            }
            finally
            {
                try
                {
                    form.Close();
                    form.Dispose();
                    Application.DoEvents();
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
    }
}
