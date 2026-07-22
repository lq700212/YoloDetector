/*
 * 文件名: DetectionResultProcessor.cs
 * 作者: Auto Generated
 * 日期: 2026-07-16
 * 版本: 1.0
 * 
 * 功能说明:
 *     这个文件定义了检测结果后处理器接口和实现类，用于对YOLO检测结果进行二次处理。
 *     
 *     设计模式：策略模式 + 组合模式
 *     - 策略模式：不同的处理策略（默认处理、尺寸过滤）可以互相替换
 *     - 组合模式：CompositeResultProcessor可以组合多个处理器
 *     
 *     后处理流程：
 *     YOLO检测 → 原始结果 → 后处理器1 → 中间结果 → 后处理器2 → 最终结果
 *     
 *     现有实现:
 *     1. DefaultResultProcessor: 默认处理器，裁剪检测框到画面边界
 *     2. SizeFilterProcessor: 尺寸过滤器，过滤太小或太大的检测框
 *     3. CompositeResultProcessor: 组合处理器，按顺序执行多个处理器
 *     
 *     使用场景:
 *     - 裁剪超出画面边界的检测框
 *     - 过滤噪声检测（太小的框）
 *     - 过滤不合理的检测（太大的框）
 *     - 实现自定义的过滤逻辑
 */

using System;
using System.Collections.Generic;
using OpenCvSharp;

namespace YoloDetector.YoloDetection
{
    /// <summary>
    /// 检测结果后处理器接口
    /// 
    /// 这个接口定义了对检测结果进行后处理的规范。
    /// 后处理器可以对原始检测结果进行过滤、裁剪、转换等操作。
    /// 
    /// 设计思想：
    /// 将检测结果的后处理逻辑从检测器中分离出来，实现关注点分离。
    /// 检测器只负责执行推理，后处理器负责处理推理结果。
    /// 
    /// 实现示例:
    /// public class CustomProcessor : IDetectionResultProcessor
    /// {
    ///     public string ProcessorName => "Custom";
    ///     
    ///     public List<DetectionResult> Process(List<DetectionResult> rawResults, int imageWidth, int imageHeight)
    ///     {
    ///         // 自定义处理逻辑
    ///         return filteredResults;
    ///     }
    /// }
    /// </summary>
    public interface IDetectionResultProcessor
    {
        /// <summary>
        /// 处理检测结果
        /// 
        /// 对原始检测结果进行处理，返回处理后的结果。
        /// 
        /// 参数:
        /// rawResults - YOLO检测器返回的原始检测结果列表
        /// imageWidth - 原始图像宽度，用于边界裁剪
        /// imageHeight - 原始图像高度，用于边界裁剪
        /// 
        /// 返回:
        /// 处理后的检测结果列表
        /// </summary>
        /// <param name="rawResults">原始检测结果列表</param>
        /// <param name="imageWidth">原始图像宽度</param>
        /// <param name="imageHeight">原始图像高度</param>
        /// <returns>处理后的检测结果列表</returns>
        List<DetectionResult> Process(List<DetectionResult> rawResults, int imageWidth, int imageHeight);

        /// <summary>
        /// 处理器名称
        /// 
        /// 用于标识不同的处理器，方便日志输出和调试。
        /// </summary>
        string ProcessorName { get; }
    }

    /// <summary>
    /// 默认检测结果处理器
    /// 
    /// 这个处理器负责将检测框裁剪到图像边界内。
    /// 当目标靠近画面边缘时，检测框可能部分超出边界，
    /// 这个处理器会将超出的部分裁剪掉，只保留可见部分。
    /// 
    /// 处理逻辑:
    /// 1. 对每个检测框，计算裁剪后的左上角和右下角坐标
    ///    - left = max(0, 原始left)
    ///    - top = max(0, 原始top)
    ///    - right = min(imageWidth, 原始right)
    ///    - bottom = min(imageHeight, 原始bottom)
    /// 2. 计算裁剪后的宽度和高度
    /// 3. 如果裁剪后宽高都大于0，保留检测框；否则丢弃
    /// 4. 设置最小尺寸过滤（10x20像素），过滤噪声
    /// 
    /// 使用示例:
    /// var processor = new DefaultResultProcessor();
    /// var results = processor.Process(rawResults, 1920, 1080);
    /// </summary>
    public class DefaultResultProcessor : IDetectionResultProcessor
    {
        /// <summary>
        /// 处理器名称，固定为 "Default"
        /// </summary>
        public string ProcessorName => "Default";

