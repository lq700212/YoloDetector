from ultralytics import YOLO

# 加载导出的 ONNX 模型
model = YOLO("../models/yolo26n.onnx")

# 对示例图片进行预测（这里使用 Ultralytics 提供的示例图片 https://ultralytics.com/images/bus.jpg）
results = model("bus.jpg", save=True)
results = model("WIN_20260715_13_59_47_Pro.jpg", save=True)
results = model("2026-07-15_142342_432.png", save=True)

# 打印模型包含的类别名称
print("模型包含的类别有：", model.names)