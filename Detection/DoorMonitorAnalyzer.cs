using System;
using System.Collections.Generic;
using OpenCvSharp;

namespace YoloDetection
{
    /// <summary>
    /// 门状态监测分析器：识别工位操作间的门是"开"还是"关"。
    ///
    /// 原理（基准比对，无需训练模型——COCO 80 类里没有 door，目标检测路线不可行）：
    ///   1. 采集"关门基准图"：门关着时把门区域（ROI）裁成灰度小图保存；
    ///   2. 运行时把当前帧的门区域与基准比对"亮度归一化平均绝对差"：
    ///      两图各自减去自身均值后再比——整体明暗漂移（昼夜/开关灯）被消除，
    ///      只剩结构差异（门开了会露出门外的新内容，结构必然大变）；
    ///   3. 差异超阈值 → 候选"开"，否则候选"关"；候选状态持续 StateHoldMs
    ///      才真正翻转（防抖）。
    ///
    /// 误报双保险：
    ///   a) 门 ROI 与任一人体框相交时跳过本帧判定（人走过门前会短暂挡住门，
    ///      结构差异巨大——但那是人不是门；保持上一状态等人体离开）；
    ///   b) 状态翻转需持续 StateHoldMs（默认 1.5 秒）。
    ///
    /// 已知局限（现场调参可缓解，见 DoorMonitorOptions.DiffThreshold 注释）：
    ///   强光照渐变（夕阳直射移动的阴影）可能造成持续误报——此时重设基准即可。
    ///
    /// 线程契约：本类不是线程安全的。Update 只允许检测管道的工作线程串行调用；
    /// SetBaseline 由 UI 线程调用（重设基准按钮）——与 Update 的互斥由宿主保证
    /// （UI 调用前先经 SafeBeginInvoke 落到 UI 线程，检测线程每帧读基准图引用，
    /// 替换是引用赋值原子操作，旧基准图由本类 Dispose 时统一释放）。
    /// </summary>
    public class DoorMonitorAnalyzer
    {
        /// <summary>门状态翻转事件：true=门被打开，false=门已关闭。仅翻转帧触发一次。</summary>
        public event Action<bool> StateChanged;

        private readonly DoorMonitorOptions _options;

        // 关门基准（灰度、ROI 裁剪尺寸）。null = 尚无基准（无法判定）。
        // 检测线程每帧读引用；SetBaseline 原子替换引用，旧图在此处释放
        private Mat _baseline;

        // 当前对外状态：true=门开
        private bool _isOpen;

        // 候选状态防抖：候选与当前状态不同的最早时间戳（-1 = 无待定候选。
        // 不能用 0 当哨兵——nowMs=0 是合法时间戳，会与哨兵冲突导致防抖永不满足）
        private long _pendingSinceMs = -1;

        // 待定的候选状态（与 _pendingSinceMs 配对）
        private bool _pendingOpen;

        public DoorMonitorAnalyzer(DoorMonitorOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            TryLoadBaselineFromFile();
        }

        /// <summary>当前生效的分析参数（只读暴露，便于可视化器取 ROI 绘制）</summary>
        public DoorMonitorOptions Options { get { return _options; } }

        /// <summary>是否已有关门基准（无基准时 Update 不产出状态判定）</summary>
        public bool HasBaseline { get { return _baseline != null && !_baseline.Empty(); } }

        /// <summary>当前门状态：true=开（相对关门基准差异超阈值）。无基准时无意义。</summary>
        public bool IsOpen { get { return _isOpen; } }

        /// <summary>
        /// 设置/重设关门基准：从 frame 裁出门区域灰度图，内存生效并落盘 PNG
        /// （重启后自动加载）。调用时机：门**关着**的时候（UI"重设基准"按钮、
        /// 或首次启用时）。
        /// </summary>
        public void SetBaselineFromFrame(Mat frame)
        {
            if (frame == null || frame.Empty())
            {
                return;
            }

            Rect roi = ComputeRoiPixels(frame.Cols, frame.Rows);
            Mat cropped = new Mat(frame, roi);
            Mat gray = new Mat();
            Cv2.CvtColor(cropped, gray, ColorConversionCodes.BGR2GRAY);

            Mat old = _baseline;
            _baseline = gray;
            if (old != null)
            {
                old.Dispose();
            }
            cropped.Dispose();

            try
            {
                string path = ResolveBaselinePath();
                if (!string.IsNullOrEmpty(path))
                {
                    string dir = System.IO.Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
                    {
                        System.IO.Directory.CreateDirectory(dir);
                    }
                    Cv2.ImWrite(path, _baseline);
                }
            }
            catch (Exception ex)
            {
                // 落盘失败不影响本次运行（内存基准已生效），下次重设可再试
                LogManager.GeneralLog("[Door] 基准图保存失败(内存基准已生效): " + ex.Message);
            }
        }

