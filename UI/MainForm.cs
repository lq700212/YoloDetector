using System;
using System.Collections.Generic;
using System.Drawing;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using YoloDetector.App;
using YoloDetector.Cameras;
using YoloDetector.Configuration;
using YoloDetection;
using YoloDetector.Infrastructure.Logging;

namespace YoloDetector.UI
{
    /// <summary>
    /// 主窗体（纯视图层）。
    ///
    /// 职责边界：
    ///   - 只负责界面构建、用户交互与结果显示
    ///   - 相机连接管理委托给 CameraController
    ///   - 视频检测编排委托给 VideoDetectionController（UI 层不接触任何 Mat/OpenCV 对象）
    ///
    /// 后台回调安全约定：
    ///   控制器的回调在后台线程触发，本窗体所有回调入口统一经过
    ///   SafeBeginInvoke 保护（句柄检查 + 异常兜底），杜绝关闭窗口瞬间的崩溃。
    /// </summary>
    public partial class MainForm : Form
    {
        private readonly CameraController _cameraController = new CameraController();
        private VideoDetectionController _videoController;

        private System.Windows.Forms.Timer _statusTimer;

        // 最近一次检测结果快照（不可变副本，供扩展使用）
        private volatile List<DetectionResult> _lastDetections = new List<DetectionResult>();
        private long _detectionCount;

        public MainForm()
        {
            InitializeComponent();
            InitializeControllers();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);

            // 句柄创建后再记录启动日志，确保能显示到UI面板
            AddLog("程序已启动，请输入相机IP地址并点击【连接相机】");
            AddLog("提示：连接成功后点击【开始预览】查看摄像头画面");
            AddLog("提示：预览时可在画面上按住鼠标左键拖拽，框选静电杆检测区域（立即生效并保存）");
        }

        private void InitializeControllers()
        {
            _statusTimer = new System.Windows.Forms.Timer
            {
                Interval = AppConfig.Current.Preview.StatusRefreshIntervalMs
            };
            _statusTimer.Tick += StatusTimer_Tick;

            // 静电杆 ROI 拖拽标定：控件内部已封装鼠标接线/虚线框绘制/坐标换算，
            // 这里只订阅结果（归一化 ROI → 热更新 + 落盘）。
            // 控件与窗体同生命周期，事件无需成对退订（控件销毁即随链断开）
            videoPictureBox.RoiSelected += roi => ApplyEsdRoiSelection(roi.X, roi.Y, roi.W, roi.H);
        }

        // ============================================================
        // 日志
        // ============================================================

        /// <summary>追加一条操作日志（写入文件 + UI 面板，任意线程可调用）</summary>
        private void AddLog(string message)
        {
            Logger.Write(message);

            // 无法送达UI时（窗体关闭中）仅记录到文件，无需额外处理
            SafeBeginInvoke(() => AppendLogToPanel(message));
        }

        private void AppendLogToPanel(string message)
        {
            if (txtLog == null || txtLog.IsDisposed)
            {
                return;
            }

            string time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            txtLog.AppendText("[" + time + "] " + message + Environment.NewLine);

            // 行数超限时裁掉前一半：AppendText 对长文本是 O(n) 操作且 TextBox
            // 保留全部历史会持续吃内存，现场长时间运行必须限制（阈值 500 行）
            const int MaxLogLines = 500;
            if (txtLog.Lines.Length > MaxLogLines)
            {
                // 捕获当前滚动位置，裁剪后恢复，避免日志区自动跳回顶部
                int scrollIndex = txtLog.GetFirstCharIndexOfCurrentLine();
                var lines = txtLog.Lines;
                var kept = new string[lines.Length - MaxLogLines / 2];
                Array.Copy(lines, MaxLogLines / 2, kept, 0, kept.Length);
                txtLog.Text = string.Join(Environment.NewLine, kept) + Environment.NewLine;
                if (scrollIndex >= txtLog.TextLength)
                {
                    scrollIndex = Math.Max(0, txtLog.TextLength - 1);
                }
                txtLog.SelectionStart = scrollIndex;
                txtLog.ScrollToCaret();
            }
            else
            {
                txtLog.ScrollToCaret();
            }
        }