        /// <summary>
        /// 处理检测结果（核心方法）
        /// 
        /// 详细处理步骤:
        /// 1. 检查输入是否为空，如果为空返回空列表
        /// 2. 遍历每个检测结果
        /// 3. 计算裁剪后的边界坐标
        /// 4. 判断裁剪后的检测框是否有有效区域
        /// 5. 判断尺寸是否满足最小要求
        /// 6. 创建新的DetectionResult，使用裁剪后的坐标
        /// 7. 添加到结果列表
        /// 
        /// 参数:
        /// rawResults - YOLO检测器返回的原始检测结果
        /// imageWidth - 图像宽度，用于边界判断
        /// imageHeight - 图像高度，用于边界判断
        /// 
        /// 返回:
        /// 裁剪后的检测结果列表
        /// </summary>
        /// <param name="rawResults">原始检测结果列表</param>
        /// <param name="imageWidth">图像宽度</param>
        /// <param name="imageHeight">图像高度</param>
        /// <returns>处理后的检测结果列表</returns>
        public List<DetectionResult> Process(List<DetectionResult> rawResults, int imageWidth, int imageHeight)
        {
            // 1. 如果输入为空，直接返回空列表
            if (rawResults == null || rawResults.Count == 0)
                return new List<DetectionResult>();

            // 2. 创建结果列表
            var processed = new List<DetectionResult>();

            // 3. 遍历每个检测结果
            foreach (var result in rawResults)
            {
                // 4. 计算裁剪后的边界坐标
                // left: 左上角X，不能小于0
                float left = Math.Max(0, result.Left);
                // top: 左上角Y，不能小于0
                float top = Math.Max(0, result.Top);
                // right: 右下角X，不能大于图像宽度
                float right = Math.Min(imageWidth, result.Right);
                // bottom: 右下角Y，不能大于图像高度
                float bottom = Math.Min(imageHeight, result.Bottom);

                // 5. 计算裁剪后的宽度和高度
                float clippedWidth = right - left;
                float clippedHeight = bottom - top;

                // 6. 判断裁剪后的检测框是否有有效区域
                // 如果宽度或高度小于等于0，说明检测框完全超出画面，丢弃
                if (clippedWidth <= 0 || clippedHeight <= 0)
                    continue;

                // 7. 最小尺寸过滤：过滤噪声
                // 检测框太小可能是误检，设置最小尺寸为10x20像素
                if (clippedWidth < 10 || clippedHeight < 20)
                    continue;

                // 8. 创建新的DetectionResult，使用裁剪后的坐标
                // 注意：重新计算中心坐标，使用裁剪后的宽度和高度
                processed.Add(new DetectionResult
                {
                    ClassId = result.ClassId,
                    ClassName = result.ClassName,
                    Confidence = result.Confidence,
                    // 新的中心X = 裁剪后left + 裁剪后宽度/2
                    X = left + clippedWidth / 2,
                    // 新的中心Y = 裁剪后top + 裁剪后高度/2
                    Y = top + clippedHeight / 2,
                    Width = clippedWidth,
                    Height = clippedHeight
                });
            }

            // 9. 返回处理后的结果列表
            return processed;
        }
    }

