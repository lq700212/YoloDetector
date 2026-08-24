using System.Windows.Forms;
using YoloDetector.Configuration;

namespace YoloDetector.UI
{
    /// <summary>
    /// 主窗体布局代码（设计器风格，partial 拆分自 MainForm）。
    /// </summary>
    public partial class MainForm
    {
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

        // ---- 控件字段声明 ----

        private System.Windows.Forms.TableLayoutPanel layoutTable;
        private System.Windows.Forms.Panel connectPanel;
        private System.Windows.Forms.Label lblTitle;
        private System.Windows.Forms.Label lblIp;
        private System.Windows.Forms.TextBox txtIp;
        private System.Windows.Forms.Label lblStatus;
        private System.Windows.Forms.Button btnConnect;
        private System.Windows.Forms.Panel streamPanel;
        private System.Windows.Forms.Label lblStream;
        private System.Windows.Forms.Label lblUrl;
        private System.Windows.Forms.TextBox txtStreamUrl;
        private System.Windows.Forms.Button btnTestStream;
        private System.Windows.Forms.Panel controlPanel;
        private System.Windows.Forms.Label lblControl;
        private System.Windows.Forms.Label lblChannel;
        private System.Windows.Forms.NumericUpDown numChannel;
        private System.Windows.Forms.Button btnStartPreview;
        private System.Windows.Forms.Button btnStopPreview;
        private System.Windows.Forms.Button btnStartRtsp;
        private System.Windows.Forms.Button btnStartRtmp;
        private System.Windows.Forms.Panel infoPanel;
        private System.Windows.Forms.Label lblInfo;
        private System.Windows.Forms.TextBox txtStatusInfo;
        private System.Windows.Forms.Panel rightPanel;
        private System.Windows.Forms.TableLayoutPanel rightTable;
        private System.Windows.Forms.Panel videoPanel;
        private System.Windows.Forms.PictureBox videoPictureBox;
        private System.Windows.Forms.Label lblVideoTitle;
        private System.Windows.Forms.Panel logPanel;
        private System.Windows.Forms.Label lblLog;
        private System.Windows.Forms.TextBox txtLog;
    }
}
