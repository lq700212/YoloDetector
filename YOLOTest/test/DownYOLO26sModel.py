from ultralytics import YOLO

# 加载 YOLO V26 nano 模型，首次运行会自动下载权重
model = YOLO("../models/yolo26s.pt")

# 将模型导出为 ONNX 格式
model.export(format="onnx")