        /// <summary>
        /// 处理一帧：更新门状态，返回快照。
        /// </summary>
        /// <param name="frame">整帧图像（BGR），方法内只读</param>
        /// <param name="persons">本帧人体框列表（用于排除"人走过遮挡门区域"）</param>
        /// <param name="nowMs">当前时间戳（毫秒），参数化以便单元测试注入虚拟时钟</param>
        public DoorFrameSnapshot Update(Mat frame, List<DetectionResult> persons, long nowMs)
        {
            var snapshot = new DoorFrameSnapshot
            {
                IsOpen = _isOpen,
                HasBaseline = HasBaseline
            };

            if (!HasBaseline || frame == null || frame.Empty())
            {
                return snapshot;
            }

            int frameW = frame.Cols, frameH = frame.Rows;
            Rect roi = ComputeRoiPixels(frameW, frameH);

            // 误报保险 a：门区域被人体框遮挡时跳过判定（人挡住门 ≠ 门开了）
            if (IsRoiOccludedByPerson(roi, persons))
            {
                snapshot.PersonOccluded = true;
                return snapshot;
            }

            // 裁当前帧门区域并转灰度；基准尺寸不同（换分辨率）时缩放对齐
            Mat cropped = new Mat(frame, roi);
            Mat curGray = new Mat();
            Cv2.CvtColor(cropped, curGray, ColorConversionCodes.BGR2GRAY);
            cropped.Dispose();

            float diff;
            try
            {
                Mat baseAligned = _baseline;
                Mat resized = null;
                if (baseAligned.Cols != curGray.Cols || baseAligned.Rows != curGray.Rows)
                {
                    resized = new Mat();
                    Cv2.Resize(baseAligned, resized, new Size(curGray.Cols, curGray.Rows));
                    baseAligned = resized;
                }

                diff = ComputeNormalizedMeanAbsDiff(curGray, baseAligned);
                snapshot.DiffValue = diff;

                resized?.Dispose();
            }
            finally
            {
                curGray.Dispose();
            }

            // 候选状态：差异超阈值 → 门开
            bool candidateOpen = diff > _options.DiffThreshold;

            if (candidateOpen == _isOpen)
            {
                _pendingSinceMs = -1; // 与当前状态一致，清掉待定候选
            }
            else
            {
                // 与当前状态不同：记录候选起始时间（首帧），持续够久才翻转
                if (_pendingSinceMs < 0 || _pendingOpen != candidateOpen)
                {
                    _pendingSinceMs = nowMs;
                    _pendingOpen = candidateOpen;
                }
                else if (nowMs - _pendingSinceMs >= _options.StateHoldMs)
                {
                    _isOpen = candidateOpen;   // 翻转
                    _pendingSinceMs = -1;
                    snapshot.IsOpen = _isOpen;

                    var handler = StateChanged;
                    if (handler != null)
                    {
                        handler(_isOpen);
                    }
                    return snapshot;
                }
            }

            snapshot.IsOpen = _isOpen;
            return snapshot;
        }

        /// <summary>
        /// 亮度归一化平均绝对差：两图各自减去自身均值（消除整体明暗漂移）后，
        /// 逐像素平均绝对差。两图必须同尺寸。
        ///
        /// 实现：ConvertTo 到 16S（减均值防下溢）→ Absdiff → Mean。
        /// 为什么不用"Absdiff 均值减均值差"的近似补偿：结构变化（门开露出大面积
        /// 新内容）本身会抬高当前图均值，近似补偿会把有效结构差异也扣掉（实测
        /// 门开场景 25.6 被扣成 6.4 而漏检）；逐像素归一化则稳定给出 23+。
        /// </summary>
        private static float ComputeNormalizedMeanAbsDiff(Mat a, Mat b)
        {
            float meanA = (float)Cv2.Mean(a).Val0;
            float meanB = (float)Cv2.Mean(b).Val0;

            using (Mat a16 = new Mat())
            using (Mat b16 = new Mat())
            using (Mat d16 = new Mat())
            {
                a.ConvertTo(a16, MatType.CV_16SC1, 1, -meanA);
                b.ConvertTo(b16, MatType.CV_16SC1, 1, -meanB);
                Cv2.Absdiff(a16, b16, d16);
                return (float)Cv2.Mean(d16).Val0;
            }
        }

