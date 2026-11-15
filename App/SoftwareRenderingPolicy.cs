using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace YoloDetector.App
{
    /// <summary>
    /// 软件渲染策略（v2.10.3 远程串流双指针问题的修复决策）。
    ///
    /// 【问题背景】用户通过网易 UU远程（GameViewer）远程操作本机时，YoloDetector
    /// （WPF，milcore 走 D3D 硬件渲染）一启动，远程画面里就出现两个鼠标指针：
    /// 一个实时跟手（控制端本地指针），一个滞后成残影（远程软件把指针合成进捕获帧）。
    /// 软件关闭后恢复正常；WinForms 时代（GDI 渲染）没有此问题。
    ///
    /// 【根因】远程串流软件检测到被控端出现 D3D 高频呈现进程后，会切换到
    /// "游戏/高性能采集"路径——该路径下画面里的光标由远程软件自行合成进捕获帧
    /// （跟随捕获帧率更新，天然滞后），而控制端同时又绘制一个实时跟手的本地指针，
    /// 于是画面上出现"双指针"。普通桌面采集路径不存在这个问题。
    ///
    /// 【修复】检测到远程控制软件在运行时，把 WPF 切到软件渲染
    /// （RenderOptions.ProcessRenderMode = SoftwareOnly）：milcore 不再走 D3D
    /// 呈现，远程软件回到普通桌面采集路径，双指针消失。
    /// 代价：硬件加速丧失，1080P 预览的合成上屏走 CPU，帧率略降——
    /// 对"检测 + 预览"工具完全可接受，且现场可用配置项覆盖。
    ///
    /// 【判定规则】
    ///   - 配置 ForceSoftwareRendering = true  → 强制软件渲染
    ///   - 配置 ForceSoftwareRendering = false → 强制硬件渲染（现场排查用）
    ///   - 配置 = null（缺省）                 → 自动：枚举进程名，命中已知远程
    ///     控制软件（UU远程/向日葵/ToDesk 等）即启用软件渲染
    ///
    /// 【分层说明】本类依赖 Windows 进程枚举与 WPF 渲染概念，脱离宿主不成立，
    /// 按项目约定留在 App 编排层（不进 Detection 类库）；进程匹配逻辑做成
    /// 纯函数（进程名列表注入），可脱离系统直接单测。
    /// </summary>
    public static class SoftwareRenderingPolicy
    {
        /// <summary>
        /// 已知远程控制/串流软件的进程名（统一小写，不含 .exe 后缀）。
        /// 匹配采用"前缀匹配"：进程名以此列表中任一项开头即命中，
        /// 这样能覆盖同一软件的多个辅助进程（如 GameViewer/Server/Service）。
        /// 新增远程软件支持时在列表追加小写进程名前缀即可。
        /// </summary>
        public static readonly string[] KnownRemoteControlProcessPrefixes = new[]
        {
            "gameviewer",      // 网易 UU远程（本次事故的直接当事软件）
            "sunloginclient",  // 向日葵远程
            "todesk",          // ToDesk 远程
            "rustdesk",        // RustDesk 远程（开源）
            "teamviewer",      // TeamViewer
            "anydesk",         // AnyDesk
            "oray",            // 贝锐全家桶其余组件（OrayRemoteShell 等）
        };

        /// <summary>
        /// 判定是否应强制 WPF 软件渲染。
        /// </summary>
        /// <param name="configOverride">
        /// 配置覆盖值（appsettings.json 的 ForceSoftwareRendering）：
        /// true/false 直接决定结果；null 表示走自动检测。
        /// </param>
        /// <param name="processNames">当前运行中的进程名列表（调用方注入，便于单测）。</param>
        /// <returns>true = 应启用软件渲染；false = 保持默认硬件渲染。</returns>
        public static bool ShouldForceSoftwareRendering(bool? configOverride, IEnumerable<string> processNames)
        {
            // 规则 1/2：配置显式 true/false 时直接生效，不做进程检测
            if (configOverride.HasValue)
            {
                return configOverride.Value;
            }

            // 规则 3：自动检测——进程名规范化（小写、去 .exe）后做前缀匹配
            if (processNames == null)
            {
                return false;
            }

            foreach (string rawName in processNames)
            {
                if (string.IsNullOrEmpty(rawName))
                {
                    continue;
                }

                string name = NormalizeProcessName(rawName);
                foreach (string prefix in KnownRemoteControlProcessPrefixes)
                {
                    if (name.StartsWith(prefix, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// 枚举当前系统的进程名列表（App 启动时调用一次，WPF 接线壳）。
        /// 枚举失败按"无远程环境"处理（返回空列表）：进程枚举出错不应阻断启动。
        /// </summary>
        public static IReadOnlyList<string> GetRunningProcessNames()
        {
            var names = new List<string>();
            try
            {
                foreach (Process process in Process.GetProcesses())
                {
                    using (process)
                    {
                        if (!string.IsNullOrEmpty(process.ProcessName))
                        {
                            names.Add(process.ProcessName);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // 防御兜底：个别受保护进程读取元数据可能异常，逐项失败忽略即可，
                // 整体失败（几乎不可能）按无远程环境处理，不阻断软件启动。
                Trace.WriteLine("远程控制软件进程枚举失败（按无远程环境处理）: " + ex.Message);
            }
            return names;
        }

        /// <summary>
        /// 进程名规范化：小写 + 去掉 .exe 后缀。
        /// 注：Process.GetProcesses 返回的 ProcessName 本身不含 .exe，
        /// 此规范化主要服务单测注入与防御性容错。
        /// </summary>
        public static string NormalizeProcessName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return string.Empty;
            }

            string trimmed = name.Trim();
            if (trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                trimmed = trimmed.Substring(0, trimmed.Length - 4);
            }
            return trimmed.ToLowerInvariant();
        }
    }
}
