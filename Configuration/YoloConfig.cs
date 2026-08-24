using Newtonsoft.Json;

namespace YoloDetector.Configuration
{
    /// <summary>
    /// YOLO 检测配置模型（对应 Detection/yoloConfig.json），独立于相机品牌配置。
    /// </summary>
    public class YoloConfig
    {
        [JsonProperty("_说明")]
        public string Description { get; set; } = "YOLO目标检测参数";

        /// <summary>YOLO 模型文件路径（相对程序 exe 目录）</summary>
        [JsonProperty("ModelPath")]
        public string ModelPath { get; set; } = "yolo26n.onnx";

        /// <summary>置信度阈值（0-1）：越低越灵敏但误检多，越高越精准但可能漏检</summary>
        [JsonProperty("ConfidenceThreshold")]
        public float ConfidenceThreshold { get; set; } = 0.5f;

        /// <summary>NMS（非极大值抑制）阈值：用于去除重叠检测框</summary>
        [JsonProperty("NmsThreshold")]
        public float NmsThreshold { get; set; } = 0.45f;

        /// <summary>是否启用 YOLO 检测</summary>
        [JsonProperty("Enabled")]
        public bool Enabled { get; set; } = false;

        /// <summary>YOLO 调试日志开关（默认关闭，避免刷屏）</summary>
        [JsonProperty("YoloDebugLog")]
        public bool YoloDebugLog { get; set; } = false;

        /// <summary>每帧检测结果日志开关（会产生大量日志，建议仅调试时开启）</summary>
        [JsonProperty("DetectionResultLog")]
        public bool DetectionResultLog { get; set; } = false;

        /// <summary>可视化方案类型：YoloBuiltin（红框/GDI+）或 OpenCV（绿框/OpenCV）</summary>
        [JsonProperty("VisualizerType")]
        public string VisualizerType { get; set; } = "YoloBuiltin";
    }
}