    /// <summary>
    /// 尺寸过滤处理器
    /// 
    /// 这个处理器根据尺寸过滤检测框，可以设置：
    /// - 最小宽度和高度（过滤太小的检测框）
    /// - 最大宽度比例和高度比例（过滤太大的检测框）
    /// 
    /// 使用场景:
    /// - 过滤噪声检测（如1x1像素的误检）
    /// - 过滤不合理的大检测框（如占画面95%以上的框）
    /// - 根据场景需求调整检测框尺寸范围
    /// 
    /// 使用示例:
    /// var processor = new SizeFilterProcessor
    /// {
    ///     MinWidth = 20,
    ///     MinHeight = 40,
    ///     MaxWidthRatio = 0.9f,
    ///     MaxHeightRatio = 0.9f
    /// };
    /// var results = processor.Process(rawResults, 1920, 1080);
    /// </summary>
    public class SizeFilterProcessor : IDetectionResultProcessor
    {
        /// <summary>
        /// 处理器名称，固定为 "SizeFilter"
        /// </summary>
        public string ProcessorName => "SizeFilter";

        /// <summary>
        /// 最小宽度（像素）
        /// 
        /// 检测框宽度小于这个值会被过滤掉。
        /// 默认值: 10像素
        /// </summary>
        public float MinWidth { get; set; } = 10;

        /// <summary>
        /// 最小高度（像素）
        /// 
        /// 检测框高度小于这个值会被过滤掉。
        /// 默认值: 20像素
        /// </summary>
        public float MinHeight { get; set; } = 20;

        /// <summary>
        /// 最大宽度比例（相对于图像宽度）
        /// 
        /// 检测框宽度与图像宽度的比值超过这个值会被过滤掉。
        /// 默认值: float.MaxValue（不限制）
        /// 
        /// 示例:
        /// MaxWidthRatio = 0.9f 表示检测框宽度不能超过图像宽度的90%
        /// </summary>
        public float MaxWidthRatio { get; set; } = float.MaxValue;

        /// <summary>
        /// 最大高度比例（相对于图像高度）
        /// 
        /// 检测框高度与图像高度的比值超过这个值会被过滤掉。
        /// 默认值: float.MaxValue（不限制）
        /// 
        /// 示例:
        /// MaxHeightRatio = 0.9f 表示检测框高度不能超过图像高度的90%
        /// </summary>
        public float MaxHeightRatio { get; set; } = float.MaxValue;

        /// <summary>
        /// 处理检测结果（尺寸过滤）
        /// 
        /// 过滤逻辑:
        /// 1. 检查宽度是否小于MinWidth
        /// 2. 检查高度是否小于MinHeight
        /// 3. 检查宽度是否超过图像宽度的MaxWidthRatio倍
        /// 4. 检查高度是否超过图像高度的MaxHeightRatio倍
        /// 5. 通过所有检查的检测框保留，其余丢弃
        /// 
        /// 参数:
        /// rawResults - 原始检测结果列表
        /// imageWidth - 图像宽度
        /// imageHeight - 图像高度
        /// 
        /// 返回:
        /// 过滤后的检测结果列表
        /// </summary>
        /// <param name="rawResults">原始检测结果列表</param>
        /// <param name="imageWidth">图像宽度</param>
        /// <param name="imageHeight">图像高度</param>
        /// <returns>过滤后的检测结果列表</returns>
        public List<DetectionResult> Process(List<DetectionResult> rawResults, int imageWidth, int imageHeight)
        {
            // 1. 如果输入为空，直接返回空列表
            if (rawResults == null || rawResults.Count == 0)
                return new List<DetectionResult>();

            // 2. 创建结果列表
            var processed = new List<DetectionResult>();

            // 3. 遍历每个检测结果
            foreach (var result in rawResults)
            {
                // 4. 最小宽度检查
                if (result.Width < MinWidth)
                    continue;

                // 5. 最小高度检查
                if (result.Height < MinHeight)
                    continue;

                // 6. 最大宽度比例检查
                if (result.Width > imageWidth * MaxWidthRatio)
                    continue;

                // 7. 最大高度比例检查
                if (result.Height > imageHeight * MaxHeightRatio)
                    continue;

                // 8. 通过所有检查，保留检测框
                processed.Add(result);
            }

            // 9. 返回过滤后的结果列表
            return processed;
        }
    }

