using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using SkiaSharp;
using YoloDetection;
using YoloDetector.App;
using YoloDetector.Cameras;
using YoloDetector.Configuration;
using YoloDetector.Infrastructure.Logging;

namespace YoloDetector.UI
{
    /// <summary>
    /// 主窗口（纯视图层，玻璃拟态 Glass Aurora 版）。
    ///
    /// 职责边界（与 WinForms 版 MainForm 一致）：
    ///   - 只负责界面交互与结果显示
    ///   - 相机连接管理委托给 CameraController
    ///   - 视频检测编排委托给 VideoDetectionController（UI 层不接触任何 Mat/OpenCV 对象）
    ///
    /// 后台回调安全约定（WPF 版对应物）：
    ///   控制器的回调在后台线程触发，本窗口所有回调入口统一经过
    ///   SafeDispatcherInvoke 保护（Dispatcher 关停检查 + 异常兜底），
    ///   调度失败时调用方就地释放随行资源（如 SKBitmap 帧），杜绝关闭窗口瞬间的崩溃与泄漏。
    ///
    /// 预览显示约定：
    ///   previewSink 交出 SKBitmap 所有权 → 本窗口在 UI 线程经
    ///   SkBitmapWpfExtensions.TryWriteTo 整块写入复用的 WriteableBitmap
    ///   （分辨率不变时跨帧零大对象分配），SKBitmap 用完立即 Dispose。
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly CameraController _cameraController = new CameraController();
        private VideoDetectionController _videoController;

        private DispatcherTimer _statusTimer;

        // 最近一次检测结果快照（不可变副本，供扩展使用）
        private volatile List<DetectionResult> _lastDetections = new List<DetectionResult>();
        private long _detectionCount;

        /// <summary>预览画面拖拽框选的标定目标（决定 RoiSelected 结果分发给哪个检测功能）</summary>
        private CalibTarget _calibTarget = CalibTarget.EsdPole;

        /// <summary>标定目标枚举</summary>
        private enum CalibTarget { EsdPole, Door }

        /// <summary>当前通道号（对应 WinForms 版 numChannel.Value，0 ~ Stream.MaxChannel）</summary>
        private int _channel;

        /// <summary>预览显示位图（复用：分辨率不变时跨帧只 WritePixels，零大对象分配）</summary>
        private WriteableBitmap _previewBitmap;

        /// <summary>当前是否深色主题（初始深色，与设计稿默认一致）</summary>
        private bool _isDarkTheme = true;

        // 日志行数计数（WPF TextBox 无 WinForms 的 Lines 属性，自行计数驱动 500 行裁剪）
        private int _logLineCount;
        private const int MaxLogLines = 500;

        public MainWindow()
        {
            InitializeComponent();

            // 输入框初值（与 WinForms 版 InitializeComponent 一致：来自运行配置）
            txtIp.Text = AppConfig.Current.Connection.DefaultIp;
            txtStreamUrl.Text = AppConfig.Current.Stream.GetRtspUrl(
                AppConfig.Current.Connection.DefaultIp, 0);
        }

