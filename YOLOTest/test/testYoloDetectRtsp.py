from ultralytics import YOLO

# 你的RTSP流地址
rtsp_url = "rtsp://192.168.1.188:554/ch01.264"

# 加载模型并直接处理RTSP流
model = YOLO("yolo26n.pt")
results = model.predict(source=rtsp_url, stream=True, show=True, conf=0.5)

# 遍历结果（必须执行）
for result in results:
    pass  # YOLO自动处理所有显示和绘制