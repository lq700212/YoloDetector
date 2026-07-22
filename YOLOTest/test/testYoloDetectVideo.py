from ultralytics import YOLO

# 加载预训练的 YOLO26n 模型（可根据需要替换为 yolo26s.pt、yolo26m.pt 等）
model = YOLO("../models/yolo26n.onnx")

# 视频文件路径或摄像头索引（0 表示默认摄像头）
source = "15-10-54.mp4"  # 替换为您的视频文件路径，或使用 0 表示摄像头

# 使用流式模式逐帧处理视频，启用保存结果到磁盘
results = model.predict(source=source, stream=True, save=True, show=False)

# 遍历每一帧的检测结果
for result in results:
    # result 是一个 Results 对象，包含该帧的检测信息
    boxes = result.boxes  # 获取检测框对象
    # 如果需要，可以在此处访问 boxes.xyxy、boxes.conf、boxes.cls 等属性进行进一步处理
    
    # 由于设置了 save=True，YOLO 会自动将带框结果保存到 runs/detect/predict/ 目录下
    # 如果设置了 show=True，则会在此处弹出窗口显示当前帧的检测结果

print("视频处理完成，结果已保存至 runs/detect/predict/ 目录。")