using System;
using Newtonsoft.Json;
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
                DrawOverlay = DrawOverlay
            };
        }

        private static float Clamp(float v, float min, float max)
        {
            return v < min ? min : (v > max ? max : v);
        }
    }
}