    /// <summary>
    /// 组合检测结果处理器
    /// 
    /// 这个处理器可以组合多个处理器，按顺序执行它们。
    /// 使用组合模式，将多个处理器串联起来，形成处理链。
    /// 
    /// 处理流程:
    /// 原始结果 → 处理器1 → 中间结果1 → 处理器2 → 中间结果2 → ... → 最终结果
    /// 
    /// 使用示例:
    /// var composite = new CompositeResultProcessor();
    /// composite.AddProcessor(new DefaultResultProcessor());
    /// composite.AddProcessor(new SizeFilterProcessor());
    /// var results = composite.Process(rawResults, 1920, 1080);
    /// </summary>
    public class CompositeResultProcessor : IDetectionResultProcessor
    {
        /// <summary>
        /// 处理器名称，固定为 "Composite"
        /// </summary>
        public string ProcessorName => "Composite";

        /// <summary>
        /// 处理器列表
        /// 
        /// 存储所有添加的处理器，按添加顺序执行。
        /// </summary>
        private readonly List<IDetectionResultProcessor> _processors = new List<IDetectionResultProcessor>();

        /// <summary>
        /// 添加处理器
        /// 
        /// 将一个处理器添加到处理链的末尾，会在其他处理器之后执行。
        /// 
        /// 参数:
        /// processor - 要添加的处理器实例
        /// 
        /// 示例:
        /// composite.AddProcessor(new DefaultResultProcessor());
        /// </summary>
        /// <param name="processor">要添加的处理器实例</param>
        public void AddProcessor(IDetectionResultProcessor processor)
        {
            _processors.Add(processor);
        }

        /// <summary>
        /// 移除处理器
        /// 
        /// 从处理链中移除指定的处理器。
        /// 如果处理器不在列表中，什么都不做。
        /// 
        /// 参数:
        /// processor - 要移除的处理器实例
        /// 
        /// 示例:
        /// composite.RemoveProcessor(sizeFilter);
        /// </summary>
        /// <param name="processor">要移除的处理器实例</param>
        public void RemoveProcessor(IDetectionResultProcessor processor)
        {
            _processors.Remove(processor);
        }

        /// <summary>
        /// 处理检测结果（组合处理）
        /// 
        /// 按顺序执行所有添加的处理器，每个处理器的输出作为下一个处理器的输入。
        /// 
        /// 处理流程:
        /// 1. 将原始结果传递给第一个处理器
        /// 2. 将第一个处理器的输出传递给第二个处理器
        /// 3. 重复这个过程，直到所有处理器都执行完毕
        /// 4. 返回最后一个处理器的输出作为最终结果
        /// 
        /// 参数:
        /// rawResults - 原始检测结果列表
        /// imageWidth - 图像宽度
        /// imageHeight - 图像高度
        /// 
        /// 返回:
        /// 经过所有处理器处理后的最终结果列表
        /// </summary>
        /// <param name="rawResults">原始检测结果列表</param>
        /// <param name="imageWidth">图像宽度</param>
        /// <param name="imageHeight">图像高度</param>
        /// <returns>处理后的检测结果列表</returns>
        public List<DetectionResult> Process(List<DetectionResult> rawResults, int imageWidth, int imageHeight)
        {
            // 1. 将原始结果作为初始输入
            var results = rawResults ?? new List<DetectionResult>();

            // 2. 按顺序执行每个处理器
            foreach (var processor in _processors)
            {
                // 3. 将当前结果传递给处理器，获取处理后的结果
                results = processor.Process(results, imageWidth, imageHeight);
            }

            // 4. 返回最终结果
            return results;
        }
    }
}