        /// <summary>窗口加载（对应 WinForms OnShown：句柄/界面就绪后再记录启动提示）</summary>
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // 状态轮询定时器（DispatcherTimer 走 UI 线程消息循环，对应 WinForms Timer）
            _statusTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(AppConfig.Current.Preview.StatusRefreshIntervalMs)
            };
            _statusTimer.Tick += StatusTimer_Tick;

            // ROI 拖拽标定：控件内部已封装鼠标接线/虚线框绘制/坐标换算，
            // 这里只订阅结果（归一化 ROI → 按当前标定目标分发：静电杆/门区域）。
            // 控件与窗口同生命周期，事件无需成对退订（窗口销毁即随链断开）
            roiImage.RoiSelected += roi =>
            {
                if (_calibTarget == CalibTarget.Door)
                {
                    ApplyDoorRoiSelection(roi.X, roi.Y, roi.W, roi.H);
                }
                else
                {
                    ApplyEsdRoiSelection(roi.X, roi.Y, roi.W, roi.H);
                }
            };

            // 启动提示（与 WinForms 版一致；首次 AddLog 触发 Logger 惰性初始化，
            // 文件日志写入"程序启动"分隔线——GUI 冒烟脚本的配对断言依赖此行为）
            AddLog("程序已启动，请输入相机IP地址并点击【连接相机】");
            AddLog("提示：连接成功后点击【开始预览】查看摄像头画面");
            AddLog("提示：预览时可在画面上按住鼠标左键拖拽，框选静电杆检测区域（立即生效并保存）");
        }

        // ============================================================
        // 日志
        // ============================================================

        /// <summary>追加一条操作日志（写入文件 + UI 面板，任意线程可调用）</summary>
        private void AddLog(string message)
        {
            Logger.Write(message);

            // 无法送达UI时（窗口关闭中）仅记录到文件，无需额外处理
            SafeDispatcherInvoke(() => AppendLogToPanel(message));
        }

        private void AppendLogToPanel(string message)
        {
            if (txtLog == null)
            {
                return;
            }

            string time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            // 智能跟随：仅当滚动条已在底部时才自动滚到新行，
            // 用户上翻历史日志时不会被强行拉回（WinForms 版滚动位置保护的同款语义）
            bool atBottom = txtLog.ExtentHeight <= 0 ||
                (txtLog.VerticalOffset + txtLog.ViewportHeight) >= txtLog.ExtentHeight - 1.0;

            txtLog.AppendText("[" + time + "] " + message + Environment.NewLine);
            _logLineCount++;

            // 行数超限时裁掉前一半：AppendText 对长文本是 O(n) 操作且 TextBox
            // 保留全部历史会持续吃内存，现场长时间运行必须限制（阈值 500 行）。
            // WPF 无 Lines 属性，按换行切分；只在跨过阈值时执行（每 250 行一次），开销可忽略
            if (_logLineCount > MaxLogLines)
            {
                var lines = txtLog.Text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                int cut = lines.Length - MaxLogLines / 2;
                var kept = new string[lines.Length - cut];
                Array.Copy(lines, cut, kept, 0, kept.Length);
                txtLog.Text = string.Join(Environment.NewLine, kept) + Environment.NewLine;
                _logLineCount = kept.Length;
            }

            if (atBottom)
            {
                txtLog.ScrollToEnd();
            }
        }

        // ============================================================
        // 相机连接
        // ============================================================

        private async void btnConnect_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await ConnectOrDisconnectAsync();
            }
            catch (Exception ex)
            {
                // async void 事件处理器的兜底：任何异常都不允许逃逸出 UI 消息循环
                UpdateConnectionStatus(_cameraController.IsConnected);
                AddLog("连接异常: " + ex.Message);
                MessageBox.Show("连接异常: " + ex.Message, "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task ConnectOrDisconnectAsync()
        {
            // 已连接则断开
            if (_cameraController.IsConnected)
            {
                StopVideoPreview();
                _cameraController.Disconnect();
                _statusTimer.Stop();
                UpdateConnectionStatus(false);
                AddLog("已断开相机连接");
                return;
            }

            string ip = txtIp != null ? txtIp.Text.Trim() : string.Empty;

            if (string.IsNullOrEmpty(ip))
            {
                MessageBox.Show("请输入相机IP地址！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!IPAddress.TryParse(ip, out _))
            {
                MessageBox.Show("请输入有效的IP地址！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            SetConnectingUi(true);
            AddLog("正在连接相机 " + ip + "...");

            try
            {
                bool connected = await _cameraController.ConnectAsync(ip);

                if (connected)
                {
                    UpdateConnectionStatus(true);
                    AddLog("相机 " + ip + " 连接成功！");
                    _statusTimer.Start();

                    await RefreshDeviceStatusAsync(showBusyLog: false);

                    if (txtStreamUrl != null)
                    {
                        txtStreamUrl.Text = _cameraController.BuildStreamUrl(GetChannel(), ip);
                    }
                }
                else
                {
                    UpdateConnectionStatus(false);
                    AddLog("相机 " + ip + " 连接失败，请检查网络连接和IP地址");
                    MessageBox.Show("连接失败，请检查网络连接和IP地址！", "错误",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                UpdateConnectionStatus(false);
                AddLog("连接异常: " + ex.Message);
                MessageBox.Show("连接异常: " + ex.Message, "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                SetConnectingUi(false);
            }
        }

        private void SetConnectingUi(bool connecting)
        {
            btnConnect.IsEnabled = !connecting;
            btnConnect.Content = connecting
                ? "连接中..."
                : (_cameraController.IsConnected ? "⏻ 断开连接" : "⏻ 连接相机");
        }

        /// <summary>
        /// 相机状态徽标：已连接 = 绿点 + 徽标绿字；未连接 = 红点 + 次要灰字。
        /// WPF 用 SetResourceReference 做主题画刷的动态切换（跟随深浅主题自动更新）
        /// </summary>
        private void UpdateConnectionStatus(bool connected)
        {
            if (lblStatus == null || btnConnect == null) return;

            lblStatus.Text = connected ? "已连接" : "未连接";
            badgeDot.SetResourceReference(Shape.FillProperty,
                connected ? "SuccessBrush" : "DangerBrush");
            lblStatus.SetResourceReference(TextBlock.ForegroundProperty,
                connected ? "BadgeOkTextBrush" : "TextSubBrush");
            // 徽标底色：已连接 = 徽标绿底；未连接 = 中性 ghost 底
            //（绿底配红点不协调——玻璃徽标语义：底色随状态同步切换）
            badgeStatus.SetResourceReference(Border.BackgroundProperty,
                connected ? "BadgeOkBgBrush" : "GhostBgBrush");
            btnConnect.Content = connected ? "⏻ 断开连接" : "⏻ 连接相机";
        }

        // ============================================================
        // 拉流/推流控制
        // ============================================================

        private async void btnStartRtsp_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await SetRtspEnableAsync(true);
            }
            catch (Exception ex)
            {
                AddLog("操作异常: " + ex.Message);
            }
        }

        private async Task SetRtspEnableAsync(bool enable)
        {
            if (!EnsureCameraConnected()) return;

            int channel = GetChannel();
            AddLog((enable ? "开启" : "关闭") + "通道 " + channel + " 拉流...");

            try
            {
                bool success = await _cameraController.SetRtspAsync(channel, enable);
                AddLog(success
                    ? (enable ? "开启" : "关闭") + "通道 " + channel + " 拉流成功！"
                    : (enable ? "开启" : "关闭") + "通道 " + channel + " 拉流失败");

                await RefreshDeviceStatusAsync(showBusyLog: false);
            }
            catch (Exception ex)
            {
                AddLog("操作异常: " + ex.Message);
            }
        }

        private async void btnStartRtmp_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!EnsureCameraConnected()) return;

                int channel = GetChannel();
                AddLog("开启通道 " + channel + " 推流...");

                string rtmpUrl = txtStreamUrl != null ? txtStreamUrl.Text.Trim() : string.Empty;
                bool success = await _cameraController.SetRtmpAsync(channel, rtmpUrl, enable: true);
                AddLog(success ? "开启推流成功！" : "推流失败");

                await RefreshDeviceStatusAsync(showBusyLog: false);
            }
            catch (Exception ex)
            {
                AddLog("操作异常: " + ex.Message);
            }
        }

        private bool EnsureCameraConnected()
        {
            if (_cameraController.IsConnected) return true;

            MessageBox.Show("请先连接相机！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        private int GetChannel()
        {
            return _channel; // 范围已由 stepper 按钮约束（0 ~ Stream.MaxChannel）
        }

        private void btnChannelUp_Click(object sender, RoutedEventArgs e)
        {
            if (_channel < AppConfig.Current.Stream.MaxChannel)
            {
                _channel++;
                SyncChannelText();
            }
        }

        private void btnChannelDown_Click(object sender, RoutedEventArgs e)
        {
            if (_channel > 0)
            {
                _channel--;
                SyncChannelText();
            }
        }

        /// <summary>同步两处 stepper 显示（顶栏 + 左栏"通道与标定"卡，设计稿两处都有入口）</summary>
        private void SyncChannelText()
        {
            string text = _channel.ToString();
            txtChannel.Text = text;
            txtChannelTop.Text = text;
        }

        // ============================================================
        // 设备状态刷新（防重入保护位于 CameraController 内部）
        // ============================================================

        private async void StatusTimer_Tick(object sender, EventArgs e)
        {
            if (_cameraController.IsConnected)
            {
                await RefreshDeviceStatusAsync(showBusyLog: false);
            }
        }

        private async Task RefreshDeviceStatusAsync(bool showBusyLog)
        {
            if (!_cameraController.IsConnected) return;

            if (showBusyLog)
            {
                AddLog("正在获取设备状态...");
            }

            try
            {
                DeviceStatus status = await _cameraController.TryGetStatusAsync();

                // null = 未连接或上一轮查询未完成（跳过本轮，非错误）
                if (status == null) return;

                string text = BuildStatusText(status);
                SafeDispatcherInvoke(() =>
                {
                    if (txtStatusInfo != null)
                    {
                        txtStatusInfo.Text = text;
                    }
                });
            }
            catch (Exception ex)
            {
                AddLog("获取状态异常: " + ex.Message);
            }
        }

        private static string BuildStatusText(DeviceStatus status)
        {
            var sb = new StringBuilder();
            sb.AppendLine("IP地址:   " + status.IpAddress);
            sb.AppendLine("品牌:     " + status.Brand);
            sb.AppendLine("CPU:      " + status.CpuUsage.ToString("F1") + "%");
            sb.AppendLine("内存:     " + status.MemoryUsage.ToString("F1") + "%");
            sb.AppendLine("磁盘总量: " + status.GetFormattedDiskTotal());
            sb.AppendLine("磁盘可用: " + status.GetFormattedDiskFree());
            sb.AppendLine("使用率:   " + status.GetDiskUsage().ToString("F1") + "%");
            sb.AppendLine("录像总数: " + status.TotalVideoCount);
            return sb.ToString();
        }

        // ============================================================
        // 视频预览与YOLO检测
        // ============================================================

        private void btnStartPreview_Click(object sender, RoutedEventArgs e)
        {
            StartVideoPreview();
        }

        private void btnStopPreview_Click(object sender, RoutedEventArgs e)
        {
            StopVideoPreview();
            AddLog("================== 预览流程结束 ==================");
        }

        private void StartVideoPreview()
        {
            if (!EnsureCameraConnected()) return;

            try
            {
                string rtspUrl = txtStreamUrl != null ? txtStreamUrl.Text.Trim() : string.Empty;
                if (string.IsNullOrEmpty(rtspUrl))
                {
                    rtspUrl = _cameraController.BuildStreamUrl(GetChannel(), GetCurrentIp());
                }

                AddLog("正在启动视频预览...");
                AddLog("RTSP流地址: " + rtspUrl);

                // 静电杆触摸检测：打印本次预览实际加载的标定值。
                // 现场"第一次打开摸杆无反应"时先看这行——确认加载的 ROI 与实际杆位置
                // 是否对得上（此前启动不打印 ROI，无法区分"没加载"和"位置偏差"）
                if (AppConfig.Esd.Enabled)
                {
                    var esd = AppConfig.Esd;
                    AddLog(string.Format(
                        "静电杆触摸检测已启用: ROI=X={0:F3} Y={1:F3} W={2:F3} H={3:F3} 容差={4:F0}px 持续={5:F0}ms（预览中拖拽框选可实时调整）",
                        esd.RoiX, esd.RoiY, esd.RoiW, esd.RoiH, esd.MarginPx, esd.HoldDurationMs));
                }
                else
                {
                    AddLog("静电杆触摸检测未启用（esdConfig.json Enabled=false）");
                }

                EnsureVideoController();

                var options = new DetectionStartupOptions
                {
                    ModelPath = ExpandModelPath(AppConfig.Yolo.ModelPath),
                    ConfidenceThreshold = AppConfig.Yolo.ConfidenceThreshold,
                    NmsThreshold = AppConfig.Yolo.NmsThreshold,
                    YoloDebugLog = AppConfig.Yolo.YoloDebugLog,
                    DetectionResultLog = AppConfig.Yolo.DetectionResultLog,
                    VisualizerType = ParseVisualizerType(AppConfig.Yolo.VisualizerType),
                    RtspUrl = rtspUrl,
                    // 注入 UI 日志回调：检测线程的异常/过程日志同步显示到界面面板，
                    // 否则现场排查"为什么检测不到"只能翻日志文件
                    LogSink = msg => SafeDispatcherInvoke(() => AppendLogToPanel(msg)),
                    // 静电杆触摸检测旁路：模型/参数从 esdConfig.json 来；
                    // Enabled=false 时传 null 等价于关闭，管道零开销
                    PoseModelPath = AppConfig.Esd.Enabled
                        ? ExpandModelPath(AppConfig.Esd.PoseModelPath)
                        : null,
                    EsdOptions = AppConfig.Esd.Enabled ? AppConfig.Esd.ToOptions() : null,
                    DoorOptions = AppConfig.Door.Enabled ? AppConfig.Door.ToOptions() : null
                };

                _videoController.Start(options);

                // 隐藏占位提示 + 显示视频角标（本次预览的实际流地址与通道）
                lblVideoTitle.Visibility = Visibility.Collapsed;
                lblStreamBadge.Text = rtspUrl;
                lblChannelBadge.Text = "通道 " + GetChannel();
                badgeStream.Visibility = Visibility.Visible;
                badgeChannel.Visibility = Visibility.Visible;

                AddLog("YOLO检测已启动");
            }
            catch (System.IO.FileNotFoundException fnfEx)
            {
                AddLog("YOLO模型文件不存在: " + fnfEx.FileName);
                MessageBox.Show("YOLO模型文件不存在: " + fnfEx.FileName, "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                AddLog("视频预览启动失败: " + ex.Message);
                MessageBox.Show(
                    "视频预览启动失败！\n\n请检查：\n1. RTSP流地址是否正确\n2. 相机是否开启视频输出\n3. 网络连接是否正常\n\n详细信息: " + ex.Message,
                    "视频预览失败", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void StopVideoPreview()
        {
            if (_videoController != null && _videoController.IsRunning)
            {
                _videoController.Stop();
                AddLog("YOLO检测已停止");
            }

            if (lblVideoTitle != null)
            {
                lblVideoTitle.Visibility = Visibility.Visible;
                badgeStream.Visibility = Visibility.Collapsed;
                badgeChannel.Visibility = Visibility.Collapsed;
            }
        }

        private void EnsureVideoController()
        {
            if (_videoController != null) return;

            _videoController = new VideoDetectionController(
                previewSink: OnPreviewFrameReceived,
                detectionSink: OnDetectionsReceived);

            // 静电触摸状态翻转 → 日志面板提示（仅在事件帧触发一次，不会刷屏）。
            // 回调在检测线程触发，经 SafeDispatcherInvoke 调度回 UI 线程。
            // 结束事件带"本次持续时长"（分析器在清零前记录的最终值）：
            // Hold=400ms 下拍一下(0.3~0.5秒)与长摸(数秒)都触发同一事件，
            // 现场靠这个时长区分动作类型、审计误判。
            _videoController.EsdContactChanged += (s, e) =>
            {
                string msg = e.InContact
                    ? $"⚡人员#{e.TrackId} 正在触摸静电杆 (持续{e.ContactElapsedMs / 1000.0:F1}秒)"
                    : $"人员#{e.TrackId} 结束触摸静电杆 (本次持续{e.ContactElapsedMs / 1000.0:F1}秒)";
                SafeDispatcherInvoke(() => AddLog(msg));
            };

            // 门状态翻转 → 日志提示（分析器内部已防抖，仅翻转帧触发一次）
            _videoController.DoorStateChanged += (s, isOpen) =>
            {
                string msg = isOpen
                    ? "⚠操作间门被打开（请确认是否合规操作）"
                    : "✓操作间门已关闭";
                SafeDispatcherInvoke(() => AddLog(msg));
            };
        }

        private static string ExpandModelPath(string modelPath)
        {
            if (string.IsNullOrEmpty(modelPath))
            {
                modelPath = "Detection/model/yolo26n.onnx";
            }

            if (System.IO.Path.IsPathRooted(modelPath))
            {
                return modelPath;
            }

            return System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, modelPath);
        }

        private static VisualizerType ParseVisualizerType(string value)
        {
            return string.Equals(value, "OpenCV", StringComparison.OrdinalIgnoreCase)
                ? VisualizerType.OpenCV
                : VisualizerType.YoloBuiltin;
        }

        private string GetCurrentIp()
        {
            string ip = txtIp != null ? txtIp.Text.Trim() : string.Empty;
            return string.IsNullOrEmpty(ip) ? AppConfig.Current.Connection.DefaultIp : ip;
        }

        // ============================================================
        // 控制器回调（后台线程触发，经安全调度后更新UI）
        // ============================================================

        /// <summary>
        /// 预览帧回调：SKBitmap 所有权移交本方法。
        /// 经 SafeDispatcherInvoke 调度到 UI 线程后写入显示位图；
        /// 调度失败时立即释放防止泄漏。
        /// </summary>
        private void OnPreviewFrameReceived(SKBitmap frame)
        {
            if (!SafeDispatcherInvoke(() => ShowPreviewFrame(frame)))
            {
                frame.Dispose(); // 无法送达UI（如窗口正在关闭），就地释放
            }
        }

        /// <summary>
        /// 显示预览帧（UI 线程）：整块写入复用的 WriteableBitmap；
        /// 分辨率变化（含首帧）时按帧规格重建。SKBitmap 用完立即 Dispose。
        /// </summary>
        private void ShowPreviewFrame(SKBitmap frame)
        {
            try
            {
                if (roiImage == null)
                {
                    return;
                }

                if (_previewBitmap == null ||
                    _previewBitmap.PixelWidth != frame.Width ||
                    _previewBitmap.PixelHeight != frame.Height)
                {
                    var bmp = frame.CreateMatchingWriteableBitmap();
                    if (bmp == null)
                    {
                        return; // 格式不受支持（理论上不会发生：类库固定输出 Bgra8888）
                    }

                    _previewBitmap = bmp; // 旧位图无 Dispose 语义，随 GC 回收
                    roiImage.Source = _previewBitmap;
                }

                frame.TryWriteTo(_previewBitmap); // 整块拷贝（Bgra8888→Bgra32 直接对齐）
            }
            finally
            {
                frame.Dispose();
            }
        }

        // ============================================================
        // 静电杆 ROI 拖拽标定（预览画面上框选静电杆/门区域）
        //
        // 交互封装在 RoiSelectionImage 控件内（鼠标/绘制/坐标换算），
        // 本窗口只消费结果：热更新运行链路 + 配置落盘。
        // ============================================================

        /// <summary>
        /// 应用标定结果：先热更新运行链路（若 ESD 已启用），再把配置落盘。
        /// 两步都失败也不抛异常——标定是辅助操作，日志说明结果即可。
        /// </summary>
        private void ApplyEsdRoiSelection(float roiX, float roiY, float roiW, float roiH)
        {
            bool liveApplied = _videoController != null && _videoController.TryUpdateEsdRoi(roiX, roiY, roiW, roiH);

            AppConfig.SaveEsdRoi(roiX, roiY, roiW, roiH);

            AddLog(liveApplied
                ? string.Format(
                    "静电杆区域已标定: X={0:F3} Y={1:F3} W={2:F3} H={3:F3}（已实时生效并保存到 esdConfig.json）",
                    roiX, roiY, roiW, roiH)
                : "静电杆区域已保存到 esdConfig.json（当前未启用静电触摸检测，下次启用预览时生效）");
        }

        /// <summary>
        /// 应用门区域标定结果：热更新运行链路 + 配置落盘（语义与静电杆标定一致）。
        /// 额外提示：门区域变了，关门基准图不再匹配——需要门关着时重新采集基准。
        /// </summary>
        private void ApplyDoorRoiSelection(float roiX, float roiY, float roiW, float roiH)
        {
            bool liveApplied = _videoController != null && _videoController.TryUpdateDoorRoi(roiX, roiY, roiW, roiH);

            AppConfig.SaveDoorRoi(roiX, roiY, roiW, roiH);

            AddLog(liveApplied
                ? string.Format(
                    "门区域已标定: X={0:F3} Y={1:F3} W={2:F3} H={3:F3}（已实时生效并保存到 doorConfig.json）\n⚠门区域已变化，请在门关着时点击【重设门基准】重新采集",
                    roiX, roiY, roiW, roiH)
                : "门区域已保存到 doorConfig.json（当前未启用预览，下次启用时生效）\n⚠请在门关着时点击【重设门基准】重新采集");
        }

        /// <summary>
        /// 切换拖拽框选的标定目标：静电杆区域 ↔ 门区域。
        /// 设计稿在左栏"通道与标定"卡与视频浮层各有一处入口，两处按钮文本同步更新。
        /// </summary>
        private void btnCalibTarget_Click(object sender, RoutedEventArgs e)
        {
            if (_calibTarget == CalibTarget.EsdPole)
            {
                _calibTarget = CalibTarget.Door;
                btnCalibTarget.Content = "标定: 门区域";
                btnCalibTargetSide.Content = "标定: 门区域";
                AddLog("标定目标已切换为【门区域】——在预览画面上拖拽框选门的位置");
            }
            else
            {
                _calibTarget = CalibTarget.EsdPole;
                btnCalibTarget.Content = "标定: 静电杆";
                btnCalibTargetSide.Content = "标定: 静电杆";
                AddLog("标定目标已切换为【静电杆区域】——在预览画面上拖拽框选静电杆位置");
            }
        }

        /// <summary>
        /// 重设关门基准：用最近一帧预览画面采集（必须门关着的时候点）。
        /// 基准内存生效并落盘 PNG，重启不丢。
        /// </summary>
        private void btnDoorBaseline_Click(object sender, RoutedEventArgs e)
        {
            if (_videoController == null || !_videoController.IsRunning)
            {
                AddLog("重设门基准失败：请先启动预览");
                return;
            }

            bool ok = _videoController.SetDoorBaselineFromLatestFrame();
            AddLog(ok
                ? "✓关门基准已重设（当前门区域画面已记为'关闭'状态）"
                : "重设门基准失败：门状态监测未启用或尚未产出画面");
        }

        /// <summary>检测结果回调：保存快照并按需输出统计日志</summary>
        private void OnDetectionsReceived(List<DetectionResult> detections)
        {
            _lastDetections = detections ?? new List<DetectionResult>();

            long count = Interlocked.Increment(ref _detectionCount);

            // 开关守卫（v2.9.2）：字符串插值在调用前就已求值——无守卫时即使日志关闭，
            // 每帧仍要构造一次字符串（小对象但每帧一次，积少成多）。先判开关再组装。
            if (!LogManager.EnableDetectionResultLog)
            {
                return;
            }

            if (_lastDetections.Count > 0)
            {
                var d = _lastDetections[0];
                LogManager.DetectionResultLog(
                    $"★检测#{count}: {_lastDetections.Count}个, cls={d.ClassId}({d.ClassName}) " +
                    $"conf={d.Confidence:F3} pos=({d.X:F0},{d.Y:F0}) size={d.Width:F0}x{d.Height:F0}");
            }
            else if (count <= 5 || count % 30 == 0)
            {
                LogManager.DetectionResultLog(
                    $"☆帧#{count}: 0个目标(阈值={AppConfig.Yolo.ConfidenceThreshold})");
            }
        }

        // ============================================================
        // 主题切换（深色 ↔ 浅色）
        // ============================================================

        private void btnTheme_Click(object sender, RoutedEventArgs e)
        {
            ApplyTheme(!_isDarkTheme);
        }

        /// <summary>
        /// 切换深/浅主题：把 App 资源里的 Palette.* 字典原位替换（Dark ↔ Light）。
        /// 全部颜色经 DynamicResource 引用同名 key，替换即整体换肤，样式文件零改动。
        /// </summary>
        private void ApplyTheme(bool dark)
        {
            // 必须用"程序集名;component"形式的显式 pack URI：相对形式在 net472 运行时
            // 按 file:// 解析会 IOException；不带程序集名的 pack 形式只解析入口程序集，
            // 回归 harness 里加载主窗口时找不到资源（与 App.xaml 同一坑位，见其注释）
            string source = dark
                ? "pack://application:,,,/YoloDetector;component/UI/Themes/Palette.Dark.xaml"
                : "pack://application:,,,/YoloDetector;component/UI/Themes/Palette.Light.xaml";
            var newDict = new ResourceDictionary { Source = new Uri(source, UriKind.Absolute) };

            var merged = Application.Current.Resources.MergedDictionaries;
            for (int i = 0; i < merged.Count; i++)
            {
                var s = merged[i].Source;
                if (s != null && s.OriginalString.Contains("Palette."))
                {
                    merged[i] = newDict; // 原位替换，保持字典顺序（画刷先于样式解析）
                    break;
                }
            }

            _isDarkTheme = dark;
            btnTheme.ToolTip = dark ? "切换到浅色主题" : "切换到深色主题";
        }

        // ============================================================
        // 线程安全UI工具
        // ============================================================

        /// <summary>
        /// 安全地把动作调度到 UI 线程执行（WinForms SafeBeginInvoke 的 WPF 对应物）。
        /// 返回 false 表示无法送达（Dispatcher 已开始关停），调用方应就地释放随行资源。
        /// 绝不抛出因窗口生命周期导致的异常。
        /// </summary>
        private bool SafeDispatcherInvoke(Action action)
        {
            try
            {
                var dispatcher = Dispatcher;
                if (dispatcher == null ||
                    dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
                {
                    return false;
                }

                if (dispatcher.CheckAccess())
                {
                    action();
                }
                else
                {
                    dispatcher.BeginInvoke(action);
                }
                return true;
            }
            catch (TaskCanceledException)
            {
                // Dispatcher 关停瞬间 BeginInvoke 可能被取消
                return false;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        // ============================================================
        // 窗口生命周期
        // ============================================================

        private void Window_Closed(object sender, EventArgs e)
        {
            // 顺序很重要（与 WinForms 版 FormClosing 一致）：
            // 1. 先停定时器（阻止新的异步轮询）
            // 2. 停止视频检测链路（有界等待后台线程退出，之后不会再有帧回调）
            // 3. 清除检测模块的UI日志回调
            // 4. 释放预览画面引用
            // 5. 关闭文件日志（写"程序退出"分隔线，GUI 冒烟脚本依赖此配对标记）
            if (_statusTimer != null)
            {
                _statusTimer.Stop();
                _statusTimer = null;
            }

            if (_videoController != null)
            {
                _videoController.Dispose();
                _videoController = null;
            }

            LogManager.ClearUiSink();

            if (roiImage != null)
            {
                roiImage.Source = null; // WPF 位图无 Dispose 语义，断开引用交给 GC
                _previewBitmap = null;
            }

            Logger.Close();
        }
    }
}
