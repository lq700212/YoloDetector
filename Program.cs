using System;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Win32;

namespace YoloDetector
{
    // ============================================================
    // 程序入口类
    // 功能：这是整个程序的起点，负责启动WinForms应用
    // 说明：所有C#程序都必须有一个Program类和Main方法，这是程序的入口
    // ============================================================
    static class Program
    {
        // ============================================================
        // [STAThread] 标记（必须添加）
        // 说明：WinForms应用必须加上这个标记，告诉系统使用单线程模型
        //       如果不加，程序可能会出现各种奇怪的问题
        // ============================================================
        [STAThread]
        
        // ============================================================
        // Main方法（程序入口点）
        // 功能：程序运行时第一个执行的方法，负责初始化并启动主窗口
        // ============================================================
        static void Main()
        {
            // 设置WebBrowser控件使用IE11/Edge模式
            // 说明：WinForms的WebBrowser控件默认使用IE7内核，无法支持现代JS（如flv.js）
            //       通过注册表设置FEATURE_BROWSER_EMULATION为11001，强制使用IE11模式
            //       这样才能正常加载相机的draw.html页面（使用flv.js播放WebSocket FLV流）
            SetWebBrowserEmulationMode();
            
            // 启用视觉样式
            // 说明：让界面使用Windows系统的主题样式，看起来更美观
            //       如果不调用这个方法，界面会使用老式的Windows风格
            Application.EnableVisualStyles();
            
            // 设置兼容文本渲染
            // 说明：确保在不同版本的Windows系统上字体显示一致
            //       特别是在高DPI显示器上能正常显示
            Application.SetCompatibleTextRenderingDefault(false);
            
            // 启动主窗体
            // 说明：创建MainForm实例并启动消息循环，显示主界面
            //       这行代码执行后，程序就进入了事件驱动模式
            //       用户操作（点击按钮、输入文字等）都会触发相应的事件
            Application.Run(new MainForm());
        }
        
        // ============================================================
        // 设置WebBrowser控件的IE版本模拟
        // 说明：修改注册表，强制WebBrowser使用IE11模式
        //       这是解决flv.js等现代JS库无法在WebBrowser中运行的关键
        // ============================================================
        private static void SetWebBrowserEmulationMode()
        {
            try
            {
                // 获取当前程序的文件名（如 CameraDemo.exe）
                string appName = Assembly.GetExecutingAssembly().GetName().Name + ".exe";
                
                // 注册表路径：HKEY_CURRENT_USER\Software\Microsoft\Internet Explorer\Main\FeatureControl\FEATURE_BROWSER_EMULATION
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(
                    @"Software\Microsoft\Internet Explorer\Main\FeatureControl\FEATURE_BROWSER_EMULATION"))
                {
                    // 设置值为11001，表示使用IE11 Edge模式
                    // 11001 = IE11，强制使用Edge渲染模式
                    // 11000 = IE11，默认模式
                    // 10001 = IE10，以此类推
                    key.SetValue(appName, 11001, RegistryValueKind.DWord);
                }
                
                System.Diagnostics.Debug.WriteLine("WebBrowser IE11模式设置成功");
            }
            catch (Exception ex)
            {
                // 设置失败不影响程序运行，只是WebBrowser可能无法正常显示视频
                System.Diagnostics.Debug.WriteLine("WebBrowser IE11模式设置失败: " + ex.Message);
            }
        }
    }
}