using System;
using System.Collections.Generic;
using System.Drawing;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using OpenCvSharp;

namespace YoloDetector
{
    // ============================================================
    // 主窗体类
    // 功能：摄像头二次开发Demo的主界面
    // ============================================================
    public partial class MainForm : Form
    {
        // --------------------------
        // 成员变量声明区
        // --------------------------
        
        // 相机API客户端（使用接口类型实现品牌解耦）
        // 具体实现由配置文件中的CameraBrand字段决定（如XSW、HIK、DAHUA）
        private ICameraApi cameraClient;
        
        // 状态定时器
        private System.Windows.Forms.Timer statusTimer;
        
        // ============================================================
        // 视频预览：使用OpenCV捕获并在PictureBox显示
        // ============================================================
        private PictureBox videoPictureBox;

        // ============================================================
        // YOLO检测相关变量
        // ============================================================
        // YOLO检测器实例（接口类型，方便替换不同版本的YOLO）
        // 具体实现是YoloV26Detector，负责加载模型和执行检测
        private YoloDetection.IYoloDetector yoloDetector;
        
        // YOLO检测服务（核心服务类）
        // 负责管理检测线程、接收视频帧、执行检测、绘制检测框
        // 通过事件通知UI显示结果
        private YoloDetection.YoloDetectionService yoloDetectionService;
        
        // RTSP帧捕获器
        // 使用OpenCV的VideoCapture捕获RTSP流，每帧传递给检测服务
        private YoloDetection.RtspFrameCapturer rtspFrameCapturer;
        
        // 最后一次检测结果列表
        // 保存最近一次的检测结果，供其他地方使用
        private List<YoloDetection.DetectionResult> lastYoloDetections = new List<YoloDetection.DetectionResult>();
        
        // --------------------------
        // 界面控件成员变量（声明为public以便设计器访问）
        // --------------------------
        
        // 连接区域控件
        private Panel connectPanel;
        private Label lblTitle;
        private Label lblIp;
        private TextBox txtIp;
        private Label lblStatus;
        private Button btnConnect;
        
        // 视频流配置区域控件
        private Panel streamPanel;
        private Label lblStream;
        private Label lblUrl;
        private TextBox txtStreamUrl;
        private Button btnTestStream;
        
        // 推拉流控制区域控件
        private Panel controlPanel;
        private Label lblControl;
        private Label lblChannel;
        private NumericUpDown numChannel;
        private Button btnStartPreview;
        private Button btnStopPreview;
        private Button btnStartRtsp;
        private Button btnStartRtmp;
        
        // 设备状态信息区域控件
        private Panel infoPanel;
        private Label lblInfo;
        private TextBox txtStatusInfo;
        
        // 视频预览区域控件
        private Panel rightPanel;
        private Panel videoPanel;
        private Label lblVideoTitle;
        
        // 日志区域控件
        private Panel logPanel;
        private Label lblLog;
        private TableLayoutPanel layoutTable;
        private TableLayoutPanel rightTable;
        private TextBox txtLog;
        
        // ============================================================
        // 构造函数
        // ============================================================
        public MainForm()
        {
            // 第一步：初始化界面控件
            InitializeComponent();
            
            // 第二步：初始化自定义组件
            InitializeCustomComponents();
            
            // 第三步：添加启动日志（只有在运行时执行）
            if (!DesignMode)
            {
                AddLog("程序已启动，请输入相机IP地址并点击【连接相机】");
                AddLog("提示：连接成功后点击【开始预览】查看摄像头画面");
            }
        }
        
        // ============================================================
        // 初始化自定义组件
        // ============================================================
        private void InitializeCustomComponents()
        {
            // 1. 初始化状态定时器
            statusTimer = new System.Windows.Forms.Timer();
            statusTimer.Interval = AppConfig.Current.Preview.StatusRefreshIntervalMs;
            statusTimer.Tick += StatusTimer_Tick;

            // 2. 注册YOLO检测器工厂（支持热插拔）
            // 通过工厂模式和注册表机制，支持运行时动态切换检测器
            // 新增检测器时只需创建工厂类并注册，无需修改主程序代码
            YoloDetection.DetectorFactoryRegistry.RegisterFactory(
                new YoloDetection.YoloV26DetectorFactory());
        }
        
