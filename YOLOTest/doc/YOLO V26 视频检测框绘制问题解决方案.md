# YOLO V26 视频检测框绘制问题解决方案

## 问题描述

在使用YOLO V26进行视频检测时，检测框和置信度未能正确绘制到视频画面上。但单张图片检测正常，检测框准确生成，置信度0.90以上。怀疑问题出在代码中的OpenCV绘制环节。

## 解决方案

直接使用YOLO V26内置的可视化API，避免手动使用OpenCV绘制，排除其他干扰因素。

### 核心参数

| 参数 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `show` | bool | False | 是否实时显示带框画面 |
| `save` | bool | False | 是否保存结果到磁盘 |
| `show_conf` | bool | True | 显示置信度分数 |
| `show_labels` | bool | True | 显示类别标签 |
| `stream` | bool | False | 流式模式处理视频 |

### 关键设置

1. **视频处理必须使用流式模式**: `stream=True`
2. **服务器环境**: `show=False`, `save=True`
3. **本地调试**: `show=True`, `save=True`
4. **置信度显示**: 默认已开启 `show_conf=True`

## 完整Demo代码

```python
from ultralytics import YOLO

# 加载模型
model = YOLO("yolo26n.pt")  # 可替换为 yolo26s.pt, yolo26m.pt 等

# 视频源（文件路径或摄像头索引）
source = "path/to/your/video.mp4"  # 或使用 0 表示摄像头

# 使用流式模式处理视频，启用保存
results = model.predict(
    source=source, 
    stream=True,      # 必须设置，避免内存溢出
    save=True,        # 保存结果到 runs/detect/predict/
    show=False,       # 服务器环境设为False，本地调试可设为True
    show_conf=True,   # 显示置信度
    show_labels=True  # 显示类别标签
)

# 遍历结果（必须执行，否则不会真正处理）
for result in results:
    # result.boxes 包含检测框信息
    # result.boxes.xyxy - 框坐标
    # result.boxes.conf - 置信度
    # result.boxes.cls - 类别
    pass

print("视频处理完成，结果已保存至 runs/detect/predict/ 目录")