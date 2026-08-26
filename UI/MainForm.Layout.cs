using System.Drawing;
using Sunny.UI;
using YoloDetector.Configuration;

namespace YoloDetector.UI
{
    /// <summary>
    /// 主窗体布局代码（partial 拆分自 MainForm，SunnyUI 小清新风格）。
    ///
    /// 整体布局（ASCII 图，AI 改界面必读）：
    /// ┌──────────────────────────────────────────────────────────────┐
    /// │ headerBar（天蓝 #54B2EC，Dock=Top，h=56）＝ 菜单栏            │
    /// │   ● YOLO 实时人员检测系统              lblStatus(连接状态)     │
    /// ├──────────────┬───────────────────────────────────────────────┤
    /// │ leftPanel    │ videoPanel（黑底视频区，72%）                  │
    /// │ (浅蓝灰底)   │   └ videoPictureBox(Zoom) + lblVideoTitle     │
    /// │ ┌卡片①相机连接┐                                              │
    /// │ │lblTitle     │───────────────────────────────────────────────│
    /// │ │lblIp txtIp  │ logPanel（25%）                               │
    /// │ │btnConnect   │   ├ lblLog                                    │
    /// │ └────────────┘   └ txtLog(UILogView，自动限500行)            │
    /// │ ┌卡片②视频流──┐                                              │
    /// │ │lblStream    │                                               │
    /// │ │lblUrl       │                                               │
    /// │ │txtStreamUrl │                                               │
    /// │ └────────────┘                                               │
    /// │ ┌卡片③推拉流──┐                                              │
    /// │ │lblControl   │                                               │
    /// │ │通道 numChn  │                                               │
    /// │ │[开始预览][停止预览]                                          │
    /// │ │[开启拉流][开启推流]                                          │
    /// │ └────────────┘                                               │
    /// │ ┌卡片④设备状态(Fill)                                         │
    /// │ │lblInfo      │                                               │
    /// │ │txtStatusInfo│                                               │
    /// │ └────────────┘                                               │
    /// └──────────────┴───────────────────────────────────────────────┘
    ///
    /// 配色约定（小清新色板，改色统一在这里找）：
    ///   主色天蓝      #54B2EC  顶栏/主按钮
    ///   主色 hover    #7AC6F1 / press #3D9FDB
    ///   页面底色      #EEF4F8  左栏与整体背景
    ///   卡片白        #FFFFFF  边框 #DCE7EE
    ///   标题字        #35505F  正文 #5E7A89
    ///   成功绿 #58C28E / 危险红 #ED7168 / 警示橙 #F2A65A
    ///
    /// 控件选型说明：
    ///   - 按钮全部 UIButton（圆角、悬停变色）；输入框 UITextBox（含水印提示）
    ///   - 日志区原生 TextBox（SunnyUI 3.9.8 已移除 UILogView），
    ///     500 行自动裁剪逻辑在 MainForm.AppendLogToPanel 中
    ///   - 视频 PictureBox 与设备状态 TextBox 保留原生控件：
    ///     前者是逐帧显示的性能关键路径，后者只需多行只读文本
    /// </summary>
    public partial class MainForm
    {
        // ---- 小清新色板（集中定义，改主题色只需要动这里）----
        private static readonly Color PrimaryColor = Color.FromArgb(84, 178, 236);    // 天蓝主色
        private static readonly Color PrimaryHover = Color.FromArgb(122, 198, 241);   // 悬停浅一档
        private static readonly Color PrimaryPress = Color.FromArgb(61, 159, 219);    // 按下深一档
        private static readonly Color PageBackColor = Color.FromArgb(238, 244, 248);  // 页面淡蓝灰底
        private static readonly Color CardBorderColor = Color.FromArgb(220, 231, 238);// 卡片描边
        private static readonly Color TitleTextColor = Color.FromArgb(53, 80, 95);    // 分组标题字
        private static readonly Color BodyTextColor = Color.FromArgb(94, 122, 137);   // 正文字
        private static readonly Color SuccessGreen = Color.FromArgb(88, 194, 142);    // 开始类按钮
        private static readonly Color DangerRed = Color.FromArgb(237, 113, 104);      // 停止类按钮

