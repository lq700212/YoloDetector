using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using OpenCvSharp;
using YoloDetection;

namespace YoloDetector.Tests
{
    // ============================================================
    // 微型测试框架：断言 + 用例组织 + 汇总报告
    //
    // 为什么不用 xUnit/NUnit：本项目坚持"克隆即离线可编译"（依赖全部
    // vendor 入 git），不引入任何需要 NuGet 还原的测试框架。
    // 本框架仅 100 余行，提供：用例分组、异常捕获、断言、超时等待、汇总。
    //
    // 怎么加新用例：
    //   1. 在对应 Tests 文件里写 public static void XxxCase()；
    //   2. 在 Program.Main 的用例清单里登记一行 T.Case("名称", 方法)；
    //   3. 跑 scripts\Run-AllTests.ps1 即自动执行并汇总。
    // ============================================================

    /// <summary>断言与用例运行器（静态类，进程级单次运行）</summary>
    internal static class T
    {
        private static int _pass;
        private static int _fail;
        private static readonly List<string> Failures = new List<string>();
        private static string _currentCase = "";

        /// <summary>当前用例内的断言失败数（供"预期失败也算用例完成"的场景使用）</summary>
        public static int CaseFailCount { get; private set; }

        /// <summary>
        /// 运行一个用例：捕获一切异常（含 Assert 失败），失败不影响后续用例。
        /// </summary>
        public static void Case(string name, Action body)
        {
            _currentCase = name;
            CaseFailCount = 0;
            var sw = Stopwatch.StartNew();
            try
            {
                body();
                sw.Stop();
                if (CaseFailCount == 0)
                {
                    Interlocked.Increment(ref _pass);
                    Report("PASS", name + "  (" + sw.ElapsedMilliseconds + "ms)");
                }
                else
                {
                    Interlocked.Increment(ref _fail);
                    Report("FAIL", name + "  — " + CaseFailCount + " 个断言未通过");
                    Failures.Add(name);
                }
            }
            catch (Exception ex)
            {
                sw.Stop();
                Interlocked.Increment(ref _fail);
                Report("FAIL", name + "  — 未捕获异常: " + ex.GetType().Name + ": " + Trim(ex.Message));
                Failures.Add(name + " [异常] " + ex.GetType().Name + ": " + Trim(ex.Message));
            }
        }

        /// <summary>断言相等。不相等记一次失败并继续本用例后续断言。</summary>
        public static void Eq<T>(T expected, T actual, string what)
        {
            if (!Equals(expected, actual))
            {
                Fail(what + "  期望=[" + expected + "] 实际=[" + actual + "]");
            }
        }

        /// <summary>断言为真。</summary>
        public static void True(bool cond, string what)
        {
            if (!cond) Fail(what);
        }

        /// <summary>断言为假。</summary>
        public static void False(bool cond, string what) => True(!cond, what);

        /// <summary>断言 action 抛出且仅抛出 TEx 类型异常。</summary>
        public static void Throws<TEx>(Action action, string what) where TEx : Exception
        {
            try
            {
                action();
                Fail(what + "  — 未抛出预期的 " + typeof(TEx).Name);
            }
            catch (TEx)
            {
                // 预期行为
            }
            catch (Exception ex)
            {
                Fail(what + "  — 抛出了错误类型 " + ex.GetType().Name + "（期望 " + typeof(TEx).Name + "）");
            }
        }

