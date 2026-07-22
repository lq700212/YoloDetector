using System.Collections.Generic;
using OpenCvSharp;

namespace YoloDetector.YoloDetection
{
    public interface IYoloDetector
    {
        bool IsInitialized { get; }
        
        float ConfidenceThreshold { get; set; }
        
        float NmsThreshold { get; set; }
        
        void Initialize(string modelPath);
        
        List<DetectionResult> Detect(byte[] imageData, int width, int height);
        
        List<DetectionResult> Detect(Mat mat);
        
        void Dispose();
    }
    
    public class DetectionResult
    {
        public int ClassId { get; set; }
        
        public string ClassName { get; set; }
        
        public float Confidence { get; set; }
        
        public float X { get; set; }
        
        public float Y { get; set; }
        
        public float Width { get; set; }
        
        public float Height { get; set; }
        
        public float Left => X - Width / 2;
        
        public float Top => Y - Height / 2;
        
        public float Right => X + Width / 2;
        
        public float Bottom => Y + Height / 2;
    }
    
    public static class YoloClasses
    {
        public static readonly Dictionary<int, string> CocoClasses = new Dictionary<int, string>
        {
            { 0, "person" },
            { 1, "bicycle" },
            { 2, "car" },
            { 3, "motorcycle" },
            { 4, "airplane" },
            { 5, "bus" },
            { 6, "train" },
            { 7, "truck" },
            { 8, "boat" },
            { 9, "traffic light" },
            { 10, "fire hydrant" },
            { 11, "stop sign" },
            { 12, "parking meter" },
            { 13, "bench" },
            { 14, "bird" },
            { 15, "cat" },
            { 16, "dog" },
            { 17, "horse" },
            { 18, "sheep" },
            { 19, "cow" },
            { 20, "elephant" },
            { 21, "bear" },
            { 22, "zebra" },
            { 23, "giraffe" },
            { 24, "backpack" },
            { 25, "umbrella" },
            { 26, "handbag" },
            { 27, "tie" },
            { 28, "suitcase" },
            { 29, "frisbee" },
            { 30, "skis" },
            { 31, "snowboard" },
            { 32, "sports ball" },
            { 33, "kite" },
            { 34, "baseball bat" },
            { 35, "baseball glove" },
            { 36, "skateboard" },
            { 37, "surfboard" },
            { 38, "tennis racket" },
            { 39, "bottle" },
            { 40, "wine glass" },
            { 41, "cup" },
            { 42, "fork" },
            { 43, "knife" },
            { 44, "spoon" },
            { 45, "bowl" },
            { 46, "banana" },
            { 47, "apple" },
            { 48, "sandwich" },
            { 49, "orange" },
            { 50, "broccoli" },
            { 51, "carrot" },
            { 52, "hot dog" },
            { 53, "pizza" },
            { 54, "donut" },
            { 55, "cake" },
            { 56, "chair" },
            { 57, "couch" },
            { 58, "potted plant" },
            { 59, "bed" },
            { 60, "dining table" },
            { 61, "toilet" },
            { 62, "tv" },
            { 63, "laptop" },
            { 64, "mouse" },
            { 65, "remote" },
            { 66, "keyboard" },
            { 67, "cell phone" },
            { 68, "microwave" },
            { 69, "oven" },
            { 70, "toaster" },
            { 71, "sink" },
            { 72, "refrigerator" },
            { 73, "book" },
            { 74, "clock" },
            { 75, "vase" },
            { 76, "scissors" },
            { 77, "teddy bear" },
            { 78, "hair drier" },
            { 79, "toothbrush" }
        };
    }
}