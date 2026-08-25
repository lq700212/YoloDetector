using System.Collections.Generic;
using YoloDetection;

namespace YoloDetector.Tests
{
    // ============================================================
    // 检测结果后处理器测试。
    //
    // DefaultResultProcessor 的边界裁剪/最小尺寸阈值(10x20)是现场调好的
    // 行为红线（见 AGENTS.md 铁律6）——本分区把它们的行为用例固定下来，
    // 任何人改动这些数值导致行为变化时，测试立即报警。
    // ============================================================

    internal static class ProcessorTests
    {
        public static void RunAll()
        {
            T.Case("后处理-null与空输入返回空列表", NullAndEmpty);
            T.Case("后处理-完全出界框丢弃", DropsFullyOutOfBounds);
            T.Case("后处理-部分出界框裁剪并重算中心", ClipsPartialOut);
            T.Case("后处理-最小尺寸过滤10x20", MinSizeFilter);
            T.Case("后处理-正常框保留数值不变", KeepsNormalBox);
            T.Case("后处理-SizeFilter相对上限过滤", SizeFilterRatio);
            T.Case("后处理-Composite串联顺序生效", CompositeChained);
            T.Case("结果模型-边界属性计算", ResultModelEdges);
        }

        /// <summary>DetectionResult 的 Left/Top/Right/Bottom 是中心点+宽高的派生属性（显示与 NMS 都依赖）</summary>
        private static void ResultModelEdges()
        {
            var r = new DetectionResult { X = 100, Y = 200, Width = 40, Height = 60 };
            T.Eq(80f, r.Left, "Left=X-W/2");
            T.Eq(170f, r.Top, "Top=Y-H/2");
            T.Eq(120f, r.Right, "Right=X+W/2");
            T.Eq(230f, r.Bottom, "Bottom=Y+H/2");
            T.Eq(0.9f, new DetectionResult { Confidence = 0.9f }.Confidence, "Confidence 可读写");
        }

        private static void NullAndEmpty()
        {
            var p = new DefaultResultProcessor();
            T.Eq(0, p.Process(null, 1920, 1080).Count, "null 输入应返回空列表");
            T.Eq(0, p.Process(new List<DetectionResult>(), 1920, 1080).Count, "空输入应返回空列表");
        }

        /// <summary>框整体在画面外必须丢弃</summary>
        private static void DropsFullyOutOfBounds()
        {
            var p = new DefaultResultProcessor();
            var input = new List<DetectionResult>
            {
                FakeDetector.Box(-500, 100, 100, 200),   // 整体在左侧外
                FakeDetector.Box(3000, 100, 100, 200),   // 整体在右侧外（图宽1920）
                FakeDetector.Box(960, -800, 200, 300),   // 整体在上方外
                FakeDetector.Box(960, 2000, 200, 300)    // 整体在下方外（图高1080）
            };
            T.Eq(0, p.Process(input, 1920, 1080).Count, "四个完全出界框都应被丢弃");
        }

        /// <summary>
        /// 部分出界的框裁到边界，且中心点/宽高按裁剪后重算——这是坐标映射的
        /// 可观察行为，公式变化会直接改变检测框显示位置，必须锁定。
        /// </summary>
        private static void ClipsPartialOut()
        {
            var p = new DefaultResultProcessor();

            // 案例1：中心(5,540) 宽20 高200 → left=-5 出界。
            // 裁剪后 left=0、right=15 → 宽15、中心X=7.5；高度不触边保持200
            var input = new List<DetectionResult> { FakeDetector.Box(5, 540, 20, 200) };
            var outList = p.Process(input, 1920, 1080);

            T.Eq(1, outList.Count, "部分出界框应保留");
            T.Eq(15f, outList[0].Width, "裁剪后宽度=15");
            T.Eq(7.5f, outList[0].X, "裁剪后中心X=7.5");
            T.Eq(200f, outList[0].Height, "未触上/下边高度不变");
            T.Eq(0f, outList[0].Left, "Left 裁剪到画面边缘0");

            // 案例2：右下角同时越界。中心(1900,1070) w40 h40：
            // right=min(1920,1920)=1920 宽度不变；bottom=min(1080,1090)=1080 → 高30、中心Y=1065
            var corner = new List<DetectionResult> { FakeDetector.Box(1900, 1070, 40, 40) };
            var cOut = p.Process(corner, 1920, 1080);
            T.Eq(1, cOut.Count, "右下角框应保留");
            T.Eq(40f, cOut[0].Width, "右边缘恰好贴齐宽度不变");
            T.Eq(30f, cOut[0].Height, "底部裁剪后高度=30");
            T.Eq(1065f, cOut[0].Y, "底部裁剪后中心Y=1065");
        }

