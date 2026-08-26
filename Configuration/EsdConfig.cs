using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using YoloDetection;

namespace YoloDetector.Configuration
{
    /// <summary>
    /// 静电接触(ESD)检测配置模型（对应 Detection/esdConfig.json）。
    ///
    /// 职责：仅做 JSON 反序列化载体 + 字段校验，经 <see cref="ToOptions"/> 转换为
    /// 检测模块参数（YoloDetection.EsdAnalysisOptions）注入——检测模块本身不碰本类，
    /// 维持"宿主管配置文件、模块收纯参数"的分层约定。
    /// </summary>
    public class EsdConfig
    {
        [JsonProperty("_说明")]
        public string Description { get; set; } = "静电杆触摸检测参数（人体姿态关键点+区域规则）";

        /// <summary>是否启用静电接触检测（关闭后管道零开销，行为与纯人员检测一致）</summary>
        [JsonProperty("Enabled")]
        public bool Enabled { get; set; } = true;

        /// <summary>姿态模型路径（相对程序 exe 目录），如 yolo11n-pose.onnx</summary>
        [JsonProperty("PoseModelPath")]
        public string PoseModelPath { get; set; } = "Detection/model/yolo11n-pose.onnx";

        /// <summary>手腕关键点置信度阈值（0-1）：低于该值的手腕坐标不参与判定</summary>
        [JsonProperty("WristConfidenceThreshold")]
        public float WristConfidenceThreshold { get; set; } = 0.35f;

        // ---------- 静电杆 ROI（归一化坐标，现场按摄像头画面标定） ----------

        /// <summary>ROI 左上角 X（相对帧宽比例 0~1）。改完启动预览看黄色框位置微调。</summary>
        [JsonProperty("RoiX")]
        public float RoiX { get; set; } = 0.40f;

        /// <summary>ROI 左上角 Y（相对帧高比例 0~1）</summary>
        [JsonProperty("RoiY")]
        public float RoiY { get; set; } = 0.25f;

        /// <summary>ROI 宽（相对帧宽比例 0~1）</summary>
        [JsonProperty("RoiW")]
        public float RoiW { get; set; } = 0.20f;

        /// <summary>ROI 高（相对帧高比例 0~1）</summary>
        [JsonProperty("RoiH")]
        public float RoiH { get; set; } = 0.35f;

        /// <summary>判定容差（像素）：手腕点到 ROI 外扩该距离以内也算命中</summary>
        [JsonProperty("MarginPx")]
        public float MarginPx { get; set; } = 20f;

        // ---------- 触摸判定时序 ----------

        /// <summary>持续命中时长阈值（毫秒）：达到才算"正在触摸"，过滤路人扫过</summary>
        [JsonProperty("HoldDurationMs")]
        public double HoldDurationMs { get; set; } = 1500;

        /// <summary>释放宽限（毫秒）：短暂遮挡/抖动不断开已建立的接触状态</summary>
        [JsonProperty("ReleaseGraceMs")]
        public double ReleaseGraceMs { get; set; } = 2000;

        // ---------- 性能 ----------

        /// <summary>每 N 帧做一次姿态推理（1=每帧；CPU 环境建议 2~3）</summary>
        [JsonProperty("ProcessEveryNFrames")]
        public int ProcessEveryNFrames { get; set; } = 1;

        /// <summary>是否在预览画面上绘制 ROI 与接触状态叠加层</summary>
        [JsonProperty("DrawOverlay")]
        public bool DrawOverlay { get; set; } = true;

        /// <summary>
        /// 是否为未触摸静电杆的人员画灰色跟踪框（#N NO GND）+ 手腕落点徽标。
        /// 默认 false：画面平时只有 YOLO 红框与黄色 ROI 框，有人真正触摸时才出现
        /// 绿色 ESD OK 粗框；现场要目检轨迹跟踪效果或核对手腕落区时临时改 true。
        /// </summary>
        [JsonProperty("DrawNoContactBoxes")]
        public bool DrawNoContactBoxes { get; set; } = false;

        /// <summary>
        /// 转换为检测模块参数。非法值（ROI 出界、负时长等）就地夹紧到安全范围，
        /// 现场手改 JSON 改坏了也不至于让检测逻辑跑飞。
        /// </summary>
        public EsdAnalysisOptions ToOptions()
        {
            return new EsdAnalysisOptions
            {
                RoiX = Clamp(RoiX, 0f, 1f),
                RoiY = Clamp(RoiY, 0f, 1f),
                RoiW = Clamp(RoiW, 0.01f, 1f),
                RoiH = Clamp(RoiH, 0.01f, 1f),
                MarginPx = Math.Max(0f, MarginPx),
                HoldDurationMs = Math.Max(0.0, HoldDurationMs),
                ReleaseGraceMs = Math.Max(0.0, ReleaseGraceMs),
                WristConfidenceThreshold = Clamp(WristConfidenceThreshold, 0.05f, 0.95f),
                DrawOverlay = DrawOverlay,
                DrawNoContactBoxes = DrawNoContactBoxes
            };
        }

        /// <summary>
        /// 运行期热更新 ROI（归一化坐标，就地夹紧）：UI 拖拽标定后同步内存单例用，
        /// 语义与 <see cref="ToOptions"/> 的夹紧规则完全一致。
        /// </summary>
        public void ApplyNormalizedRoi(float roiX, float roiY, float roiW, float roiH)
        {
            RoiX = Clamp(roiX, 0f, 1f);
            RoiY = Clamp(roiY, 0f, 1f);
            RoiW = Clamp(roiW, 0.01f, 1f);
            RoiH = Clamp(roiH, 0.01f, 1f);
        }

        /// <summary>
        /// 在 esdConfig.json 的原始文本中局部更新四个 ROI 字段，其余内容
        /// （"_说明"、"_现场标定" 等中文注释字段与全部参数）原样保留。
        ///
        /// 为什么不用"反序列化→整体序列化回写"：JSON 里有大量以下划线开头的
        /// 说明性字段（不在本模型属性中），整体序列化会把现场依赖的调参指南注释
        /// 全部抹掉。JObject 局部更新既保注释又保字段顺序。
        ///
        /// 返回更新后的 JSON 文本（缩进格式化）；输入为空或不是合法 JSON 时返回 null，
        /// 由调用方决定回退策略。四个值内部夹紧，与 ApplyNormalizedRoi 双保险。
        /// </summary>
        public static string UpdateRoiJson(string originalJson, float roiX, float roiY, float roiW, float roiH)
        {
            if (string.IsNullOrWhiteSpace(originalJson))
            {
                return null;
            }

            JObject root;
            try
            {
                root = JObject.Parse(originalJson);
            }
            catch (JsonReaderException)
            {
                return null; // 文件被手改坏：交由调用方走整对象重建路径
            }

            // JObject 保持插入顺序：已存在的键原地改值（顺序不变），
            // 缺失的键追加到末尾（手工删过字段的文件也能自动补全）
            root["RoiX"] = new JValue(Clamp(roiX, 0f, 1f));
            root["RoiY"] = new JValue(Clamp(roiY, 0f, 1f));
            root["RoiW"] = new JValue(Clamp(roiW, 0.01f, 1f));
            root["RoiH"] = new JValue(Clamp(roiH, 0.01f, 1f));

            return root.ToString(Newtonsoft.Json.Formatting.Indented);
        }

        private static float Clamp(float v, float min, float max)
        {
            return v < min ? min : (v > max ? max : v);
        }
    }
}
