using System;
using System.Collections.Generic;
using System.Linq;

namespace YoloDetection
{
    /// <summary>
    /// 静电杆（ESD）接触分析器：把"手腕关键点 + 静电杆区域"翻译成"这个人有没有在摸静电杆"。
    ///
    /// 判定模型（纯几何规则，不依赖额外模型，行为完全可解释可调参）：
    ///   1. 命中：左手腕或右手腕的关键点置信度 ≥ WristConfidenceThreshold，
    ///      且坐标落在静电杆 ROI（外扩 MarginPx 容差）内 → 该人本帧"命中"；
    ///   2. 指尖点触：沿"肘→腕"方向外推半个前臂长得到虚拟手部点，外推点落
    ///      ROI 内同样算命中（肘腕两点置信度都达标才外推，保证方向可信）——
    ///      覆盖"手臂伸直用指尖点杆"（腕点离接触点 20~40cm）的场景；
    ///   3. 兜底：双手合拢（左右腕距离&lt;40px）时模型对两腕置信度会同时崩塌
    ///      （实测 0.03~0.10），改用"双腕中点落 ROI + 任一肘点置信度达标"判定，
    ///      保证双手扶杆/捧杯与单手同样灵敏；
    ///   4. 认定：命中持续累计 ≥ HoldDurationMs → 进入 InContact 接触态；
    ///   5. 保持：接触态下手腕短暂丢失时，ReleaseGraceMs 内不清零不降级（防遮挡抖动）；
    ///   6. 结束：离开宽限期后退出接触态并清零累计；人消失超过 TrackForgetMs 后轨迹删除。
    ///
    /// 跨帧人员关联：按人体框中心点做贪心最近邻匹配（工厂场景人少且移动慢，足够可靠）；
    /// 每个被跟踪的人分配自增 TrackId，供日志与报警关联同一人。
    ///
    /// 线程契约：本类不是线程安全的。Update 只允许检测管道的工作线程串行调用；
    /// ContactChanged 事件在调用线程上同步触发，订阅方自行处理线程调度。
    /// </summary>
    public class EsdContactAnalyzer
    {
        /// <summary>
        /// 接触状态翻转事件：(trackId, 是否进入接触, 触发时刻的累计接触毫秒)。
        /// 进入(→true)与结束(→false)各触发一次，中间帧不重复触发。
        /// </summary>
        public event Action<int, bool, double> ContactChanged;

        /// <summary>单个人的跨帧跟踪状态（仅本类内部使用，字段包内直改）。</summary>
        private sealed class PersonTrack
        {
            public int TrackId;
            public float Cx, Cy, W, H;        // 最近一次匹配到的人体框（原图像素）
            public float Confidence;          // 最近一次人体框置信度
            public bool LeftWristInZone;      // 本帧左手腕是否落区
            public bool RightWristInZone;     // 本帧右手腕是否落区

            // 连续接触累计时长：只在命中帧增长；宽限期内冻结；宽限超时清零
            public double ContactElapsedMs;

            public long LastHitAtMs;          // 最近一次命中的时间戳（0 = 尚未命中过）
            public long LastMatchedAtMs;      // 最近一次被人体框匹配上的时间戳
            public long LastUpdatedMs;        // 上次参与状态机更新的时间戳（算 dt 用）
            public bool InContact;            // 当前是否处于接触态
            public bool Alive;                // 本帧是否有人体框匹配上（false = 暂时丢失）
            public bool HitThisFrame;         // 本帧手腕是否命中 ROI（状态机据此区分"刚命中/宽限等待"）

            /// <summary>
            /// 结束触摸时记录的累计时长（清零前的最终值）：
            /// ContactChanged(→false) 事件用它报告"这次触摸持续了多久"，
            /// 否则事件触发时累计已清零、日志只能打出 0 秒。
            /// </summary>
            public double FinalElapsedOnRelease;
        }

        private readonly EsdAnalysisOptions _options;
        private readonly List<PersonTrack> _tracks = new List<PersonTrack>();
        private int _nextTrackId = 1;

