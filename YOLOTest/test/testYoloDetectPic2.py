import onnxruntime as ort
import cv2
import numpy as np

# 加载 ONNX 模型
session = ort.InferenceSession("../models/yolo26n.onnx")

# 获取输入和输出节点名称
input_name = session.get_inputs()[0].name
output_name = session.get_outputs()[0].name

# 读取并预处理图像
image = cv2.imread("bus.jpg")  # 使用本地图片路径
image = cv2.resize(image, (640, 640))  # 调整尺寸为模型输入尺寸
image = image.astype(np.float32) / 255.0  # 归一化到 [0,1]
image = np.transpose(image, (2, 0, 1))  # HWC 转 CHW
image = np.expand_dims(image, axis=0)  # 增加批次维度

# 运行推理
outputs = session.run([output_name], {input_name: image})

# 处理输出（这里需要根据 YOLO 输出格式进行解析，略复杂，此处省略）
# ...
print(outputs)