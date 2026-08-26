using System;
using Newtonsoft.Json;
using YoloDetection;

namespace YoloDetector.Configuration
{
    /// <summary>
    /// 门状态检测配置模型（对应 Detection/doorConfig.json）。
    ///
    /// 职责与 EsdConfig 同一模式：仅做 JSON 反序列化载体 + 字段校验，
    /// 经 <see cref="ToOptions"/> 转换为检测模块参数注入。
    /// </summary>
    public class DoorConfig
    {
        [JsonProperty("_说明")]
        public string Description { get; set; } = "工位操作间门状态检测参数（关门基准比对）";

        /// <summary>是否启用门状态检测（关闭后管道零开销）</summary>
        [JsonProperty("Enabled")]
        public bool Enabled { get; set; } = true;

        // ---------- 门区域 ROI（归一化坐标，现场拖拽框选标定） ----------

        /// <summary>ROI 左上角 X（相对帧宽比例 0~1）</summary>
        [JsonProperty("RoiX")]
        public float RoiX { get; set; } = 0.85f;

        /// <summary>ROI 左上角 Y（相对帧高比例 0~1）</summary>
        [JsonProperty("RoiY")]
        public float RoiY { get; set; } = 0.10f;

        /// <summary>ROI 宽（相对帧宽比例 0~1）</summary>
        [JsonProperty("RoiW")]
        public float RoiW { get; set; } = 0.12f;

        /// <summary>ROI 高（相对帧高比例 0~1）</summary>
        [JsonProperty("RoiH")]
        public float RoiH { get; set; } = 0.55f;

        /// <summary>
        /// 差异阈值（灰度 0~255）：门区域与关门基准的亮度归一化差异超过该值判"门开"。
        /// 误报多（光照大变）调大；门开了不报调小。
        /// </summary>
        [JsonProperty("DiffThreshold")]
        public float DiffThreshold { get; set; } = 18f;

        /// <summary>状态翻转防抖（毫秒）：候选状态持续该时长才真正翻转（过滤人走过遮挡）</summary>
        [JsonProperty("StateHoldMs")]
        public double StateHoldMs { get; set; } = 1500;

        /// <summary>关门基准图路径（相对程序 exe 目录）</summary>
        [JsonProperty("BaselinePath")]
        public string BaselinePath { get; set; } = "Detection/door_baseline.png";

        /// <summary>是否在预览画面上绘制门区域状态叠加层</summary>
        [JsonProperty("DrawOverlay")]
        public bool DrawOverlay { get; set; } = true;

        /// <summary>
        /// 转换为检测模块参数。非法值（ROI 出界、非正阈值等）就地夹紧到安全范围。
        /// </summary>
        public DoorMonitorOptions ToOptions()
        {
            return new DoorMonitorOptions
            {
                RoiX = Clamp(RoiX, 0f, 1f),
                RoiY = Clamp(RoiY, 0f, 1f),
                RoiW = Clamp(RoiW, 0.01f, 1f),
                RoiH = Clamp(RoiH, 0.01f, 1f),
                DiffThreshold = Math.Max(1f, DiffThreshold),
                StateHoldMs = Math.Max(0.0, StateHoldMs),
                BaselinePath = BaselinePath,
                DrawOverlay = DrawOverlay
            };
        }

        /// <summary>运行期热更新 ROI（归一化坐标，就地夹紧）：UI 拖拽标定后同步内存单例用。</summary>
        public void ApplyNormalizedRoi(float roiX, float roiY, float roiW, float roiH)
        {
            RoiX = Clamp(roiX, 0f, 1f);
            RoiY = Clamp(roiY, 0f, 1f);
            RoiW = Clamp(roiW, 0.01f, 1f);
            RoiH = Clamp(roiH, 0.01f, 1f);
        }

        /// <summary>
        /// 在 doorConfig.json 原始文本中局部更新四个 ROI 字段（保留注释字段与字段顺序），
        /// 逻辑与 EsdConfig.UpdateRoiJson 完全一致（共用 RoiJsonUpdater 实现）。
        /// </summary>
        public static string UpdateRoiJson(string originalJson, float roiX, float roiY, float roiW, float roiH)
        {
            return RoiJsonUpdater.Update(originalJson, roiX, roiY, roiW, roiH);
        }

        private static float Clamp(float v, float min, float max)
        {
            return v < min ? min : (v > max ? max : v);
        }
    }
}