        // ============================================================
        // 初始化界面控件（标准WinForms设计器模式）
        // ============================================================
        private void InitializeComponent()
        {
            this.layoutTable = new System.Windows.Forms.TableLayoutPanel();
            this.connectPanel = new System.Windows.Forms.Panel();
            this.lblTitle = new System.Windows.Forms.Label();
            this.lblIp = new System.Windows.Forms.Label();
            this.txtIp = new System.Windows.Forms.TextBox();
            this.lblStatus = new System.Windows.Forms.Label();
            this.btnConnect = new System.Windows.Forms.Button();
            this.streamPanel = new System.Windows.Forms.Panel();
            this.lblStream = new System.Windows.Forms.Label();
            this.lblUrl = new System.Windows.Forms.Label();
            this.txtStreamUrl = new System.Windows.Forms.TextBox();
            this.btnTestStream = new System.Windows.Forms.Button();
            this.controlPanel = new System.Windows.Forms.Panel();
            this.lblControl = new System.Windows.Forms.Label();
            this.lblChannel = new System.Windows.Forms.Label();
            this.numChannel = new System.Windows.Forms.NumericUpDown();
            this.btnStartPreview = new System.Windows.Forms.Button();
            this.btnStopPreview = new System.Windows.Forms.Button();
            this.btnStartRtsp = new System.Windows.Forms.Button();
            this.btnStartRtmp = new System.Windows.Forms.Button();
            this.infoPanel = new System.Windows.Forms.Panel();
            this.lblInfo = new System.Windows.Forms.Label();
            this.txtStatusInfo = new System.Windows.Forms.TextBox();
            this.rightPanel = new System.Windows.Forms.Panel();
            this.rightTable = new System.Windows.Forms.TableLayoutPanel();
            this.videoPanel = new System.Windows.Forms.Panel();
            this.videoPictureBox = new System.Windows.Forms.PictureBox();
            this.lblVideoTitle = new System.Windows.Forms.Label();
            this.logPanel = new System.Windows.Forms.Panel();
            this.lblLog = new System.Windows.Forms.Label();
            this.txtLog = new System.Windows.Forms.TextBox();
            this.layoutTable.SuspendLayout();
            this.connectPanel.SuspendLayout();
            this.streamPanel.SuspendLayout();
            this.controlPanel.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.numChannel)).BeginInit();
            this.infoPanel.SuspendLayout();
            this.rightPanel.SuspendLayout();
            this.rightTable.SuspendLayout();
            this.videoPanel.SuspendLayout();
            this.logPanel.SuspendLayout();
            this.SuspendLayout();
            // 
            // layoutTable
            // 
            this.layoutTable.ColumnCount = 2;
            this.layoutTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 35F));
            this.layoutTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 65F));
            this.layoutTable.Controls.Add(this.connectPanel, 0, 0);
            this.layoutTable.Controls.Add(this.streamPanel, 0, 1);
            this.layoutTable.Controls.Add(this.controlPanel, 0, 2);
            this.layoutTable.Controls.Add(this.infoPanel, 0, 3);
            this.layoutTable.Controls.Add(this.rightPanel, 1, 0);
            this.layoutTable.Dock = System.Windows.Forms.DockStyle.Fill;
            this.layoutTable.GrowStyle = System.Windows.Forms.TableLayoutPanelGrowStyle.FixedSize;
            this.layoutTable.Location = new System.Drawing.Point(0, 0);
            this.layoutTable.Name = "layoutTable";
            this.layoutTable.RowCount = 4;
            this.layoutTable.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 80F));
            this.layoutTable.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 80F));
            this.layoutTable.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 80F));
            this.layoutTable.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.layoutTable.Size = new System.Drawing.Size(1084, 711);
            this.layoutTable.TabIndex = 0;
            // 
            // connectPanel
            // 
            this.connectPanel.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(245)))), ((int)(((byte)(247)))), ((int)(((byte)(250)))));
            this.connectPanel.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.connectPanel.Controls.Add(this.lblTitle);
            this.connectPanel.Controls.Add(this.lblIp);
            this.connectPanel.Controls.Add(this.txtIp);
            this.connectPanel.Controls.Add(this.lblStatus);
            this.connectPanel.Controls.Add(this.btnConnect);
            this.connectPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.connectPanel.Location = new System.Drawing.Point(3, 3);
            this.connectPanel.Name = "connectPanel";
            this.connectPanel.Size = new System.Drawing.Size(373, 74);
            this.connectPanel.TabIndex = 0;
            // 
            // lblTitle
            // 
            this.lblTitle.AutoSize = true;
            this.lblTitle.Font = new System.Drawing.Font("微软雅黑", 12F, System.Drawing.FontStyle.Bold);
            this.lblTitle.Location = new System.Drawing.Point(10, 10);
            this.lblTitle.Name = "lblTitle";
            this.lblTitle.Size = new System.Drawing.Size(106, 22);
            this.lblTitle.TabIndex = 0;
            this.lblTitle.Text = "相机连接配置";
            // 
            // lblIp
            // 
            this.lblIp.AutoSize = true;
            this.lblIp.Location = new System.Drawing.Point(10, 35);
            this.lblIp.Name = "lblIp";
            this.lblIp.Size = new System.Drawing.Size(47, 12);
            this.lblIp.TabIndex = 1;
            this.lblIp.Text = "相机IP:";
            // 
            // txtIp
            // 
            this.txtIp.Font = new System.Drawing.Font("微软雅黑", 10F);
            this.txtIp.Location = new System.Drawing.Point(70, 32);
            this.txtIp.Name = "txtIp";
            this.txtIp.Size = new System.Drawing.Size(150, 25);
            this.txtIp.TabIndex = 2;
            this.txtIp.Text = AppConfig.Current.Connection.DefaultIp;
            // 
            // lblStatus
            // 
            this.lblStatus.AutoSize = true;
            this.lblStatus.Font = new System.Drawing.Font("微软雅黑", 10F, System.Drawing.FontStyle.Bold);
            this.lblStatus.ForeColor = System.Drawing.Color.Red;
            this.lblStatus.Location = new System.Drawing.Point(230, 15);
            this.lblStatus.Name = "lblStatus";
            this.lblStatus.Size = new System.Drawing.Size(87, 19);
            this.lblStatus.TabIndex = 3;
            this.lblStatus.Text = "状态: 未连接";
            // 
            // btnConnect
            // 
            this.btnConnect.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(0)))), ((int)(((byte)(122)))), ((int)(((byte)(204)))));
            this.btnConnect.Font = new System.Drawing.Font("微软雅黑", 10F);
            this.btnConnect.ForeColor = System.Drawing.Color.White;
            this.btnConnect.Location = new System.Drawing.Point(230, 35);
            this.btnConnect.Name = "btnConnect";
            this.btnConnect.Size = new System.Drawing.Size(120, 25);
            this.btnConnect.TabIndex = 4;
            this.btnConnect.Text = "连接相机";
            this.btnConnect.UseVisualStyleBackColor = false;
            this.btnConnect.Click += new System.EventHandler(this.btnConnect_Click);
            // 
            // streamPanel
            // 
            this.streamPanel.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(245)))), ((int)(((byte)(247)))), ((int)(((byte)(250)))));
            this.streamPanel.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.streamPanel.Controls.Add(this.lblStream);
            this.streamPanel.Controls.Add(this.lblUrl);
            this.streamPanel.Controls.Add(this.txtStreamUrl);
            this.streamPanel.Controls.Add(this.btnTestStream);
            this.streamPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.streamPanel.Location = new System.Drawing.Point(3, 63);
            this.streamPanel.Name = "streamPanel";
            this.streamPanel.Size = new System.Drawing.Size(373, 74);
            this.streamPanel.TabIndex = 1;
            // 
            // lblStream
            // 
            this.lblStream.AutoSize = true;
            this.lblStream.Font = new System.Drawing.Font("微软雅黑", 12F, System.Drawing.FontStyle.Bold);
            this.lblStream.Location = new System.Drawing.Point(10, 10);
            this.lblStream.Name = "lblStream";
            this.lblStream.Size = new System.Drawing.Size(90, 22);
            this.lblStream.TabIndex = 0;
            this.lblStream.Text = "视频流配置";
            // 
            // lblUrl
            // 
            this.lblUrl.AutoSize = true;
            this.lblUrl.Location = new System.Drawing.Point(10, 40);
            this.lblUrl.Name = "lblUrl";
            this.lblUrl.Size = new System.Drawing.Size(47, 12);
            this.lblUrl.TabIndex = 1;
            this.lblUrl.Text = "流地址:";
            // 
            // txtStreamUrl
            // 
            this.txtStreamUrl.Font = new System.Drawing.Font("微软雅黑", 9F);
            this.txtStreamUrl.Location = new System.Drawing.Point(70, 37);
            this.txtStreamUrl.Name = "txtStreamUrl";
            this.txtStreamUrl.Size = new System.Drawing.Size(300, 23);
            this.txtStreamUrl.TabIndex = 2;
            this.txtStreamUrl.Text = AppConfig.Current.Stream.GetRtspUrl(AppConfig.Current.Connection.DefaultIp, 0);
            // 
            // btnTestStream
            // 
            this.btnTestStream.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(240)))), ((int)(((byte)(173)))), ((int)(((byte)(78)))));
            this.btnTestStream.Font = new System.Drawing.Font("微软雅黑", 9F);
            this.btnTestStream.ForeColor = System.Drawing.Color.White;
            this.btnTestStream.Location = new System.Drawing.Point(380, 35);
            this.btnTestStream.Name = "btnTestStream";
            this.btnTestStream.Size = new System.Drawing.Size(70, 30);
            this.btnTestStream.TabIndex = 3;
            this.btnTestStream.Text = "测试流";
            this.btnTestStream.UseVisualStyleBackColor = false;
            this.btnTestStream.Click += new System.EventHandler(this.btnTestStream_Click);
            // 
            // controlPanel
            // 
            this.controlPanel.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(245)))), ((int)(((byte)(247)))), ((int)(((byte)(250)))));
            this.controlPanel.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.controlPanel.Controls.Add(this.lblControl);
            this.controlPanel.Controls.Add(this.lblChannel);
            this.controlPanel.Controls.Add(this.numChannel);
            this.controlPanel.Controls.Add(this.btnStartPreview);
            this.controlPanel.Controls.Add(this.btnStopPreview);
            this.controlPanel.Controls.Add(this.btnStartRtsp);
            this.controlPanel.Controls.Add(this.btnStartRtmp);
            this.controlPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.controlPanel.Location = new System.Drawing.Point(3, 143);
            this.controlPanel.Name = "controlPanel";
            this.controlPanel.Size = new System.Drawing.Size(373, 74);
            this.controlPanel.TabIndex = 2;
            // 
            // lblControl
            // 
            this.lblControl.AutoSize = true;
            this.lblControl.Font = new System.Drawing.Font("微软雅黑", 12F, System.Drawing.FontStyle.Bold);
            this.lblControl.Location = new System.Drawing.Point(10, 10);
            this.lblControl.Name = "lblControl";
            this.lblControl.Size = new System.Drawing.Size(90, 22);
            this.lblControl.TabIndex = 0;
            this.lblControl.Text = "推拉流控制";
            // 
            // lblChannel
            // 
            this.lblChannel.AutoSize = true;
            this.lblChannel.Location = new System.Drawing.Point(10, 40);
            this.lblChannel.Name = "lblChannel";
            this.lblChannel.Size = new System.Drawing.Size(35, 12);
            this.lblChannel.TabIndex = 1;
            this.lblChannel.Text = "通道:";
            // 
            // numChannel
            // 
            this.numChannel.Location = new System.Drawing.Point(50, 37);
            this.numChannel.Maximum = new decimal(new int[] {
            AppConfig.Current.Stream.MaxChannel,
            0,
            0,
            0});
            this.numChannel.Name = "numChannel";
            this.numChannel.Size = new System.Drawing.Size(60, 21);
            this.numChannel.TabIndex = 2;
            // 
            // btnStartPreview
            // 
            this.btnStartPreview.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(92)))), ((int)(((byte)(184)))), ((int)(((byte)(92)))));
            this.btnStartPreview.Font = new System.Drawing.Font("微软雅黑", 9F);
            this.btnStartPreview.ForeColor = System.Drawing.Color.White;
            this.btnStartPreview.Location = new System.Drawing.Point(120, 35);
            this.btnStartPreview.Name = "btnStartPreview";
            this.btnStartPreview.Size = new System.Drawing.Size(80, 30);
            this.btnStartPreview.TabIndex = 3;
            this.btnStartPreview.Text = "开始预览";
            this.btnStartPreview.UseVisualStyleBackColor = false;
            this.btnStartPreview.Click += new System.EventHandler(this.btnStartPreview_Click);
            // 
            // btnStopPreview
            // 
            this.btnStopPreview.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(217)))), ((int)(((byte)(83)))), ((int)(((byte)(79)))));
            this.btnStopPreview.Font = new System.Drawing.Font("微软雅黑", 9F);
            this.btnStopPreview.ForeColor = System.Drawing.Color.White;
            this.btnStopPreview.Location = new System.Drawing.Point(210, 35);
            this.btnStopPreview.Name = "btnStopPreview";
            this.btnStopPreview.Size = new System.Drawing.Size(80, 30);
            this.btnStopPreview.TabIndex = 4;
            this.btnStopPreview.Text = "停止预览";
            this.btnStopPreview.UseVisualStyleBackColor = false;
            this.btnStopPreview.Click += new System.EventHandler(this.btnStopPreview_Click);
            // 
            // btnStartRtsp
            // 
            this.btnStartRtsp.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(92)))), ((int)(((byte)(184)))), ((int)(((byte)(92)))));
            this.btnStartRtsp.Font = new System.Drawing.Font("微软雅黑", 9F);
            this.btnStartRtsp.ForeColor = System.Drawing.Color.White;
            this.btnStartRtsp.Location = new System.Drawing.Point(300, 35);
            this.btnStartRtsp.Name = "btnStartRtsp";
            this.btnStartRtsp.Size = new System.Drawing.Size(80, 30);
            this.btnStartRtsp.TabIndex = 5;
            this.btnStartRtsp.Text = "开启拉流";
            this.btnStartRtsp.UseVisualStyleBackColor = false;
            this.btnStartRtsp.Click += new System.EventHandler(this.btnStartRtsp_Click);
            // 
            // btnStartRtmp
            // 
            this.btnStartRtmp.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(92)))), ((int)(((byte)(184)))), ((int)(((byte)(92)))));
            this.btnStartRtmp.Font = new System.Drawing.Font("微软雅黑", 9F);
            this.btnStartRtmp.ForeColor = System.Drawing.Color.White;
            this.btnStartRtmp.Location = new System.Drawing.Point(390, 35);
            this.btnStartRtmp.Name = "btnStartRtmp";
            this.btnStartRtmp.Size = new System.Drawing.Size(80, 30);
            this.btnStartRtmp.TabIndex = 6;
            this.btnStartRtmp.Text = "开启推流";
            this.btnStartRtmp.UseVisualStyleBackColor = false;
            this.btnStartRtmp.Click += new System.EventHandler(this.btnStartRtmp_Click);
            // 
            // infoPanel
            // 
            this.infoPanel.BackColor = System.Drawing.Color.White;
            this.infoPanel.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.infoPanel.Controls.Add(this.lblInfo);
            this.infoPanel.Controls.Add(this.txtStatusInfo);
            this.infoPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.infoPanel.Location = new System.Drawing.Point(3, 223);
            this.infoPanel.Name = "infoPanel";
            this.infoPanel.Size = new System.Drawing.Size(373, 485);
            this.infoPanel.TabIndex = 3;
            // 
            // lblInfo
            // 
            this.lblInfo.AutoSize = true;
            this.lblInfo.Font = new System.Drawing.Font("微软雅黑", 12F, System.Drawing.FontStyle.Bold);
            this.lblInfo.Location = new System.Drawing.Point(10, 10);
            this.lblInfo.Name = "lblInfo";
            this.lblInfo.Size = new System.Drawing.Size(106, 22);
            this.lblInfo.TabIndex = 0;
            this.lblInfo.Text = "设备状态信息";
            // 
            // txtStatusInfo
            // 
            this.txtStatusInfo.BackColor = System.Drawing.Color.White;
            this.txtStatusInfo.Font = new System.Drawing.Font("Consolas", 9F);
            this.txtStatusInfo.Location = new System.Drawing.Point(10, 40);
            this.txtStatusInfo.Multiline = true;
            this.txtStatusInfo.Name = "txtStatusInfo";
            this.txtStatusInfo.ReadOnly = true;
            this.txtStatusInfo.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.txtStatusInfo.Size = new System.Drawing.Size(360, 450);
            this.txtStatusInfo.TabIndex = 1;
            // 
            // rightPanel
            // 
            this.rightPanel.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.rightPanel.Controls.Add(this.rightTable);
            this.rightPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.rightPanel.Location = new System.Drawing.Point(382, 3);
            this.rightPanel.Name = "rightPanel";
            this.layoutTable.SetRowSpan(this.rightPanel, 4);
            this.rightPanel.Size = new System.Drawing.Size(699, 705);
            this.rightPanel.TabIndex = 4;
            // 
            // rightTable
            // 
            this.rightTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.rightTable.Controls.Add(this.videoPanel);
            this.rightTable.Controls.Add(this.logPanel);
            this.rightTable.Dock = System.Windows.Forms.DockStyle.Fill;
            this.rightTable.Location = new System.Drawing.Point(0, 0);
            this.rightTable.Name = "rightTable";
            this.rightTable.RowCount = 2;
            this.rightTable.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 75F));
            this.rightTable.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 25F));
            this.rightTable.Size = new System.Drawing.Size(697, 703);
            this.rightTable.TabIndex = 0;
            // 
            // videoPanel
            // 
            this.videoPanel.BackColor = System.Drawing.Color.Black;
            this.videoPanel.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.videoPanel.Controls.Add(this.videoPictureBox);
            this.videoPanel.Controls.Add(this.lblVideoTitle);
            this.videoPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.videoPanel.Location = new System.Drawing.Point(3, 3);
            this.videoPanel.Name = "videoPanel";
            this.videoPanel.Size = new System.Drawing.Size(691, 521);
            this.videoPanel.TabIndex = 0;
            // 
            // videoPictureBox
            // 
            this.videoPictureBox.Dock = System.Windows.Forms.DockStyle.Fill;
            this.videoPictureBox.Location = new System.Drawing.Point(0, 0);
            this.videoPictureBox.Name = "videoPictureBox";
            this.videoPictureBox.Size = new System.Drawing.Size(687, 517);
            this.videoPictureBox.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
            this.videoPictureBox.TabIndex = 1;
            this.videoPictureBox.TabStop = false;
            // 
            // lblVideoTitle
            // 
            this.lblVideoTitle.AutoSize = true;
            this.lblVideoTitle.Font = new System.Drawing.Font("微软雅黑", 12F, System.Drawing.FontStyle.Bold);
            this.lblVideoTitle.ForeColor = System.Drawing.Color.White;
            this.lblVideoTitle.Location = new System.Drawing.Point(10, 10);
            this.lblVideoTitle.Name = "lblVideoTitle";
            this.lblVideoTitle.Size = new System.Drawing.Size(106, 22);
            this.lblVideoTitle.TabIndex = 0;
            this.lblVideoTitle.Text = "视频预览区域";
            // 
            // logPanel
            // 
            this.logPanel.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.logPanel.Controls.Add(this.lblLog);
            this.logPanel.Controls.Add(this.txtLog);
            this.logPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.logPanel.Location = new System.Drawing.Point(3, 530);
            this.logPanel.Name = "logPanel";
            this.logPanel.Size = new System.Drawing.Size(691, 170);
            this.logPanel.TabIndex = 1;
            // 
            // lblLog
            // 
            this.lblLog.AutoSize = true;
            this.lblLog.Font = new System.Drawing.Font("微软雅黑", 10F, System.Drawing.FontStyle.Bold);
            this.lblLog.Location = new System.Drawing.Point(10, 5);
            this.lblLog.Name = "lblLog";
            this.lblLog.Size = new System.Drawing.Size(65, 19);
            this.lblLog.TabIndex = 0;
            this.lblLog.Text = "操作日志";
            // 
            // txtLog
            // 
            this.txtLog.BackColor = System.Drawing.Color.Black;
            this.txtLog.Font = new System.Drawing.Font("Consolas", 9F);
            this.txtLog.ForeColor = System.Drawing.Color.LightGreen;
            this.txtLog.Location = new System.Drawing.Point(10, 30);
            this.txtLog.Multiline = true;
            this.txtLog.Name = "txtLog";
            this.txtLog.ReadOnly = true;
            this.txtLog.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.txtLog.Size = new System.Drawing.Size(640, 140);
            this.txtLog.TabIndex = 1;
            // 
            // MainForm
            // 
            this.ClientSize = new System.Drawing.Size(1084, 711);
            this.Controls.Add(this.layoutTable);
            this.Name = "MainForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "摄像头二次开发Demo";
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.MainForm_FormClosing);
            this.layoutTable.ResumeLayout(false);
            this.connectPanel.ResumeLayout(false);
            this.connectPanel.PerformLayout();
            this.streamPanel.ResumeLayout(false);
            this.streamPanel.PerformLayout();
            this.controlPanel.ResumeLayout(false);
            this.controlPanel.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.numChannel)).EndInit();
            this.infoPanel.ResumeLayout(false);
            this.infoPanel.PerformLayout();
            this.rightPanel.ResumeLayout(false);
            this.rightTable.ResumeLayout(false);
            this.videoPanel.ResumeLayout(false);
            this.videoPanel.PerformLayout();
            this.logPanel.ResumeLayout(false);
            this.logPanel.PerformLayout();
            this.ResumeLayout(false);

        }
        
        // ============================================================
        // 添加日志方法（使用成员变量，安全可靠，支持跨线程调用）
        // ============================================================
        // 说明：由于WinForms要求所有UI操作必须在创建控件的线程（主线程）上执行，
        //       如果从其他线程（如LibVLC回调线程）调用此方法，需要使用Invoke切换到UI线程
        private void AddLog(string message)
        {
            Logger.Write(message);
            
            if (txtLog == null)
            {
                return;
            }
            
            // 检查是否需要跨线程调用（当前线程是否是创建txtLog控件的线程）
            if (txtLog.InvokeRequired)
            {
                // 需要跨线程调用，使用Invoke方法在UI线程上执行AddLog方法
                txtLog.Invoke(new Action<string>(AddLog), message);
                return;
            }
            
            string time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            
            txtLog.AppendText("[" + time + "] " + message + "\r\n");
            
            txtLog.ScrollToCaret();
        }
        
        // ============================================================
        // 更新连接状态显示
        // ============================================================
        private void UpdateConnectionStatus(bool connected)
        {
            // 安全检查
            if (lblStatus == null || btnConnect == null)
                return;
            
            // 设置状态文本和颜色
            lblStatus.Text = connected ? "状态: 已连接" : "状态: 未连接";
            lblStatus.ForeColor = connected ? Color.Green : Color.Red;
            
            // 设置按钮文本
            btnConnect.Text = connected ? "断开连接" : "连接相机";
        }
        
        // ============================================================
        // 连接相机按钮点击事件
        // ============================================================
        private async void btnConnect_Click(object sender, EventArgs e)
        {
            // 安全检查
            if (txtIp == null || btnConnect == null)
                return;
            
            // 如果已经连接，就断开
            if (cameraClient != null)
            {
                StopVideoPreview();
                cameraClient = null;
                statusTimer.Stop();
                UpdateConnectionStatus(false);
                AddLog("已断开相机连接");
                return;
            }
            
            // 获取IP地址
            string ip = txtIp.Text.Trim();
            
            // 验证IP地址
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
            
            // 禁用按钮
            btnConnect.Enabled = false;
            btnConnect.Text = "连接中...";
            AddLog("正在连接相机 " + ip + "...");
            
            try
            {
                // 创建API客户端并测试连接
                cameraClient = CameraApiFactory.Create(ip);
                bool connected = await cameraClient.TestConnectionAsync(ip);
                
                if (connected)
                {
                    UpdateConnectionStatus(true);
                    AddLog("相机 " + ip + " 连接成功！");
                    statusTimer.Start();
                    await RefreshDeviceStatus();
                    
                    // 更新视频流地址
                    if (txtStreamUrl != null)
                    {
                        txtStreamUrl.Text = cameraClient.GetVideoStreamUrl(ip, 0);
                    }
                }
                else
                {
                    cameraClient = null;
                    UpdateConnectionStatus(false);
                    AddLog("相机 " + ip + " 连接失败，请检查网络连接和IP地址");
                    MessageBox.Show("连接失败，请检查网络连接和IP地址！", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            catch (Exception ex)
            {
                cameraClient = null;
                UpdateConnectionStatus(false);
                AddLog("连接异常: " + ex.Message);
                MessageBox.Show("连接异常: " + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                btnConnect.Enabled = true;
                btnConnect.Text = cameraClient != null ? "断开连接" : "连接相机";
            }
        }
        
        // ============================================================
        // 开启拉流按钮点击事件
        // ============================================================
        private async void btnStartRtsp_Click(object sender, EventArgs e)
        {
            await SetRtspEnable(true);
        }
        
        // ============================================================
        // 开启推流按钮点击事件
        // ============================================================
        private async void btnStartRtmp_Click(object sender, EventArgs e)
        {
            await SetRtmpEnable(true);
        }
        
        // ============================================================
        // 设置拉流开关
        // ============================================================
        private async Task SetRtspEnable(bool enable)
        {
            if (cameraClient == null)
            {
                MessageBox.Show("请先连接相机！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            
            if (numChannel == null)
                return;
            
            int channel = (int)numChannel.Value;
            
            AddLog((enable ? "开启" : "关闭") + "通道 " + channel + " 拉流...");
            
            try
            {
                string ip = txtIp.Text.Trim();
                
                bool success;
                if (enable)
                {
                    success = await cameraClient.SetRtspEnableAsync(ip, channel);
                }
                else
                {
                    success = await cameraClient.SetRtspDisableAsync(ip, channel);
                }
                
                if (success)
                {
                    AddLog((enable ? "开启" : "关闭") + "通道 " + channel + " 拉流成功！");
                }
                else
                {
                    AddLog((enable ? "开启" : "关闭") + "通道 " + channel + " 拉流失败");
                }
                
                await RefreshDeviceStatus();
            }
            catch (Exception ex)
            {
                AddLog("操作异常: " + ex.Message);
            }
        }
        
        // ============================================================
        // 设置推流开关
        // ============================================================
        private async Task SetRtmpEnable(bool enable)
        {
            if (cameraClient == null)
            {
                MessageBox.Show("请先连接相机！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            
            if (numChannel == null)
                return;
            
            int channel = (int)numChannel.Value;
            
            AddLog((enable ? "开启" : "关闭") + "通道 " + channel + " 推流...");
            
            try
            {
                string ip = txtIp.Text.Trim();
                
                bool success;
                if (enable)
                {
                    string rtmpUrl = txtStreamUrl?.Text.Trim() ?? "";
                    success = await cameraClient.SetRtmpEnableAsync(ip, channel, rtmpUrl);
                }
                else
                {
                    success = await cameraClient.SetRtmpDisableAsync(ip, channel);
                }
                
                if (success)
                {
                    AddLog((enable ? "开启" : "关闭") + "通道 " + channel + " 推流成功！");
                }
                else
                {
                    AddLog((enable ? "开启" : "关闭") + "通道 " + channel + " 推流失败");
                }
                
                await RefreshDeviceStatus();
            }
            catch (Exception ex)
            {
                AddLog("操作异常: " + ex.Message);
            }
        }
        
        // ============================================================
        // 刷新设备状态
        // ============================================================
        private async Task RefreshDeviceStatus()
        {
            if (cameraClient == null)
                return;
            
            AddLog("正在获取设备状态...");
            
            try
            {
                string ip = txtIp.Text.Trim();
                
                // 获取设备状态（使用通用的DeviceStatus类型）
                DeviceStatus status = await cameraClient.GetDeviceStatusAsync(ip);
                
                if (status != null && txtStatusInfo != null)
                {
                    StringBuilder sb = new StringBuilder();
                    sb.AppendLine("=== 设备基本信息 ===");
                    sb.AppendLine("IP地址: " + status.IpAddress);
                    sb.AppendLine("品牌: " + status.Brand);
                    sb.AppendLine("CPU使用率: " + status.CpuUsage.ToString("F1") + "%");
                    sb.AppendLine("内存使用率: " + status.MemoryUsage.ToString("F1") + "%");
                    sb.AppendLine("磁盘总量: " + status.GetFormattedDiskTotal());
                    sb.AppendLine("磁盘可用: " + status.GetFormattedDiskFree());
                    sb.AppendLine("磁盘使用率: " + status.GetDiskUsage().ToString("F1") + "%");
                    sb.AppendLine("录像总数: " + status.TotalVideoCount);
                    
                    sb.AppendLine();
                    sb.AppendLine("=== 带宽信息 ===");
                    sb.AppendLine("当前品牌不支持带宽信息查询");
                    
                    txtStatusInfo.Text = sb.ToString();
                    AddLog("设备状态更新成功");
                }
                else
                {
                    AddLog("获取设备状态失败");
                }
            }
            catch (Exception ex)
            {
                AddLog("获取状态异常: " + ex.Message);
            }
        }
        
        // ============================================================
        // 开始预览按钮点击事件
        // 使用WebBrowser控件直接访问相机预览页面（draw.html）
        // 该页面使用flv.js播放WebSocket FLV流
        // ============================================================
        private void btnStartPreview_Click(object sender, EventArgs e)
        {
            // 如果还没有连接相机，先连接
            if (cameraClient == null)
            {
                MessageBox.Show("请先连接相机！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            
            AddLog("================== 开始预览流程 ==================");
            
            // 开始视频预览（使用WebBrowser加载相机预览页面）
            AddLog("正在启动视频预览...");
            StartVideoPreview();
        }
        
        // ============================================================
        // 停止预览按钮点击事件
        // ============================================================
        private void btnStopPreview_Click(object sender, EventArgs e)
        {
            StopVideoPreview();
        }
        
        // ============================================================
        // 开始视频预览（使用OpenCV捕获RTSP流并在PictureBox显示）
        // ============================================================
        // OpenCV方案优点：
        //  1. 直接获取视频帧，便于YOLO检测处理
        //  2. 检测框直接绘制在图像上，坐标系完全一致
        //  3. 避免LibVLC letterboxing导致的坐标偏移问题
        //  4. 检测和显示使用同一帧，不存在帧不同步问题
        // ============================================================
        private void StartVideoPreview()
        {
            try
            {
                StopVideoPreviewInternal();
                
                string cameraIp = txtIp.Text.Trim();
                if (string.IsNullOrEmpty(cameraIp))
                {
                    cameraIp = AppConfig.Current.Connection.DefaultIp;
                }
                
                int channel = numChannel != null ? (int)numChannel.Value : 0;
                AddLog("正在确保通道 " + channel + " 的RTSP拉流已开启...");
                
                StartVideoPreviewWithOpenCV();
            }
            catch (Exception ex)
            {
                AddLog("视频预览启动失败: " + ex.Message);
                MessageBox.Show("视频预览启动失败: " + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        
        // ============================================================
        // 使用OpenCV播放RTSP流（新方案）
        // ============================================================
        private void StartVideoPreviewWithOpenCV()
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new Action(StartVideoPreviewWithOpenCV));
                return;
            }

            try
            {
                string cameraIp = txtIp.Text.Trim();
                if (string.IsNullOrEmpty(cameraIp))
                {
                    cameraIp = AppConfig.Current.Connection.DefaultIp;
                }
                
                string rtspUrl = txtStreamUrl?.Text.Trim();
                if (string.IsNullOrEmpty(rtspUrl))
                {
                    int channel = numChannel != null ? (int)numChannel.Value : 0;
                    rtspUrl = AppConfig.Current.Stream.GetRtspUrl(cameraIp, channel);
                }
                
                AddLog("正在启动视频预览（OpenCV方案）...");
                AddLog("RTSP流地址: " + rtspUrl);
                
                if (lblVideoTitle != null)
                {
                    lblVideoTitle.Visible = false;
                }
                
                StartYoloDetection(rtspUrl);
                
                AddLog("OpenCV视频预览已启动");
            }
            catch (Exception ex)
            {
                AddLog("OpenCV方案启动失败: " + ex.Message);
                MessageBox.Show("OpenCV播放失败！\n\n请检查：\n1. RTSP流地址是否正确\n2. 相机是否开启视频输出\n3. 网络连接是否正常", 
                    "视频预览失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        
        // ============================================================
        // 初始化YOLO检测器
        // ============================================================
        // 说明：从配置文件读取模型路径和阈值参数，创建YOLO检测器实例
        //       使用接口抽象，方便后续更换不同的YOLO模型或检测算法
        private bool InitializeYoloDetector()
        {
            try
            {
                if (yoloDetector != null)
                {
                    yoloDetector.Dispose();
                }

                string modelPath = AppConfig.Yolo.ModelPath;

                if (!System.IO.File.Exists(modelPath))
                {
                    AddLog($"YOLO模型文件不存在: {modelPath}");
                    return false;
                }

                // 注意：使用Microsoft.ML.OnnxRuntime.Managed包，无需检查native DLL

                yoloDetector = new YoloDetection.YoloV26Detector();
                yoloDetector.ConfidenceThreshold = AppConfig.Yolo.ConfidenceThreshold;
                yoloDetector.NmsThreshold = AppConfig.Yolo.NmsThreshold;

                // 初始化日志管理器（从配置文件读取日志开关设置）
                YoloDetection.LogManager.Initialize(
                    enableYoloLog: AppConfig.Yolo.YoloDebugLog,
                    enableGeneralLog: true,
                    enableDetectionResultLog: AppConfig.Yolo.DetectionResultLog,
                    logWriter: msg => AddLog(msg)
                );
                AddLog(YoloDetection.LogManager.GetStatusDescription());

                // 注入诊断日志回调（让检测器内部日志可见）
                // YoloV26Detector内部已集成LogManager，这里的设置会覆盖默认行为
                // 如果需要绕过LogManager直接输出，可以设置为 AddLog
                YoloDetection.YoloV26Detector.DiagnosticLogger = msg => AddLog(msg);

                yoloDetector.Initialize(modelPath);

                AddLog("YOLO检测器初始化成功");
                return true;
            }
            catch (Exception ex)
            {
                AddLog("YOLO检测器初始化失败: " + ex.Message);
                if (ex.InnerException != null)
                {
                    AddLog("内部异常: " + ex.InnerException.Message);
                }
                return false;
            }
        }
        
        // ============================================================
        // 启动YOLO检测
        // ============================================================
        // 启动流程说明：
        // 1. 检查YOLO是否启用（配置文件中设置）
        // 2. 如果检测器未初始化，先初始化
        // 3. 创建检测服务（传入检测器和可视化器）
        // 4. 订阅检测服务的事件（DetectionsUpdated、FrameReady）
        // 5. 启动检测服务
        // 6. 创建帧捕获器并启动（连接RTSP流）
        // 
        // 数据流：
        // RTSP流 → RtspFrameCapturer（捕获帧）→ YoloDetectionService（检测+绘制）→ PictureBox（显示）
        // 
        // 可视化器选择：
        // - OpenCVVisualizer：绿色检测框（默认）
        // - YoloBuiltinVisualizer：红色检测框
        // 
        // 切换可视化器方法（运行时切换）：
        // yoloDetectionService.SwitchVisualizer(YoloDetection.VisualizerType.YoloBuiltin);
        // yoloDetectionService.SwitchVisualizer(YoloDetection.VisualizerType.OpenCV);
        // ============================================================
        private void StartYoloDetection(string rtspUrl)
        {
            // 检查是否需要跨线程调用（WinForms要求UI操作在主线程）
            if (this.InvokeRequired)
            {
                this.Invoke(new Action<string>(StartYoloDetection), rtspUrl);
                return;
            }

            // 1. 检查YOLO是否启用（从配置文件读取）
            if (!AppConfig.Yolo.Enabled)
            {
                return;
            }
            
            // 2. 如果检测器未初始化，先初始化
            if (yoloDetector == null && !InitializeYoloDetector())
            {
                return;
            }
            
            // 3. 如果检测服务已存在，先释放旧的
            if (yoloDetectionService != null)
            {
                yoloDetectionService.Dispose();
            }
            
            // 4. 创建可视化器（选择绘制方案）
            // 这里使用OpenCVVisualizer（绿色框），也可以使用YoloBuiltinVisualizer（红色框）
            var visualizer = new YoloDetection.OpenCVVisualizer();

            // 5. 创建检测服务（传入检测器和可视化器）
            yoloDetectionService = new YoloDetection.YoloDetectionService(yoloDetector, visualizer);

            // 6. 设置检测参数（从配置文件读取）
            // ConfidenceThreshold：置信度阈值，只有高于这个值的检测才保留
            // NmsThreshold：非极大值抑制阈值，用于去除重叠的检测框
            yoloDetectionService.ConfidenceThreshold = AppConfig.Yolo.ConfidenceThreshold;
            yoloDetectionService.NmsThreshold = AppConfig.Yolo.NmsThreshold;

            // 7. 订阅检测服务的事件
            // - DetectionsUpdated：检测完成时触发，返回检测结果列表
            // - FrameProcessed：绘制完成时触发，返回带检测框的Mat图像
            // 重要：保持同步检测确保检测框和画面完全对齐（准确性优先）
            yoloDetectionService.DetectionsUpdated += YoloDetectionService_DetectionsUpdated;
            yoloDetectionService.FrameProcessed += YoloDetectionService_FrameProcessed;

            // 8. 启动检测服务（创建检测线程）
            yoloDetectionService.Start();

            // 9. 如果帧捕获器已存在，先释放旧的
            if (rtspFrameCapturer != null)
            {
                rtspFrameCapturer.Dispose();
            }

            // 10. 创建帧捕获器（独立于检测服务，实现解耦）
            // 帧捕获器负责读取RTSP帧，通过FrameReady事件传递给检测服务
            // 检测服务同步完成推理+绘制，检测框与画面严格同步
            rtspFrameCapturer = new YoloDetection.RtspFrameCapturer();
            rtspFrameCapturer.FrameReady += RtspFrameCapturer_FrameReady;

            // 11. 启动帧捕获器（连接RTSP流）
            if (rtspFrameCapturer.Start(rtspUrl))
            {
                AddLog("RTSP帧捕获器已启动");
            }
            else
            {
                AddLog("RTSP帧捕获器启动失败");
            }

            AddLog("YOLO检测已启动");
        }
        
        // ============================================================
        // 停止YOLO检测
        // ============================================================
        // 停止流程说明：
        // 1. 停止帧捕获器（断开RTSP流连接）
        // 2. 取消订阅检测服务的事件（防止内存泄漏）
        // 3. 释放检测服务资源（停止检测线程）
        // 4. 释放PictureBox中的图像（防止内存泄漏）
        // ============================================================
        private void StopYoloDetection()
        {
            // 1. 停止帧捕获器（断开RTSP流连接）
            if (rtspFrameCapturer != null)
            {
                // 取消订阅事件（非常重要！否则会导致内存泄漏）
                rtspFrameCapturer.FrameReady -= RtspFrameCapturer_FrameReady;

                // 释放帧捕获器资源
                rtspFrameCapturer.Dispose();
                rtspFrameCapturer = null;
            }

            // 2. 停止检测服务
            if (yoloDetectionService != null)
            {
                // 取消订阅事件（非常重要！否则会导致内存泄漏）
                yoloDetectionService.DetectionsUpdated -= YoloDetectionService_DetectionsUpdated;
                yoloDetectionService.FrameProcessed -= YoloDetectionService_FrameProcessed;

                // 释放检测服务（停止检测线程）
                yoloDetectionService.Dispose();
                yoloDetectionService = null;
            }

            // 3. 释放PictureBox中的图像（防止内存泄漏）
            if (videoPictureBox != null && videoPictureBox.Image != null)
            {
                videoPictureBox.Image.Dispose();
                videoPictureBox.Image = null;
            }

            AddLog("YOLO检测已停止");
        }
        
        // ============================================================
        // YOLO检测结果更新事件处理方法
        // ============================================================
        // 当YOLO检测完成后，检测服务会触发DetectionsUpdated事件，
        // 这个方法就是事件处理程序，负责处理检测结果。
        // 
        // 检测结果包含：
        // - ClassId：类别ID（如0=person, 1=bicycle等）
        // - ClassName：类别名称
        // - Confidence：置信度（0~1，越大越可信）
        // - X/Y：检测框中心点坐标
        // - Left/Top：检测框左上角坐标
        // - Width/Height：检测框宽度和高度
        // ============================================================
        private int yoloDetectionCount = 0;  // 检测次数计数器

        private void YoloDetectionService_DetectionsUpdated(object sender, List<YoloDetection.DetectionResult> detections)
        {
            // 保存检测结果（供其他地方使用）
            lastYoloDetections = detections ?? new List<YoloDetection.DetectionResult>();
            
            // 增加检测次数
            yoloDetectionCount++;

            // 如果检测到目标，输出详细日志（由LogManager控制开关）
            if (lastYoloDetections.Count > 0)
            {
                // 取第一个检测结果作为示例
                var d = lastYoloDetections[0];
                
                // 输出日志：检测次数、目标数量、类别、置信度、位置、大小
                // 使用DetectionResultLog方法，由LogManager.EnableDetectionResultLog控制
                // 默认关闭，避免每帧输出导致画面卡顿
                YoloDetection.LogManager.DetectionResultLog($"★检测#{yoloDetectionCount}: {lastYoloDetections.Count}个, " +
                       $"cls={d.ClassId}({d.ClassName}) conf={d.Confidence:F3} " +
                       $"pos=({d.X:F0},{d.Y:F0}) size={d.Width:F0}x{d.Height:F0}");
            }
            // 如果没有检测到目标，每隔一段时间输出一次日志（由LogManager控制开关）
            else if (yoloDetectionCount <= 5 || yoloDetectionCount % 30 == 0)
            {
                YoloDetection.LogManager.DetectionResultLog($"☆帧#{yoloDetectionCount}: 0个目标(阈值={AppConfig.Yolo.ConfidenceThreshold})");
            }
        }
        
        // ============================================================
        // YOLO检测完成后帧绘制事件处理方法（v3.0 使用Mat格式，支持跨平台）
        // ============================================================
        // 当YOLO检测服务完成一帧的检测和绘制后，会触发FrameProcessed事件，
        // 这个方法就是事件处理程序，负责在PictureBox中显示带检测框的图像。
        //
        // v3.0 架构优化：
        // 1. 检测服务返回Mat格式，不再绑定WinForms的Bitmap
        // 2. 通过IDetectionVisualizer接口实现绘制逻辑解耦
        // 3. 支持运行时切换检测器和可视化器
        //
        // 关键要点：
        // 1. 线程安全检查：检测服务在后台线程运行，更新UI必须在主线程
        // 2. 资源管理：更新图像前必须释放旧图像，否则会内存泄漏
        // 3. Mat转Bitmap使用高性能的LockBits+CopyMemory方式
        // ============================================================
        private void YoloDetectionService_FrameProcessed(object sender, OpenCvSharp.Mat frame)
        {
            // 1. 安全检查：PictureBox或图像为空则直接返回
            if (videoPictureBox == null || frame == null || frame.Empty())
                return;

            // 2. 线程安全检查：检查当前线程是否是创建PictureBox的线程（主线程）
            // WinForms要求所有UI操作必须在主线程执行
            if (videoPictureBox.InvokeRequired)
            {
                videoPictureBox.BeginInvoke(new Action<object, OpenCvSharp.Mat>(YoloDetectionService_FrameProcessed), sender, frame);
                return;
            }

            // 3. 释放旧图像（非常重要！否则会导致内存泄漏）
            if (videoPictureBox.Image != null)
            {
                videoPictureBox.Image.Dispose();
            }

            // 4. 将Mat转换为Bitmap并显示
            // 使用IDetectionVisualizer中定义的高性能转换方法
            videoPictureBox.Image = YoloDetection.MatExtensions.MatToBitmap(frame);

            // 5. 释放Mat对象（非常重要！否则会内存泄漏）
            frame.Dispose();
        }

        // ============================================================
        // RTSP帧捕获器帧就绪事件处理方法（v3.0 新增，实现解耦）
        // ============================================================
        // 当RtspFrameCapturer捕获到新帧时触发，将帧传递给检测服务
        // 这是帧数据流向检测服务的入口点
        // ============================================================
        private void RtspFrameCapturer_FrameReady(object sender, OpenCvSharp.Mat frame)
        {
            // 将帧传递给检测服务处理
            if (yoloDetectionService != null && yoloDetectionService.IsRunning)
            {
                yoloDetectionService.ProcessFrame(frame);
            }
            else
            {
                // 如果检测服务未运行，直接显示原始帧
                if (videoPictureBox != null && !frame.Empty())
                {
                    if (videoPictureBox.InvokeRequired)
                    {
                        videoPictureBox.BeginInvoke(new Action<OpenCvSharp.Mat>(DisplayRawFrame), frame);
                    }
                    else
                    {
                        DisplayRawFrame(frame);
                    }
                }
            }
        }

        // ============================================================
        // 显示原始帧（无检测框）
        // ============================================================
        private void DisplayRawFrame(OpenCvSharp.Mat frame)
        {
            if (videoPictureBox == null || frame == null || frame.Empty())
                return;

            if (videoPictureBox.Image != null)
            {
                videoPictureBox.Image.Dispose();
            }

            videoPictureBox.Image = YoloDetection.MatExtensions.MatToBitmap(frame);
            frame.Dispose();
        }

        // ============================================================
        // 停止视频预览（内部方法，不记录日志）
        // ============================================================
        private void StopVideoPreviewInternal()
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new Action(StopVideoPreviewInternal));
                return;
            }
            
            if (lblVideoTitle != null)
            {
                lblVideoTitle.Visible = true;
            }
        }
        
        // ============================================================
        // 停止视频预览（公共方法）
        // ============================================================
        private void StopVideoPreview()
        {
            StopYoloDetection();
            StopVideoPreviewInternal();
            
            AddLog("视频预览已停止");
            AddLog("================== 预览流程结束 ==================");
        }
        
        // ============================================================
        // 测试视频流按钮点击事件
        // ============================================================
        private async void btnTestStream_Click(object sender, EventArgs e)
        {
            if (txtStreamUrl == null)
                return;
            
            string url = txtStreamUrl.Text.Trim();
            if (string.IsNullOrEmpty(url))
            {
                MessageBox.Show("请输入视频流地址！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            
            AddLog("正在测试视频流: " + url);
            
            try
            {
                using (HttpClient client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(5);
                    HttpResponseMessage response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                    AddLog("测试结果: HTTP状态码 " + (int)response.StatusCode);
                    
                    if (response.Content.Headers.ContentType != null)
                    {
                        AddLog("Content-Type: " + response.Content.Headers.ContentType);
                    }
                }
            }
            catch (Exception ex)
            {
                AddLog("测试失败: " + ex.Message);
                MessageBox.Show("测试失败: " + ex.Message, "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
        
        // ============================================================
        // 定时器事件
        // ============================================================
        private async void StatusTimer_Tick(object sender, EventArgs e)
        {
            if (cameraClient != null)
            {
                await RefreshDeviceStatus();
            }
        }
        
        // ============================================================
        // 窗体关闭事件
        // ============================================================
        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            StopVideoPreviewInternal();

            StopYoloDetection();
            yoloDetector?.Dispose();

            statusTimer.Stop();
            statusTimer.Dispose();

            if (videoPictureBox != null)
            {
                videoPictureBox.Image?.Dispose();
                videoPictureBox.Dispose();
            }

            Logger.Close();
        }
    }
}