        /// <param name="options">
        /// 运行参数。传入后由本实例长期持有——宿主每次 Start 应构造新的 options 与分析器
        /// （运行期参数不变；要改配置就重建链路）。
        /// </param>
        public EsdContactAnalyzer(EsdAnalysisOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        /// <summary>当前生效的分析参数（只读暴露，便于可视化器取 ROI 绘制）</summary>
        public EsdAnalysisOptions Options { get { return _options; } }

        /// <summary>清空全部跟踪状态（停止预览/切换流源时调用，避免旧画面残留计时）。</summary>
        public void Reset()
        {
            _tracks.Clear();
        }

        /// <summary>
        /// 处理一帧：更新跟踪与接触状态机，返回本帧快照（新实例，外部可自由持有）。
        /// </summary>
        /// <param name="persons">人体框列表（原图像素坐标）</param>
        /// <param name="poses">姿态结果，必须与 persons 一一对应（IPoseDetector 契约）</param>
        /// <param name="frameWidth">帧宽（像素），用于把归一化 ROI 换算成像素矩形</param>
        /// <param name="frameHeight">帧高（像素）</param>
        /// <param name="nowMs">当前时间戳（毫秒单调时钟；以参数形式暴露是为了单元测试注入虚拟时钟）</param>
        public EsdFrameSnapshot Update(
            List<DetectionResult> persons,
            List<PoseResult> poses,
            int frameWidth,
            int frameHeight,
            long nowMs)
        {
            if (persons == null) persons = new List<DetectionResult>();
            if (poses == null || poses.Count == 0) poses = BuildEmptyPoses(persons);

            EsdRoiRect roi = ComputeRoiPixels(frameWidth, frameHeight);

            MarkAllTracksMissing();
            // 遗忘必须在匹配之前：否则长时间离场的人一回来就先被旧轨迹
            // 近邻匹配吸走，永远走不到遗忘分支，"重新计时"语义失效
            ForgetStaleTracks(nowMs);
            MatchTracksToPersons(persons, poses, roi, nowMs);
            UpdateStateMachine(nowMs);

            return BuildSnapshot(nowMs);
        }

        /// <summary>便利重载：时间戳自动取系统单调时钟（生产链路用这个）。</summary>
        public EsdFrameSnapshot Update(
            List<DetectionResult> persons,
            List<PoseResult> poses,
            int frameWidth,
            int frameHeight)
        {
            return Update(persons, poses, frameWidth, frameHeight, NowMs());
        }

        /// <summary>
        /// 归一化 ROI → 像素矩形（每帧换算一次；分辨率变化自动适应）。
        /// 静态方法：可视化器等外部也需要同一换算，避免为了一个纯函数去构造分析器。
        /// ROI 矩形类型为 <see cref="EsdRoiRect"/>（浮点，定义见独立文件）。
        /// </summary>
        public static EsdRoiRect ComputeRoiPixels(EsdAnalysisOptions options, int frameWidth, int frameHeight)
        {
            return new EsdRoiRect
            {
                X = Clamp01(options.RoiX) * frameWidth,
                Y = Clamp01(options.RoiY) * frameHeight,
                W = Clamp01(options.RoiW) * frameWidth,
                H = Clamp01(options.RoiH) * frameHeight
            };
        }

        private EsdRoiRect ComputeRoiPixels(int frameWidth, int frameHeight)
        {
            return ComputeRoiPixels(_options, frameWidth, frameHeight);
        }

        // ==================== 状态机核心步骤 ====================

        private void MarkAllTracksMissing()
        {
            foreach (var t in _tracks)
            {
                t.Alive = false;
                t.HitThisFrame = false; // 本帧未匹配的人不可能命中，防止残留上一帧标志
            }
        }

        /// <summary>
        /// 把本帧每个人体框匹配到既有轨迹（贪心最近邻：中心距离小于框尺寸的 0.7 倍即认同一人）；
        /// 匹配不上的新建轨迹。
        /// 说明：不做外观特征级跟踪——固定工位摄像头下人员移动连续，近邻匹配已够用；
        /// 将来需要更强关联再引入 IoU+外观代价矩阵，接口不变。
        /// </summary>
        private void MatchTracksToPersons(
            List<DetectionResult> persons, List<PoseResult> poses, EsdRoiRect roi, long nowMs)
        {
            for (int i = 0; i < persons.Count; i++)
            {
                DetectionResult person = persons[i];
                PoseResult pose = i < poses.Count ? poses[i] : null;

                PersonTrack best = null;
                float bestDist = float.MaxValue;

                foreach (var t in _tracks)
                {
                    if (t.Alive)
                    {
                        continue; // 一条轨迹一帧只关联一个人
                    }

                    float dx = t.Cx - person.X;
                    float dy = t.Cy - person.Y;
                    float dist = (float)Math.Sqrt(dx * dx + dy * dy);
                    // 关联门限随人体大小自适应：人越近（框越大）允许的帧间位移越大
                    float gate = Math.Max(person.Width, person.Height) * 0.7f;

                    if (dist < gate && dist < bestDist)
                    {
                        bestDist = dist;
                        best = t;
                    }
                }

                if (best == null)
                {
                    best = CreateTrack(nowMs);
                    _tracks.Add(best);
                    EnforceTrackLimit();
                }

                // 更新轨迹空间信息与本帧存活标记
                best.Cx = person.X;
                best.Cy = person.Y;
                best.W = person.Width;
                best.H = person.Height;
                best.Confidence = person.Confidence;
                best.Alive = true;
                best.LastMatchedAtMs = nowMs;

                // 手腕落区判定（瞬时值直接覆盖；无有效关键点则两腕均为 false）
                best.LeftWristInZone = IsWristInZone(pose, CocoKeyPointIndexes.LeftWrist, roi)
                    || IsHandTipInZone(pose, CocoKeyPointIndexes.LeftWrist, CocoKeyPointIndexes.LeftElbow, roi);
                best.RightWristInZone = IsWristInZone(pose, CocoKeyPointIndexes.RightWrist, roi)
                    || IsHandTipInZone(pose, CocoKeyPointIndexes.RightWrist, CocoKeyPointIndexes.RightElbow, roi);

                // 任一手腕命中即视为本帧命中：刷新命中时间戳 + 置本帧命中标志
                best.HitThisFrame = best.LeftWristInZone || best.RightWristInZone;

                // 双手合拢兜底（v2.9）：双手交叠扶杆/捧杯时，姿态模型对两个腕点的
                // 置信度会同时崩塌（实测 0.03~0.10，坐标却大致正确），上面的常规
                // 判定双腕全灭 → 双手摸反而不灵敏。交叠特征明确（两腕距离很近）
                // 时改用"中点落区 + 肘点验证"兜底，见方法注释。
                if (!best.HitThisFrame && IsOverlappingHandsInZone(pose, roi))
                {
                    best.LeftWristInZone = true;
                    best.RightWristInZone = true;
                    best.HitThisFrame = true;
                }

                if (best.HitThisFrame)
                {
                    best.LastHitAtMs = nowMs;
                }
            }
        }

        private PersonTrack CreateTrack(long nowMs)
        {
            return new PersonTrack
            {
                TrackId = _nextTrackId++,
                LastUpdatedMs = nowMs,
                LastMatchedAtMs = nowMs,
                LastHitAtMs = 0
            };
        }

        /// <summary>
        /// 腕部落区判定：关键点存在、置信度达标、坐标在 ROI 容差范围内三者同时满足。
        /// </summary>
        private bool IsWristInZone(PoseResult pose, int keyPointIndex, EsdRoiRect roi)
        {
            if (pose == null || !pose.HasKeypoints ||
                keyPointIndex >= pose.Keypoints.Count)
            {
                return false;
            }

            var kpt = pose.Keypoints[keyPointIndex];
            if (kpt.Confidence < _options.WristConfidenceThreshold)
            {
                return false;
            }

            return roi.Contains(kpt.X, kpt.Y, _options.MarginPx);
        }

        /// <summary>
        /// 指尖点触判定（v2.9）：沿"肘→腕"方向把腕点外推半个前臂长，得到近似
        /// 手掌/指尖位置，外推点落在 ROI 内即视为接触。
        ///
        /// 为什么需要：判定锚点是腕关节，而工人用**指尖**点触静电杆时，腕点离
        /// 接触点有 20~40cm（手臂伸直），远超 MarginPx 容差——"明明碰到了却不触发"。
        /// 外推点 = 腕 + (腕-肘) × 0.5：前臂长与"腕到中指尖"长度相近，外推半段
        /// 前臂恰好覆盖手掌到指尖区域。
        ///
        /// 可信度要求：肘、腕两点置信度都达标才外推——方向由两点决定，任一点
        /// 不可信则外推方向乱指。误报兜底：手臂"指向"ROI 但尚未接触也会命中，
        /// 需持续 HoldDurationMs 才认定——防静电场景"手臂伸向杆并保持"本身
        /// 就接近接触语义，宁可灵敏。
        /// </summary>
        private bool IsHandTipInZone(PoseResult pose, int wristIndex, int elbowIndex, EsdRoiRect roi)
        {
            const float ExtendRatio = 0.5f;

            if (pose == null || !pose.HasKeypoints ||
                pose.Keypoints.Count <= elbowIndex)
            {
                return false;
            }

            var wrist = pose.Keypoints[wristIndex];
            var elbow = pose.Keypoints[elbowIndex];
            if (wrist.Confidence < _options.WristConfidenceThreshold ||
                elbow.Confidence < _options.WristConfidenceThreshold)
            {
                return false;
            }

            float tipX = wrist.X + (wrist.X - elbow.X) * ExtendRatio;
            float tipY = wrist.Y + (wrist.Y - elbow.Y) * ExtendRatio;
            return roi.Contains(tipX, tipY, _options.MarginPx);
        }

        /// <summary>
        /// 双手合拢兜底判定（v2.9）：双手交叠扶杆/捧杯时，姿态模型对左右腕的
        /// 置信度会同时崩塌（实测 0.03~0.10，远低于任何可用阈值），但两个腕点的
        /// 坐标仍大致正确（模型知道手在哪，只是"不确定是哪只手"）。
        ///
        /// 判定条件（三者同时满足）：
        ///   1. 左右腕距离 &lt; 40px——"双手合拢"的强几何特征；
        ///   2. 双腕中点落在 ROI 容差内——两个独立给出的坐标互相印证位置；
        ///   3. 任一肘点置信度达标——肘未被遮挡、识别稳定（实测交叠时 0.55~0.77），
        ///      验证手臂确实抬起，防止垂手/走动时腕点偶然飘进 ROI 造成误报。
        ///
        /// 误报兜底：即使个别帧误判，接触认定还需持续 HoldDurationMs——坐标
        /// 乱飘的点难以连续 1 秒稳定落在小 ROI 内。
        /// </summary>
        private bool IsOverlappingHandsInZone(PoseResult pose, EsdRoiRect roi)
        {
            const float OverlapDistPx = 40f;

            if (pose == null || !pose.HasKeypoints ||
                pose.Keypoints.Count <= CocoKeyPointIndexes.RightElbow)
            {
                return false;
            }

            var lw = pose.Keypoints[CocoKeyPointIndexes.LeftWrist];
            var rw = pose.Keypoints[CocoKeyPointIndexes.RightWrist];
            float dx = lw.X - rw.X;
            float dy = lw.Y - rw.Y;
            if (dx * dx + dy * dy > OverlapDistPx * OverlapDistPx)
            {
                return false; // 双手没有合拢：走常规判定，不走兜底
            }

            float midX = (lw.X + rw.X) / 2f;
            float midY = (lw.Y + rw.Y) / 2f;
            if (!roi.Contains(midX, midY, _options.MarginPx))
            {
                return false;
            }

            var le = pose.Keypoints[CocoKeyPointIndexes.LeftElbow];
            var re = pose.Keypoints[CocoKeyPointIndexes.RightElbow];
            return le.Confidence >= _options.WristConfidenceThreshold ||
                   re.Confidence >= _options.WristConfidenceThreshold;
        }

        /// <summary>删除长时间无人匹配的轨迹（人已离开画面；回来会重新计时）。</summary>
        private void ForgetStaleTracks(long nowMs)
        {
            for (int i = _tracks.Count - 1; i >= 0; i--)
            {
                if (nowMs - _tracks[i].LastMatchedAtMs > _options.TrackForgetMs)
                {
                    _tracks.RemoveAt(i);
                }
            }
        }

        /// <summary>轨迹表容量保护：超出上限时淘汰最久未匹配的轨迹，防拥挤场景内存无界增长。</summary>
        private void EnforceTrackLimit()
        {
            while (_tracks.Count > _options.MaxTrackedPersons)
            {
                long oldest = long.MaxValue;
                int oldestIdx = 0;
                for (int i = 0; i < _tracks.Count; i++)
                {
                    if (_tracks[i].LastMatchedAtMs < oldest)
                    {
                        oldest = _tracks[i].LastMatchedAtMs;
                        oldestIdx = i;
                    }
                }
                _tracks.RemoveAt(oldestIdx);
            }
        }

        /// <summary>
        /// 按帧推进每个人的接触状态机：
        ///   命中帧 → 累计时长按 dt 增长；
        ///   未命中但在宽限期内 → 冻结累计（不清零，防遮挡/抖动打断）；
        ///   未命中且超过宽限期 → 清零退出接触态。
        /// InContact 翻转时触发 ContactChanged（进入/结束各一次）。
        /// </summary>
        private void UpdateStateMachine(long nowMs)
        {
            foreach (var t in _tracks)
            {
                double dt = nowMs - t.LastUpdatedMs;
                t.LastUpdatedMs = nowMs;

                // dt 为负或异常大时按 0 处理，避免一次性灌入巨量时长造成误判。
                // 上限取 5 秒：必须容纳"ESD 降频(ProcessEveryNFrames) × 低帧率RTSP"
                // 叠加后的合法帧间隔（如 3帧×1fps=3s），不能按单帧间隔估计
                if (dt < 0 || dt > 5000)
                {
                    dt = 0;
                }

                if (t.HitThisFrame)
                {
                    // 本帧命中：累计接触时长增长
                    t.ContactElapsedMs += dt;
                }
                else if (!(t.LastHitAtMs > 0 && nowMs - t.LastHitAtMs <= _options.ReleaseGraceMs))
                {
                    // 未命中且已超出宽限期：彻底结束。
                    // 先记录清零前的最终时长（结束事件要用），再清零
                    t.FinalElapsedOnRelease = t.ContactElapsedMs;
                    t.ContactElapsedMs = 0;
                }
                // 其余情况（未命中但仍在宽限期内）：冻结累计时长，等待手腕再次出现

                bool wasInContact = t.InContact;
                t.InContact = t.ContactElapsedMs >= _options.HoldDurationMs;

                if (t.InContact != wasInContact)
                {
                    // 进入事件带当前累计；结束事件带清零前最终值
                    double reported = t.InContact ? t.ContactElapsedMs : t.FinalElapsedOnRelease;
                    RaiseContactChanged(t.TrackId, t.InContact, reported);
                }
            }
        }

        private void RaiseContactChanged(int trackId, bool inContact, double elapsedMs)
        {
            var handler = ContactChanged;
            if (handler != null)
            {
                handler(trackId, inContact, elapsedMs);
            }
        }

        /// <summary>构建本帧不可变快照（含暂时丢失但仍在跟踪中的人，UI 才能持续显示其状态）。</summary>
        private EsdFrameSnapshot BuildSnapshot(long nowMs)
        {
            var snapshot = new EsdFrameSnapshot { TimestampMs = nowMs };

            foreach (var t in _tracks)
            {
                snapshot.Persons.Add(new EsdPersonStatus
                {
                    TrackId = t.TrackId,
                    X = t.Cx,
                    Y = t.Cy,
                    Width = t.W,
                    Height = t.H,
                    LeftWristInZone = t.LeftWristInZone,
                    RightWristInZone = t.RightWristInZone,
                    InContact = t.InContact,
                    ContactElapsedMs = t.ContactElapsedMs,
                    Confidence = t.Confidence
                });

                if (t.InContact)
                {
                    snapshot.ContactCount++;
                }
            }

            return snapshot;
        }

        // ==================== 工具 ====================

        private static float Clamp01(float v)
        {
            return v < 0f ? 0f : (v > 1f ? 1f : v);
        }

        /// <summary>
        /// 单调时钟毫秒（Environment.TickCount 是 int 且会回绕，.NET Framework 4.7.2
        /// 又没有 TickCount64，故统一用 Stopwatch——跨天连续运行也不出错）。
        /// </summary>
        public static long NowMs()
        {
            return System.Diagnostics.Stopwatch.GetTimestamp() * 1000 / System.Diagnostics.Stopwatch.Frequency;
        }

        private static List<PoseResult> BuildEmptyPoses(List<DetectionResult> persons)
        {
            var poses = new List<PoseResult>();
            if (persons != null)
            {
                foreach (var p in persons)
                {
                    poses.Add(new PoseResult { Person = p });
                }
            }
            return poses;
        }
    }
}