        /// <summary>小于 MinWidth=10 或 MinHeight=20 的框视为噪声丢弃（行为红线）</summary>
        private static void MinSizeFilter()
        {
            var p = new DefaultResultProcessor();
            var input = new List<DetectionResult>
            {
                FakeDetector.Box(100, 100, 5, 200),    // 宽5 <10 丢弃
                FakeDetector.Box(100, 100, 10, 200),   // 宽恰为10 保留（< 判定）
                FakeDetector.Box(100, 100, 200, 19),   // 高19 <20 丢弃
                FakeDetector.Box(100, 100, 200, 20),   // 高恰为20 保留
                FakeDetector.Box(100, 100, 9, 15)      // 双超丢弃
            };
            T.Eq(2, p.Process(input, 1920, 1080).Count, "仅两个恰好在阈值上的框应保留");

            // 阈值可调：宽高同时上调到50后，所有框都被过滤——验证属性确实生效
            p.MinWidth = 50;
            p.MinHeight = 50;
            T.Eq(0, p.Process(input, 1920, 1080).Count, "阈值上调到50后全部过滤");
        }

        /// <summary>完全在画面内的框原样保留，数值不被篡改</summary>
        private static void KeepsNormalBox()
        {
            var p = new DefaultResultProcessor();
            var input = new List<DetectionResult> { FakeDetector.Box(500, 400, 120, 260, conf: 0.87f) };
            var output = p.Process(input, 1920, 1080);

            T.Eq(1, output.Count, "正常框应保留");
            T.Eq(500f, output[0].X, "中心X不变");
            T.Eq(400f, output[0].Y, "中心Y不变");
            T.Eq(120f, output[0].Width, "宽度不变");
            T.Eq(260f, output[0].Height, "高度不变");
            T.Eq(0.87f, output[0].Confidence, "置信度不变");
        }

        /// <summary>SizeFilterProcessor 的相对比例上限过滤</summary>
        private static void SizeFilterRatio()
        {
            var f = new SizeFilterProcessor { MaxWidthRatio = 0.5f, MaxHeightRatio = 0.8f };
            var input = new List<DetectionResult>
            {
                FakeDetector.Box(100, 100, 90, 100),    // 90 <= 200*0.5 且 100 <= 200*0.8 保留
                FakeDetector.Box(100, 100, 110, 100),   // 宽超比例上限 丢弃
                FakeDetector.Box(100, 100, 90, 170)     // 高超比例上限 丢弃
            };
            T.Eq(1, f.Process(input, 200, 200).Count, "仅第一个合规框保留");

            // 绝对尺寸下限同样生效
            var f2 = new SizeFilterProcessor();
            var small = new List<DetectionResult> { FakeDetector.Box(100, 100, 9, 19) };
            T.Eq(0, f2.Process(small, 200, 200).Count, "默认绝对下限10x20应过滤小框");
        }

        /// <summary>组合处理器按添加顺序串行执行</summary>
        private static void CompositeChained()
        {
            var composite = new CompositeResultProcessor();
            composite.AddProcessor(new DefaultResultProcessor());          // 先裁剪+滤噪
            composite.AddProcessor(new SizeFilterProcessor                 // 再限制最大占屏比
            {
                MaxWidthRatio = 0.3f,
                MaxHeightRatio = float.MaxValue
            });

            var input = new List<DetectionResult>
            {
                FakeDetector.Box(-50, 540, 200, 200),   // 裁剪后 w=150 >576*0.3? 图宽1920→上限576，150<576 保留
                FakeDetector.Box(960, 100, 900, 150),   // w=900 >576 丢弃
                FakeDetector.Box(100, 100, 5, 100)      // 太窄被 Default 丢弃
            };
            T.Eq(1, composite.Process(input, 1920, 1080).Count, "组合链后仅第1个保留");
        }
    }
}