        private void InitializeComponent()
        {
            this.headerBar = new System.Windows.Forms.Panel();
            this.lblAppTitle = new UILabel();
            this.lblStatus = new UILabel();
            this.layoutTable = new System.Windows.Forms.TableLayoutPanel();
            this.leftPanel = new System.Windows.Forms.Panel();
            this.cardConnect = new UIPanel();
            this.lblTitle = new UILabel();
            this.lblIp = new UILabel();
            this.txtIp = new UITextBox();
            this.btnConnect = new UIButton();
            this.cardStream = new UIPanel();
            this.lblStream = new UILabel();
            this.lblUrl = new UILabel();
            this.txtStreamUrl = new UITextBox();
            this.cardControl = new UIPanel();
            this.lblControl = new UILabel();
            this.lblChannel = new UILabel();
            this.numChannel = new UIIntegerUpDown();
            this.btnStartPreview = new UIButton();
            this.btnStopPreview = new UIButton();
            this.btnStartRtsp = new UIButton();
            this.btnStartRtmp = new UIButton();
            this.cardInfo = new UIPanel();
            this.lblInfo = new UILabel();
            this.txtStatusInfo = new System.Windows.Forms.TextBox();
            this.rightPanel = new System.Windows.Forms.Panel();
            this.rightTable = new System.Windows.Forms.TableLayoutPanel();
            this.videoPanel = new System.Windows.Forms.Panel();
            this.videoPictureBox = new RoiSelectionPictureBox();
            this.lblVideoTitle = new UILabel();
            this.logPanel = new UIPanel();
            this.lblLog = new UILabel();
            this.txtLog = new System.Windows.Forms.TextBox();
            this.headerBar.SuspendLayout();
            this.layoutTable.SuspendLayout();
            this.leftPanel.SuspendLayout();
            this.cardConnect.SuspendLayout();
            this.cardStream.SuspendLayout();
            this.cardControl.SuspendLayout();
            this.cardInfo.SuspendLayout();
            this.rightPanel.SuspendLayout();
            this.rightTable.SuspendLayout();
            this.videoPanel.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.videoPictureBox)).BeginInit();
            this.logPanel.SuspendLayout();
            this.SuspendLayout();
            //
            // headerBar —— 顶部天蓝色菜单栏（用户要求的视觉核心）
            //
            this.headerBar.BackColor = PrimaryColor;
            this.headerBar.Controls.Add(this.lblAppTitle);
            this.headerBar.Controls.Add(this.lblStatus);
            this.headerBar.Dock = System.Windows.Forms.DockStyle.Top;
            this.headerBar.Location = new System.Drawing.Point(0, 0);
            this.headerBar.Name = "headerBar";
            this.headerBar.Size = new System.Drawing.Size(1280, 56);
            this.headerBar.TabIndex = 0;
            //
            // lblAppTitle —— 应用名（顶栏左侧）
            //
            this.lblAppTitle.AutoSize = true;
            this.lblAppTitle.Font = new Font("微软雅黑", 13F, FontStyle.Bold);
            this.lblAppTitle.ForeColor = Color.White;
            this.lblAppTitle.Location = new System.Drawing.Point(18, 14);
            this.lblAppTitle.Name = "lblAppTitle";
            this.lblAppTitle.Text = "YOLO 实时人员检测系统";
            //
            // lblStatus —— 连接状态徽标（顶栏右侧，Anchor 右对齐；
            // MainForm.UpdateConnectionStatus 会改它的 Text/ForeColor）
            //
            this.lblStatus.AutoSize = true;
            this.lblStatus.Font = new Font("微软雅黑", 10.5F, FontStyle.Bold);
            this.lblStatus.ForeColor = Color.FromArgb(255, 225, 222);
            this.lblStatus.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
            this.lblStatus.Location = new System.Drawing.Point(1120, 18);
            this.lblStatus.Name = "lblStatus";
            this.lblStatus.Text = "● 未连接";
            //
            // layoutTable —— 主体两列：左侧控制面板 / 右侧视频+日志
            //
            this.layoutTable.ColumnCount = 2;
            this.layoutTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 330F));
            this.layoutTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.layoutTable.Controls.Add(this.leftPanel, 0, 0);
            this.layoutTable.Controls.Add(this.rightPanel, 1, 0);
            this.layoutTable.Dock = System.Windows.Forms.DockStyle.Fill;
            this.layoutTable.Location = new System.Drawing.Point(0, 56);
            this.layoutTable.Name = "layoutTable";
            this.layoutTable.RowCount = 1;
            this.layoutTable.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.layoutTable.Size = new System.Drawing.Size(1280, 744);
            this.layoutTable.TabIndex = 1;
            //
            // leftPanel —— 左栏容器（淡蓝灰底）
            //
            this.leftPanel.AutoScroll = true;
            this.leftPanel.BackColor = PageBackColor;
            this.leftPanel.Controls.Add(this.cardConnect);
            this.leftPanel.Controls.Add(this.cardStream);
            this.leftPanel.Controls.Add(this.cardControl);
            this.leftPanel.Controls.Add(this.cardInfo);
            this.leftPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.leftPanel.Name = "leftPanel";
            this.leftPanel.Padding = new System.Windows.Forms.Padding(12, 10, 12, 10);
            this.leftPanel.Size = new System.Drawing.Size(330, 744);
            //
            // cardConnect —— 卡片①：相机连接
            //
            StyleCard(this.cardConnect, "cardConnect");
            this.cardConnect.Controls.Add(this.lblTitle);
            this.cardConnect.Controls.Add(this.lblIp);
            this.cardConnect.Controls.Add(this.txtIp);
            this.cardConnect.Controls.Add(this.btnConnect);
            this.cardConnect.Location = new System.Drawing.Point(12, 10);
            this.cardConnect.Size = new System.Drawing.Size(306, 152);
            //
            // lblTitle
            //
            StyleCardTitle(this.lblTitle, "相机连接");
            this.lblTitle.Location = new System.Drawing.Point(16, 14);
            //
            // lblIp —— 与 txtIp（y=54,h=32,中心70）垂直居中：9.5F 标签渲染高约18，y=70-9=61
            //
            StyleBodyLabel(this.lblIp, "相机IP");
            this.lblIp.Location = new System.Drawing.Point(16, 61);
            //
            // txtIp —— IP 输入框（带水印示例）
            //
            this.txtIp.Font = new Font("微软雅黑", 10F);
            this.txtIp.Location = new System.Drawing.Point(78, 54);
            this.txtIp.Name = "txtIp";
            this.txtIp.Size = new System.Drawing.Size(212, 32);
            this.txtIp.Text = AppConfig.Current.Connection.DefaultIp;
            this.txtIp.Watermark = "如 192.168.0.15";
            //
            // btnConnect —— 主操作按钮（天蓝大按钮）
            //
            StylePrimaryButton(this.btnConnect);
            this.btnConnect.Font = new Font("微软雅黑", 10.5F);
            this.btnConnect.Location = new System.Drawing.Point(16, 100);
            this.btnConnect.Name = "btnConnect";
            this.btnConnect.Size = new System.Drawing.Size(274, 36);
            this.btnConnect.TabIndex = 1;
            this.btnConnect.Text = "连接相机";
            this.btnConnect.Click += new System.EventHandler(this.btnConnect_Click);
            //
            // cardStream —— 卡片②：视频流配置
            //
            StyleCard(this.cardStream, "cardStream");
            this.cardStream.Controls.Add(this.lblStream);
            this.cardStream.Controls.Add(this.lblUrl);
            this.cardStream.Controls.Add(this.txtStreamUrl);
            this.cardStream.Location = new System.Drawing.Point(12, 172);
            this.cardStream.Size = new System.Drawing.Size(306, 100);
            //
            // lblStream
            //
            StyleCardTitle(this.lblStream, "视频流配置");
            this.lblStream.Location = new System.Drawing.Point(16, 14);
            //
            // lblUrl —— 与 txtStreamUrl（y=50,h=32,中心66）垂直居中：y=66-9=57
            //
            StyleBodyLabel(this.lblUrl, "流地址");
            this.lblUrl.Location = new System.Drawing.Point(16, 57);
            //
            // txtStreamUrl
            //
            this.txtStreamUrl.Font = new Font("微软雅黑", 9F);
            this.txtStreamUrl.Location = new System.Drawing.Point(78, 50);
            this.txtStreamUrl.Name = "txtStreamUrl";
            this.txtStreamUrl.Size = new System.Drawing.Size(212, 32);
            this.txtStreamUrl.Text = AppConfig.Current.Stream.GetRtspUrl(AppConfig.Current.Connection.DefaultIp, 0);
            this.txtStreamUrl.Watermark = "rtsp://ip:554/stream0";
            //
            // cardControl —— 卡片③：预览与推拉流控制
            //
            StyleCard(this.cardControl, "cardControl");
            this.cardControl.Controls.Add(this.lblControl);
            this.cardControl.Controls.Add(this.lblChannel);
            this.cardControl.Controls.Add(this.numChannel);
            this.cardControl.Controls.Add(this.btnStartPreview);
            this.cardControl.Controls.Add(this.btnStopPreview);
            this.cardControl.Controls.Add(this.btnStartRtsp);
            this.cardControl.Controls.Add(this.btnStartRtmp);
            this.cardControl.Location = new System.Drawing.Point(12, 282);
            this.cardControl.Size = new System.Drawing.Size(306, 196);
            //
            // lblControl
            //
            StyleCardTitle(this.lblControl, "预览与推拉流");
            this.lblControl.Location = new System.Drawing.Point(16, 14);
            //
            // lblChannel —— 与 numChannel（y=54,h=32,中心70）垂直居中：y=70-9=61
            //
            StyleBodyLabel(this.lblChannel, "通道");
            this.lblChannel.Location = new System.Drawing.Point(16, 61);
            //
            // numChannel —— 通道选择（上限取自品牌配置 MaxChannel）
            //
            this.numChannel.Font = new Font("微软雅黑", 10F);
            this.numChannel.Location = new System.Drawing.Point(66, 54);
            // UIIntegerUpDown 的 Maximum 为 Double 类型，直接隐式转换即可
            this.numChannel.Maximum = AppConfig.Current.Stream.MaxChannel;
            this.numChannel.Name = "numChannel";
            this.numChannel.Size = new System.Drawing.Size(100, 32);
            this.numChannel.Value = 0;
            //
            // btnStartPreview —— 绿色系开始按钮（2×2 网格排布）
            //
            StyleButton(this.btnStartPreview, SuccessGreen);
            this.btnStartPreview.Font = new Font("微软雅黑", 9.5F);
            this.btnStartPreview.Location = new System.Drawing.Point(16, 102);
            this.btnStartPreview.Name = "btnStartPreview";
            this.btnStartPreview.Size = new System.Drawing.Size(136, 36);
            this.btnStartPreview.TabIndex = 1;
            this.btnStartPreview.Text = "开始预览";
            this.btnStartPreview.Click += new System.EventHandler(this.btnStartPreview_Click);
            //
            // btnStopPreview
            //
            StyleButton(this.btnStopPreview, DangerRed);
            this.btnStopPreview.Font = new Font("微软雅黑", 9.5F);
            this.btnStopPreview.Location = new System.Drawing.Point(160, 102);
            this.btnStopPreview.Name = "btnStopPreview";
            this.btnStopPreview.Size = new System.Drawing.Size(136, 36);
            this.btnStopPreview.TabIndex = 2;
            this.btnStopPreview.Text = "停止预览";
            this.btnStopPreview.Click += new System.EventHandler(this.btnStopPreview_Click);
            //
            // btnStartRtsp
            //
            StyleButton(this.btnStartRtsp, SuccessGreen);
            this.btnStartRtsp.Font = new Font("微软雅黑", 9.5F);
            this.btnStartRtsp.Location = new System.Drawing.Point(16, 148);
            this.btnStartRtsp.Name = "btnStartRtsp";
            this.btnStartRtsp.Size = new System.Drawing.Size(136, 36);
            this.btnStartRtsp.TabIndex = 3;
            this.btnStartRtsp.Text = "开启拉流";
            this.btnStartRtsp.Click += new System.EventHandler(this.btnStartRtsp_Click);
            //
            // btnStartRtmp
            //
            StyleButton(this.btnStartRtmp, PrimaryColor);
            this.btnStartRtmp.Font = new Font("微软雅黑", 9.5F);
            this.btnStartRtmp.Location = new System.Drawing.Point(160, 148);
            this.btnStartRtmp.Name = "btnStartRtmp";
            this.btnStartRtmp.Size = new System.Drawing.Size(136, 36);
            this.btnStartRtmp.TabIndex = 4;
            this.btnStartRtmp.Text = "开启推流";
            this.btnStartRtmp.Click += new System.EventHandler(this.btnStartRtmp_Click);
            //
            // cardInfo —— 卡片④：设备状态信息（占满剩余高度）
            //
            StyleCard(this.cardInfo, "cardInfo");
            this.cardInfo.Controls.Add(this.lblInfo);
            this.cardInfo.Controls.Add(this.txtStatusInfo);
            this.cardInfo.Location = new System.Drawing.Point(12, 488);
            this.cardInfo.Size = new System.Drawing.Size(306, 246);
            this.cardInfo.Anchor = System.Windows.Forms.AnchorStyles.Top |
                System.Windows.Forms.AnchorStyles.Bottom |
                System.Windows.Forms.AnchorStyles.Left |
                System.Windows.Forms.AnchorStyles.Right;
            //
            // lblInfo
            //
            StyleCardTitle(this.lblInfo, "设备状态");
            this.lblInfo.Location = new System.Drawing.Point(16, 14);
            //
            // txtStatusInfo —— 多行只读文本（保留原生 TextBox，仅调外观）
            //
            this.txtStatusInfo.Anchor = System.Windows.Forms.AnchorStyles.Top |
                System.Windows.Forms.AnchorStyles.Bottom |
                System.Windows.Forms.AnchorStyles.Left |
                System.Windows.Forms.AnchorStyles.Right;
            this.txtStatusInfo.BackColor = Color.White;
            this.txtStatusInfo.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.txtStatusInfo.Font = new Font("Consolas", 9F);
            this.txtStatusInfo.ForeColor = TitleTextColor;
            this.txtStatusInfo.Location = new System.Drawing.Point(18, 50);
            this.txtStatusInfo.Multiline = true;
            this.txtStatusInfo.Name = "txtStatusInfo";
            this.txtStatusInfo.ReadOnly = true;
            this.txtStatusInfo.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.txtStatusInfo.Size = new System.Drawing.Size(270, 134);
            //
            // rightPanel —— 右侧容器
            //
            this.rightPanel.Controls.Add(this.rightTable);
            this.rightPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.rightPanel.Name = "rightPanel";
            this.rightPanel.Padding = new System.Windows.Forms.Padding(0, 10, 12, 10);
            this.rightPanel.Size = new System.Drawing.Size(950, 744);
            //
            // rightTable —— 上 75% 视频 / 下 25% 日志
            //
            this.rightTable.ColumnCount = 1;
            this.rightTable.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
            this.rightTable.Controls.Add(this.videoPanel, 0, 0);
            this.rightTable.Controls.Add(this.logPanel, 0, 1);
            this.rightTable.Dock = System.Windows.Forms.DockStyle.Fill;
            this.rightTable.Name = "rightTable";
            this.rightTable.RowCount = 2;
            this.rightTable.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 72F));
            this.rightTable.RowStyles.Add(new System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 28F));
            this.rightTable.Size = new System.Drawing.Size(938, 724);
            //
            // videoPanel —— 黑底视频区
            //
            this.videoPanel.BackColor = Color.Black;
            this.videoPanel.Controls.Add(this.videoPictureBox);
            this.videoPanel.Controls.Add(this.lblVideoTitle);
            this.videoPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.videoPanel.Name = "videoPanel";
            this.videoPanel.Padding = new System.Windows.Forms.Padding(0, 0, 0, 6);
            this.videoPanel.Size = new System.Drawing.Size(938, 517);
            //
            // videoPictureBox —— 逐帧显示 + 内置拖拽框选标定（RoiSelectionPictureBox：
            // 鼠标接线/虚线框/坐标换算全在控件内，Cross 光标为其默认值，勿换原生 PictureBox）
            //
            this.videoPictureBox.Dock = System.Windows.Forms.DockStyle.Fill;
            this.videoPictureBox.Name = "videoPictureBox";
            this.videoPictureBox.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Zoom;
            this.videoPictureBox.TabStop = false;
            //
            // lblVideoTitle —— 未开流时的占位提示
            //
            this.lblVideoTitle.AutoSize = true;
            this.lblVideoTitle.Font = new Font("微软雅黑", 12F, FontStyle.Bold);
            this.lblVideoTitle.ForeColor = Color.FromArgb(150, 150, 150);
            this.lblVideoTitle.Location = new System.Drawing.Point(14, 12);
            this.lblVideoTitle.Name = "lblVideoTitle";
            this.lblVideoTitle.Text = "视频预览区域";
            //
            // logPanel —— 日志卡片
            //
            this.logPanel.FillColor = Color.White;
            this.logPanel.RectColor = CardBorderColor;
            this.logPanel.Radius = 8;
            this.logPanel.Controls.Add(this.lblLog);
            this.logPanel.Controls.Add(this.txtLog);
            this.logPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.logPanel.Name = "logPanel";
            this.logPanel.Padding = new System.Windows.Forms.Padding(6, 2, 6, 6);
            this.logPanel.Size = new System.Drawing.Size(938, 201);
            //
            // lblLog
            //
            this.lblLog.AutoSize = true;
            this.lblLog.Font = new Font("微软雅黑", 10F, FontStyle.Bold);
            this.lblLog.ForeColor = TitleTextColor;
            this.lblLog.Location = new System.Drawing.Point(14, 8);
            this.lblLog.Name = "lblLog";
            this.lblLog.Text = "运行日志";
            //
            // txtLog —— 运行日志（原生 TextBox：SunnyUI 3.9.8 已移除 UILogView 控件；
            // 行数上限裁剪逻辑在 MainForm.AppendLogToPanel 中实现，防长期运行内存增长）
            //
            this.txtLog.Anchor = System.Windows.Forms.AnchorStyles.Top |
                System.Windows.Forms.AnchorStyles.Bottom |
                System.Windows.Forms.AnchorStyles.Left |
                System.Windows.Forms.AnchorStyles.Right;
            this.txtLog.BackColor = Color.White;
            this.txtLog.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.txtLog.Font = new Font("Consolas", 9F);
            this.txtLog.ForeColor = TitleTextColor;
            this.txtLog.Location = new System.Drawing.Point(16, 34);
            this.txtLog.Multiline = true;
            this.txtLog.Name = "txtLog";
            this.txtLog.ReadOnly = true;
            this.txtLog.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.txtLog.Size = new System.Drawing.Size(906, 159);
            //
            // MainForm
            //
            this.ClientSize = new Size(1280, 800);
            this.MinimumSize = new Size(1160, 740);
            this.Controls.Add(this.layoutTable);
            this.Controls.Add(this.headerBar);
            this.Font = new Font("微软雅黑", 9F);
            this.Name = "MainForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "YOLO 实时人员检测系统";
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.MainForm_FormClosing);
            this.headerBar.ResumeLayout(false);
            this.headerBar.PerformLayout();
            this.layoutTable.ResumeLayout(false);
            this.leftPanel.ResumeLayout(false);
            this.cardConnect.ResumeLayout(false);
            this.cardConnect.PerformLayout();
            this.cardStream.ResumeLayout(false);
            this.cardStream.PerformLayout();
            this.cardControl.ResumeLayout(false);
            this.cardControl.PerformLayout();
            this.cardInfo.ResumeLayout(false);
            this.cardInfo.PerformLayout();
            this.rightPanel.ResumeLayout(false);
            this.rightTable.ResumeLayout(false);
            this.videoPanel.ResumeLayout(false);
            this.videoPanel.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.videoPictureBox)).EndInit();
            this.logPanel.ResumeLayout(false);
            this.logPanel.PerformLayout();
            this.ResumeLayout(false);
        }

        // ==================== 样式辅助方法 ====================
        // Designer 通常不用辅助方法，但手写布局里抽出来可以避免同一套
        // 颜色/圆角参数散落十几处；改主题时只动文件头的色板常量即可。

        /// <summary>统一卡片样式：白底、浅灰描边、8px 圆角</summary>
        private static void StyleCard(UIPanel card, string name)
        {
            card.FillColor = Color.White;
            card.RectColor = CardBorderColor;
            card.Radius = 8;
            card.Name = name;
        }

        /// <summary>分组标题：加粗深蓝灰</summary>
        private static void StyleCardTitle(UILabel label, string text)
        {
            label.AutoSize = true;
            label.Font = new Font("微软雅黑", 11F, FontStyle.Bold);
            label.ForeColor = TitleTextColor;
            label.Text = text;
        }

        /// <summary>字段标签：正文灰</summary>
        private static void StyleBodyLabel(UILabel label, string text)
        {
            label.AutoSize = true;
            label.Font = new Font("微软雅黑", 9.5F);
            label.ForeColor = BodyTextColor;
            label.Text = text;
        }

        /// <summary>纯色圆角按钮：白字 + 悬停提亮 + 按下压暗（属性名对应 SunnyUI 3.9.8）</summary>
        private static void StyleButton(UIButton button, Color baseColor)
        {
            button.FillColor = baseColor;
            button.FillHoverColor = Lighten(baseColor, 0.25f);
            button.FillPressColor = Darken(baseColor, 0.12f);
            button.RectColor = baseColor;
            button.RectHoverColor = Lighten(baseColor, 0.25f);
            button.RectPressColor = Darken(baseColor, 0.12f);
            button.ForeColor = Color.White;
        }

        /// <summary>主操作按钮（天蓝）</summary>
        private static void StylePrimaryButton(UIButton button)
        {
            StyleButton(button, PrimaryColor);
        }

        /// <summary>向 target 颜色混合 f 比例（0~1），用于按钮悬停/按压的明暗渐变</summary>
        private static Color Mix(Color c, Color target, float f)
        {
            return Color.FromArgb(
                c.R + (int)((target.R - c.R) * f),
                c.G + (int)((target.G - c.G) * f),
                c.B + (int)((target.B - c.B) * f));
        }

        private static Color Lighten(Color c, float f) { return Mix(c, Color.White, f); }

        private static Color Darken(Color c, float f) { return Mix(c, Color.Black, f); }

        // ---- 控件字段声明 ----

        private System.Windows.Forms.Panel headerBar;
        private UILabel lblAppTitle;
        private UILabel lblStatus;
        private System.Windows.Forms.TableLayoutPanel layoutTable;
        private System.Windows.Forms.Panel leftPanel;
        private UIPanel cardConnect;
        private UILabel lblTitle;
        private UILabel lblIp;
        private UITextBox txtIp;
        private UIButton btnConnect;
        private UIPanel cardStream;
        private UILabel lblStream;
        private UILabel lblUrl;
        private UITextBox txtStreamUrl;
        private UIPanel cardControl;
        private UILabel lblControl;
        private UILabel lblChannel;
        private UIIntegerUpDown numChannel;
        private UIButton btnStartPreview;
        private UIButton btnStopPreview;
        private UIButton btnStartRtsp;
        private UIButton btnStartRtmp;
        private UIPanel cardInfo;
        private UILabel lblInfo;
        private System.Windows.Forms.TextBox txtStatusInfo;
        private System.Windows.Forms.Panel rightPanel;
        private System.Windows.Forms.TableLayoutPanel rightTable;
        private System.Windows.Forms.Panel videoPanel;
        private RoiSelectionPictureBox videoPictureBox;
        private UILabel lblVideoTitle;
        private UIPanel logPanel;
        private UILabel lblLog;
        private System.Windows.Forms.TextBox txtLog;
    }
}
