using System;
using System.Windows.Forms;
using YoloDetector.UI;

namespace YoloDetector
{
    /// <summary>
    /// 程序入口。
    /// </summary>
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            // 启用系统视觉样式与一致的文本渲染
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // 进入主窗体消息循环
            Application.Run(new MainForm());
        }
    }
}