        /// <summary>门 ROI 是否被任一人体框遮挡（矩形相交测试）。</summary>
        private static bool IsRoiOccludedByPerson(Rect roi, List<DetectionResult> persons)
        {
            if (persons == null)
            {
                return false;
            }

            foreach (var p in persons)
            {
                float pl = p.X - p.Width / 2f;
                float pr = p.X + p.Width / 2f;
                float pt = p.Y - p.Height / 2f;
                float pb = p.Y + p.Height / 2f;

                bool intersect = pl < roi.Right && pr > roi.X &&
                                 pt < roi.Bottom && pb > roi.Y;
                if (intersect)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>归一化 ROI → 像素矩形（静态方法供可视化器复用，与 ESD 同一模式）。</summary>
        public static Rect ComputeRoiPixels(DoorMonitorOptions options, int frameWidth, int frameHeight)
        {
            int x = (int)(Clamp01(options.RoiX) * frameWidth);
            int y = (int)(Clamp01(options.RoiY) * frameHeight);
            int w = Math.Max(1, (int)(Clamp01(options.RoiW) * frameWidth));
            int h = Math.Max(1, (int)(Clamp01(options.RoiH) * frameHeight));
            // 右/下越界时往回收，保证矩形完整落在画面内
            x = Math.Min(x, Math.Max(0, frameWidth - w));
            y = Math.Min(y, Math.Max(0, frameHeight - h));
            return new Rect(x, y, w, h);
        }

        private Rect ComputeRoiPixels(int frameWidth, int frameHeight)
        {
            return ComputeRoiPixels(_options, frameWidth, frameHeight);
        }

        /// <summary>启动时尝试从磁盘加载基准图（不存在/损坏则保持"无基准"状态）。</summary>
        private void TryLoadBaselineFromFile()
        {
            try
            {
                string path = ResolveBaselinePath();
                if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path))
                {
                    return;
                }

                Mat loaded = Cv2.ImRead(path, ImreadModes.Grayscale);
                if (loaded != null && !loaded.Empty())
                {
                    _baseline = loaded;
                    LogManager.GeneralLog("[Door] 已加载关门基准图: " + path);
                }
                else
                {
                    loaded?.Dispose();
                    LogManager.GeneralLog("[Door] 基准图读取失败(空图), 请重新采集: " + path);
                }
            }
            catch (Exception ex)
            {
                LogManager.GeneralLog("[Door] 基准图加载异常, 请重新采集: " + ex.Message);
            }
        }

        private string ResolveBaselinePath()
        {
            if (string.IsNullOrEmpty(_options.BaselinePath))
            {
                return null;
            }

            // 相对路径锚定 exe 目录（与 esdConfig.json 等运行配置同一约定）
            if (System.IO.Path.IsPathRooted(_options.BaselinePath))
            {
                return _options.BaselinePath;
            }

            return System.IO.Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                _options.BaselinePath.Replace('/', System.IO.Path.DirectorySeparatorChar));
        }

        private static float Clamp01(float v)
        {
            return v < 0f ? 0f : (v > 1f ? 1f : v);
        }
    }

    /// <summary>
    /// 门状态监测的单帧快照（不可变值对象，外部可自由持有）。
    /// </summary>
    public sealed class DoorFrameSnapshot
    {
        /// <summary>当前门状态：true=开（相对关门基准差异超阈值）</summary>
        public bool IsOpen { get; set; }

        /// <summary>是否已有关门基准（false 时 IsOpen 无意义）</summary>
        public bool HasBaseline { get; set; }

        /// <summary>本帧差异值（亮度归一化平均绝对差）；被遮挡/无基准时为 0</summary>
        public float DiffValue { get; set; }

        /// <summary>本帧门区域是否被人体框遮挡（遮挡时跳过判定，保持上一状态）</summary>
        public bool PersonOccluded { get; set; }
    }
}
