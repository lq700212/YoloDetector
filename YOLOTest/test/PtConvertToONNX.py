import onnx

# 加载 ONNX 模型
onnx_model = onnx.load("../models/yolo26n.onnx")

# 检查模型是否正确
try:
    onnx.checker.check_model(onnx_model)
    print("ONNX 模型检查通过，模型结构正确！")
except Exception as e:
    print("ONNX 模型检查失败：", e)