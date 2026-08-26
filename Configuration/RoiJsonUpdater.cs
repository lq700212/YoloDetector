using Newtonsoft.Json.Linq;

namespace YoloDetector.Configuration
{
    /// <summary>
    /// ROI 配置 JSON 局部更新工具（EsdConfig/DoorConfig 共用）。
    ///
    /// 为什么用 JObject 局部更新而不是"反序列化→整体序列化回写"：配置文件里有
    /// 大量以下划线开头的说明性字段（"_说明"、"_现场标定"等，不在模型属性中），
    /// 整体序列化会把现场依赖的调参指南注释全部抹掉。局部更新既保注释又保字段顺序。
    /// </summary>
    public static class RoiJsonUpdater
    {
        /// <summary>
        /// 在原始 JSON 文本中局部更新 RoiX/RoiY/RoiW/RoiH 四个字段，其余内容
        /// 原样保留。四个值内部夹紧到 [0,1]/[0.01,1]，与内存热更新双保险。
        ///
        /// 返回更新后的 JSON 文本（缩进格式化）；输入为空或不是合法 JSON 时返回 null，
        /// 由调用方决定回退策略。
        /// </summary>
        public static string Update(string originalJson, float roiX, float roiY, float roiW, float roiH)
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
            catch (Newtonsoft.Json.JsonReaderException)
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