        // ============================================================
        // 相机连接
        // ============================================================

        private async void btnConnect_Click(object sender, EventArgs e)
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
                MessageBox.Show("请输入相机IP地址！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!IPAddress.TryParse(ip, out _))
            {
                MessageBox.Show("请输入有效的IP地址！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            catch (Exception ex)
            {
                UpdateConnectionStatus(false);
                AddLog("连接异常: " + ex.Message);
                MessageBox.Show("连接异常: " + ex.Message, "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                SetConnectingUi(false);
            }
        }

        private void SetConnectingUi(bool connecting)
        {
            btnConnect.Enabled = !connecting;
            btnConnect.Text = connecting ? "连接中..." : (_cameraController.IsConnected ? "断开连接" : "连接相机");
        }

        private void UpdateConnectionStatus(bool connected)
        {
            if (lblStatus == null || btnConnect == null) return;

            // lblStatus 位于天蓝色顶栏上，红/绿深色在蓝底上对比度差，
            // 改用浅色调徽标配色（未连接=珊瑚白 / 已连接=薄荷白）
            lblStatus.Text = connected ? "● 已连接" : "● 未连接";
            lblStatus.ForeColor = connected
                ? System.Drawing.Color.FromArgb(223, 247, 232)
                : System.Drawing.Color.FromArgb(255, 225, 222);
            btnConnect.Text = connected ? "断开连接" : "连接相机";
        }

        // ============================================================
        // 拉流/推流控制
        // ============================================================

        private async void btnStartRtsp_Click(object sender, EventArgs e)
        {
            await SetRtspEnableAsync(true);
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

        private async void btnStartRtmp_Click(object sender, EventArgs e)
        {
            if (!EnsureCameraConnected()) return;

            int channel = GetChannel();
            AddLog("开启通道 " + channel + " 推流...");

            try
            {
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

            MessageBox.Show("请先连接相机！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        private int GetChannel()
        {
            return numChannel != null ? (int)numChannel.Value : 0;
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
                SafeBeginInvoke(() =>
                {
                    if (txtStatusInfo != null && !txtStatusInfo.IsDisposed)
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
            sb.AppendLine("=== 设备基本信息 ===");
            sb.AppendLine("IP地址: " + status.IpAddress);
            sb.AppendLine("品牌: " + status.Brand);
            sb.AppendLine("CPU使用率: " + status.CpuUsage.ToString("F1") + "%");
            sb.AppendLine("内存使用率: " + status.MemoryUsage.ToString("F1") + "%");
            sb.AppendLine("磁盘总量: " + status.GetFormattedDiskTotal());
            sb.AppendLine("磁盘可用: " + status.GetFormattedDiskFree());
            sb.AppendLine("磁盘使用率: " + status.GetDiskUsage().ToString("F1") + "%");
            sb.AppendLine("录像总数: " + status.TotalVideoCount);
            return sb.ToString();
        }

        // ============================================================
        // 视频预览与YOLO检测
        // ============================================================

        private void btnStartPreview_Click(object sender, EventArgs e)
        {
            StartVideoPreview();
        }

        private void btnStopPreview_Click(object sender, EventArgs e)
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
                    LogSink = msg => SafeBeginInvoke(() => AppendLogToPanel(msg)),
                    // 静电杆触摸检测旁路：模型/参数从 esdConfig.json 来；
                    // Enabled=false 时传 null 等价于关闭，管道零开销
                    PoseModelPath = AppConfig.Esd.Enabled
                        ? ExpandModelPath(AppConfig.Esd.PoseModelPath)
                        : null,
                    EsdOptions = AppConfig.Esd.Enabled ? AppConfig.Esd.ToOptions() : null
                };

                _videoController.Start(options);

                if (lblVideoTitle != null)
                {
                    lblVideoTitle.Visible = false;
                }

                AddLog("YOLO检测已启动");
            }
            catch (System.IO.FileNotFoundException fnfEx)
            {
                AddLog("YOLO模型文件不存在: " + fnfEx.FileName);
                MessageBox.Show("YOLO模型文件不存在: " + fnfEx.FileName, "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (Exception ex)
            {
                AddLog("视频预览启动失败: " + ex.Message);
                MessageBox.Show(
                    "视频预览启动失败！\n\n请检查：\n1. RTSP流地址是否正确\n2. 相机是否开启视频输出\n3. 网络连接是否正常\n\n详细信息: " + ex.Message,
                    "视频预览失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
                SafeBeginInvoke(() => { if (lblVideoTitle.Visible == false) lblVideoTitle.Visible = true; });
            }
        }

        private void EnsureVideoController()
        {
            if (_videoController != null) return;

            _videoController = new VideoDetectionController(
                previewSink: OnPreviewFrameReceived,
                detectionSink: OnDetectionsReceived);

            // 静电触摸状态翻转 → 日志面板提示（仅在事件帧触发一次，不会刷屏）。
            // 回调在检测线程触发，经 SafeBeginInvoke 调度回 UI 线程。
            _videoController.EsdContactChanged += (s, e) =>
            {
                string msg = e.InContact
                    ? $"⚡人员#{e.TrackId} 正在触摸静电杆 (持续{e.ContactElapsedMs / 1000.0:F1}秒)"
                    : $"人员#{e.TrackId} 结束触摸静电杆";
                SafeBeginInvoke(() => AddLog(msg));
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
        /// 预览帧回调：Bitmap 所有权移交本方法。
        /// 经 SafeBeginInvoke 调度到 UI 线程后显示；调度失败时立即释放防止泄漏。
        /// </summary>
        private void OnPreviewFrameReceived(Bitmap frame)
        {
            if (!SafeBeginInvoke(() => ShowPreviewFrame(frame)))
            {
                frame.Dispose(); // 无法送达UI（如窗体正在关闭），就地释放
            }
        }

        private void ShowPreviewFrame(Bitmap frame)
        {
            if (videoPictureBox == null || videoPictureBox.IsDisposed)
            {
                frame.Dispose();
                return;
            }

            var old = videoPictureBox.Image;
            videoPictureBox.Image = frame;
            old?.Dispose();
        }

        // ============================================================
        // 静电杆 ROI 拖拽标定（预览画面上框选静电杆区域）
        //
        // 交互封装在 RoiSelectionPictureBox 控件内（鼠标/绘制/坐标换算），
        // 本窗体只消费结果：热更新运行链路 + 配置落盘。
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

        /// <summary>检测结果回调：保存快照并按需输出统计日志</summary>
        private void OnDetectionsReceived(List<DetectionResult> detections)
        {
            _lastDetections = detections ?? new List<DetectionResult>();

            long count = Interlocked.Increment(ref _detectionCount);

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
        // 线程安全UI工具
        // ============================================================

        /// <summary>
        /// 安全地把动作调度到UI线程执行。
        /// 返回 false 表示无法送达（句柄未创建或窗体正在销毁）。
        /// 绝不抛出因窗体生命周期导致的异常。
        /// </summary>
        private bool SafeBeginInvoke(Action action)
        {
            try
            {
                Control target = this;
                if (IsDisposed || Disposing || !IsHandleCreated)
                {
                    return false;
                }

                target.BeginInvoke(action);
                return true;
            }
            catch (ObjectDisposedException)
            {
                return false;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        // ============================================================
        // 窗体生命周期
        // ============================================================

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            // 顺序很重要：
            // 1. 先停定时器（阻止新的异步轮询）
            // 2. 停止视频检测链路（有界等待后台线程退出，之后不会再有帧回调）
            // 3. 清除检测模块的UI日志回调
            // 4. 释放PictureBox当前图像
            // 5. 关闭文件日志
            if (_statusTimer != null)
            {
                _statusTimer.Stop();
                _statusTimer.Dispose();
                _statusTimer = null;
            }

            if (_videoController != null)
            {
                _videoController.Dispose();
                _videoController = null;
            }

            LogManager.ClearUiSink();

            if (videoPictureBox != null)
            {
                var img = videoPictureBox.Image;
                videoPictureBox.Image = null;
                img?.Dispose();
            }

            Logger.Close();
        }
    }
}