        /// <summary>
        /// 有界等待条件成立（轮询，10ms 步进）。
        /// 用于后台线程事件到达类断言——绝不在测试里无限阻塞。
        /// </summary>
        public static bool WaitFor(Func<bool> condition, int timeoutMs)
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                if (condition()) return true;
                Thread.Sleep(10);
            }
            return condition();
        }

        /// <summary>记录一个失败（继续执行后续断言）。</summary>
        public static void Fail(string message)
        {
            CaseFailCount++;
            Report("  ✗", "[" + _currentCase + "] " + message);
            Failures.Add("[" + _currentCase + "] " + message);
        }

        /// <summary>输出进度信息（不属于断言）。</summary>
        public static void Info(string message) => Report("  ·", message);

        /// <summary>打印汇总并返回失败数（作为进程退出码）。</summary>
        public static int Finish()
        {
            Console.WriteLine();
            Console.WriteLine("============================================================");
            Console.WriteLine(("  汇总: PASS=" + _pass + "  FAIL=" + _fail).PadLeft(40));
            Console.WriteLine("============================================================");
            if (_fail > 0)
            {
                Console.WriteLine("失败明细:");
                foreach (var f in Failures)
                {
                    Console.WriteLine("  - " + f);
                }
            }
            return _fail;
        }

        private static void Report(string tag, string message)
        {
            Console.WriteLine(tag.PadRight(5) + message);
        }

        private static string Trim(string s)
        {
            s = (s ?? "").Replace("\r", " ").Replace("\n", " ");
            return s.Length <= 120 ? s : s.Substring(0, 120) + "...";
        }
    }

    /// <summary>
    /// 测试入口（STAThread：MainForm 构造冒烟需要 STA 线程）。
    /// 用例按分区顺序执行；配置类有静态状态的放最前并在自身内部恢复现场。
    /// </summary>
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            Console.WriteLine("YoloDetector 全量回归 harness");
            Console.WriteLine("运行目录: " + AppDomain.CurrentDomain.BaseDirectory);
            Console.WriteLine();

            ConfigTests.RunAll();               // 配置层（会临时污染静态配置，内部自行恢复）
            MatExtensionsTests.RunAll();        // Mat↔SKBitmap 像素级无损
            SkBitmapExtensionTests.RunAll();    // 宿主边界 SKBitmap→Drawing.Bitmap（含 Bgra8888 错位回归防线）
            ProcessorTests.RunAll();            // 后处理器行为红线
            VisualizerTests.RunAll();           // 可视化器契约
            DetectorTests.RunAll();             // YoloV26Detector 真实模型推理契约
            PoseTests.RunAll();                 // 姿态检测器契约 + bus真图端到端(检人→姿态→手腕)
            EsdAnalyzerTests.RunAll();          // 静电接触状态机(虚拟时钟) + 管道ESD旁路集成
            PipelineTests.RunAll();             // 检测管道线程协议（fake detector）
            FrameSourceTests.RunAll();          // 帧源生命周期（文件流/不可达地址）
            EndToEndTests.RunAll();             // 控制器端到端（真模型+视频文件流）
            CameraControllerTests.RunAll();     // 相机控制器与工厂
            AngehuaClientTests.RunAll();        // 安格华客户端契约 + DeviceStatus 计算
            LogManagerTests.RunAll();           // 日志门面开关（复位全局状态）
            LoggerTests.RunAll();               // 文件日志契约（Close 后进程内日志静默，须在 UI 冒烟前）
            RoiSelectionTests.RunAll();         // ROI 拖拽标定纯逻辑（Zoom 坐标换算 + 框选状态机）
            UiSmokeTests.RunAll();              // MainForm 构造/显示/关闭冒烟

            int fails = T.Finish();

            // UI 冒烟之后统一收尾：确保日志系统关闭（MainForm.Close 已做，
            // 但若该用例被跳过也兜底一次），避免句柄残留
            Infrastructure.Logging.Logger.Close();
            return fails;
        }
    }

    // ============================================================
    // 测试辅助类型
    // ============================================================

    /// <summary>
    /// 可编程假检测器：替代真实 ONNX 推理，用于验证管道线程协议。
    /// DetectImpl 可注入固定结果或抛异常，DetectCalls 记录调用次数。
    /// </summary>
    internal class FakeDetector : IYoloDetector
    {
        public bool InitializedFlag;

        /// <summary>Detect 的行为注入点（默认返回空列表）</summary>
        public Func<Mat, List<DetectionResult>> DetectImpl = mat => new List<DetectionResult>();

        public int DetectCalls;
        public int DisposeCalls;

        public bool IsInitialized => InitializedFlag;

        public float ConfidenceThreshold { get; set; }

        public float NmsThreshold { get; set; }

        public void Initialize(string modelPath)
        {
            InitializedFlag = true;
        }

        public List<DetectionResult> Detect(Mat mat)
        {
            DetectCalls++;
            return DetectImpl(mat);
        }

        public void Dispose()
        {
            DisposeCalls++;
        }

        /// <summary>造一个指定参数的检测结果（中心点+尺寸）</summary>
        public static DetectionResult Box(float x, float y, float w, float h, float conf = 0.9f, int cls = 0)
        {
            return new DetectionResult
            {
                ClassId = cls,
                ClassName = cls == 0 ? "person" : "class_" + cls,
                Confidence = conf,
                X = x, Y = y, Width = w, Height = h
            };
        }
    }

    /// <summary>什么都不画的空可视化器（管道测试用，避免绘制开销干扰时序）</summary>
    internal class NullVisualizer : IDetectionVisualizer
    {
        public Mat Draw(Mat frame, List<DetectionResult> results)
        {
            return frame == null || frame.Empty() ? null : frame.Clone();
        }
    }

    /// <summary>通用工具（随机图生成、像素比对、合成人形图等）</summary>
    internal static class TestUtil
    {
        private static readonly Random Rng = new Random(20260825);

        /// <summary>生成随机噪声 BGR 图（往返无损测试用：噪声保证每个字节都有区分度）</summary>
        public static Mat RandomBgrMat(int width, int height)
        {
            var mat = new Mat(height, width, MatType.CV_8UC3);
            byte[] data = new byte[width * height * 3];
            Rng.NextBytes(data);
            System.Runtime.InteropServices.Marshal.Copy(data, 0, mat.Data, data.Length);
            return mat;
        }

        /// <summary>
        /// 两张同尺寸 BGR 图逐像素比对，返回差异字节数（0=完全一致）。
        /// 注意：CountNonZero 仅支持单通道，多通道必须 Split 后分通道统计，
        /// 直接对 3 通道结果调用会抛 OpenCVException(cn == 1)。
        /// </summary>
        public static long DiffBytes(Mat a, Mat b)
        {
            if (a.Cols != b.Cols || a.Rows != b.Rows || a.Channels() != b.Channels())
                return long.MinValue; // 尺寸不同视为最大差异

            using (var diff = new Mat())
            {
                Cv2.Absdiff(a, b, diff);

                if (diff.Channels() == 1)
                {
                    return Cv2.CountNonZero(diff);
                }

                long total = 0;
                Mat[] planes = Cv2.Split(diff);
                try
                {
                    foreach (var plane in planes)
                    {
                        total += Cv2.CountNonZero(plane);
                    }
                }
                finally
                {
                    foreach (var plane in planes)
                    {
                        plane.Dispose();
                    }
                }
                return total;
            }
        }

        /// <summary>
        /// 合成"人形"测试图：深色背景 + 肤色圆头 + 深蓝躯干矩形 + 双腿。
        /// 用于真实模型的冒烟推理（不强制检出，只验证流程稳定、输出坐标合法）。
        /// </summary>
        public static Mat SyntheticPersonMat(int width = 640, int height = 480)
        {
            var mat = new Mat(height, width, MatType.CV_8UC3, new Scalar(40, 60, 50));
            int cx = width / 2;
            Cv2.Circle(mat, cx, height / 4, Math.Min(width, height) / 12, new Scalar(120, 160, 220), -1);
            Cv2.Rectangle(mat,
                new Rect(cx - width / 16, height / 3, width / 8, height / 3),
                new Scalar(140, 90, 40), -1);
            Cv2.Rectangle(mat,
                new Rect(cx - width / 20, height * 2 / 3 - 10, width / 24, height / 3),
                new Scalar(30, 30, 80), -1);
            Cv2.Rectangle(mat,
                new Rect(cx + width / 60, height * 2 / 3 - 10, width / 24, height / 3),
                new Scalar(30, 30, 80), -1);
            return mat;
        }

        /// <summary>主程序 bin 目录下的绝对路径拼接（模型等资源定位用）</summary>
        public static string BinPath(params string[] parts)
        {
            string path = AppDomain.CurrentDomain.BaseDirectory;
            foreach (var p in parts)
            {
                path = System.IO.Path.Combine(path, p);
            }
            return path;
        }
    }
}
