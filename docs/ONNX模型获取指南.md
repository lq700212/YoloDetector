# 背景与概述

**YOLO V26** 是 Ultralytics 公司于 2025 年发布的最新一代目标检测模型，属于 YOLO 系列的进化版本。它在前代基础上引入了多项架构创新和优化，如端到端无 NMS 推理、移除 DFL 损失等，旨在简化模型部署流程、提升推理效率。YOLO V26 支持多种任务，包括目标检测、实例分割、姿态估计等，并提供 nano、small、medium、large、xlarge 五种模型规模，以适应从边缘设备到高性能服务器的不同硬件环境。

对于初学者而言，**获取并使用 YOLO V26 的 ONNX 模型** 可能略显复杂。但别担心，本指南将以**新手小白**的视角，一步步教你如何从零开始，完成 YOLO V26 模型的获取、ONNX 格式转换以及基本验证。我们将手把手指导你完成以下任务：

1. **环境搭建**：安装 Python 和必要的深度学习库。
2. **模型获取**：获取 YOLO V26 预训练模型权重。
3. **ONNX 导出**：将模型转换为 ONNX 格式。
4. **模型验证**：检查 ONNX 模型是否包含“人”类别（COCO 数据集的 person 类别），并进行简单推理测试。

在开始之前，请确保你有一台能联网的电脑，并且对命令行操作有基本的了解。本指南将使用 Windows 系统作为演示环境，但大部分步骤在 macOS 或 Linux 上也类似，只是命令可能略有不同。

# 第一步：环境搭建（安装 Python 与依赖库）

在获取和使用 YOLO V26 模型之前，我们需要先搭建好 Python 运行环境，并安装必要的深度学习库。这一步是**基础且关键**的，因为后续所有操作都将在 Python 环境中进行。

## 1. 安装 Python 解释器

如果你的电脑上还没有安装 Python，需要先下载并安装 Python 解释器。Python 是运行 YOLO 模型和相关代码的基础环境。以下是安装步骤：

1. **下载 Python**：访问 Python 官网下载页面（https://www.python.org/downloads/），选择适合你操作系统的版本下载。建议下载最新的 Python 3.x 版本（例如 3.10 或 3.11），因为 YOLO V26 依赖的一些库可能对 Python 版本有要求。对于 Windows 用户，可以下载 64 位的安装程序（例如 `python-3.11.x-amd64.exe`）。
2. **运行安装程序**：双击下载的安装包，启动 Python 安装向导。在安装过程中，请务必勾选**“Add Python to PATH”**（将 Python 添加到环境变量）选项。这个选项非常重要，它会将 Python 解释器路径添加到系统环境变量中，使得你可以在命令行任何位置直接使用 `python` 命令。如果不勾选，后续在命令行输入 `python` 可能会提示找不到命令。
3. **选择安装方式**：在安装向导中，你可以选择“Install Now”（立即安装）进行默认安装，或者选择“Customize installation”自定义安装。对于新手，建议选择“Install Now”使用默认设置即可。安装程序会将 Python 解释器安装到默认路径（Windows 上通常是 `C:\Users\你的用户名\AppData\Local\Programs\Python\Python3x`），并自动配置环境变量。
4. **验证安装**：安装完成后，按 `Win + R` 键打开“运行”窗口，输入 `cmd` 回车打开命令提示符。在命令行中输入 `python --version` 并回车。如果看到类似 `Python 3.11.5` 的版本信息输出，说明 Python 安装成功。如果提示“'python' 不是内部或外部命令”，则可能是环境变量未生效，需要重启电脑或者手动将 Python 安装路径添加到系统环境变量中。

*提示：*如果你使用的是 macOS 或 Linux，Python 通常已经预装。但为了确保版本符合要求，也可以通过类似方式安装最新版的 Python 3。

## 2. 安装 Ultralytics 库及依赖

安装完 Python 后，我们需要安装 **Ultralytics** 库，这是 YOLO V26 官方提供的 Python 包，包含了模型定义、预训练权重下载、推理和训练等功能。此外，Ultralytics 库会自动处理大部分依赖库的安装，例如 PyTorch、OpenCV 等。

在命令提示符中执行以下命令来安装或升级 Ultralytics：

```bash
pip install -U ultralytics
```

**命令解析**：`pip` 是 Python 的包管理工具，`install` 表示安装，`-U`（或 `--upgrade`）表示如果已安装则升级到最新版本，`ultralytics` 是包名。执行此命令后，pip 会从 Python 包索引（PyPI）下载 Ultralytics 并安装，同时自动安装其声明的依赖项，如 PyTorch、OpenCV 等。

*注意*：由于网络原因，在国内直接使用 pip 可能会遇到下载速度慢或超时的问题。如果遇到安装失败，可以尝试使用国内的镜像源加速。例如，使用清华大学的镜像源安装：

```bash
pip install -U ultralytics -i https://pypi.tuna.tsinghua.edu.cn/simple
```

其中 `-i` 参数指定了镜像源地址。你也可以替换为其他镜像，如阿里云 `https://mirrors.aliyun.com/pypi/simple/` 等。

安装完成后，可以通过运行 `yolo help` 命令来验证 Ultralytics 是否安装成功。在命令行输入：

```bash
yolo help
```

如果看到打印出 YOLO 的帮助信息和使用说明，说明 Ultralytics 库已正确安装。

> **小结**：至此，你已经在电脑上搭建好了运行 YOLO V26 所需的 Python 环境和库。接下来，我们将获取 YOLO V26 的预训练模型权重，并将其转换为 ONNX 格式。

# 第二步：获取 YOLO V26 预训练模型权重

Ultralytics 提供了多种方式来获取 YOLO V26 的预训练模型权重（`.pt` 文件）。最简单的方法是**自动下载**，即在代码中直接加载模型时让库自动从官方下载。此外，你也可以手动从 GitHub 下载权重文件。下面分别介绍这两种方法。

## 1. 自动下载官方权重文件

Ultralytics 官方在 GitHub 上发布了 YOLO V26 的预训练模型权重文件，并将其托管在 GitHub Releases 上。当你在代码中首次加载某个模型（例如 `yolo26n.pt`）时，Ultralytics 库会自动从官方仓库下载对应的权重文件到本地。这种方式非常方便，无需你手动查找和下载文件。

例如，你可以编写一个简单的 Python 脚本（或直接在 Python 解释器中）运行以下代码：

```python
from ultralytics import YOLO

# 加载 YOLO V26 nano 模型，首次运行会自动下载权重
model = YOLO("yolo26n.pt")
```

执行上述代码时，程序会检测本地是否存在 `yolo26n.pt` 文件，如果不存在，则会从网络下载并加载。下载的文件通常保存在当前工作目录或 Ultralytics 的默认下载路径中。这种方式省去了手动下载的麻烦，**推荐新手使用**。

*提示*：YOLO V26 提供了多个模型规模，如 nano (n)、small (s)、medium (m)、large (l)、xlarge (x) 等。模型规模越小，速度越快；规模越大，精度更高但速度较慢。在本指南中，我们使用 **YOLO V26n**（nano 版本）作为示例，因为它参数量小、速度快，非常适合在 CPU 或边缘设备上运行，且实测检测效果良好。

## 2. 手动下载权重文件（可选）

如果你希望手动下载 YOLO V26 的权重文件（例如在离线环境中使用），也可以从 Ultralytics 的官方资源仓库获取。Ultralytics 将所有预训练模型权重托管在 **GitHub Assets** 仓库的 Releases 中。以下是具体步骤：

1. **访问 Ultralytics Assets 仓库**：打开浏览器，访问 [Ultralytics Assets 仓库](https://github.com/ultralytics/assets)。这个仓库专门用于存储 Ultralytics 共享的模型权重、数据集等资源。

2. **进入 Releases 页面**：在仓库页面点击“Releases”（发布）标签，查看所有发布版本。找到最新的版本 **v8.4.0**，该版本对应 YOLO V26 模型的发布。

3. **下载权重文件**：在 v8.4.0 发布页面，你会看到所有可下载的模型权重文件。这些文件名遵循 `yolo26*.pt` 的命名规则，例如：

   - **YOLO V26n**（nano）：[yolo26n.pt](https://github.com/ultralytics/assets/releases/download/v8.4.0/yolo26n.pt)
   - **YOLO V26s**（small）：[yolo26s.pt](https://github.com/ultralytics/assets/releases/download/v8.4.0/yolo26s.pt)
   - **YOLO V26m**（medium）：[yolo26m.pt](https://github.com/ultralytics/assets/releases/download/v8.4.0/yolo26m.pt)
   - **YOLO V26l**（large）：[yolo26l.pt](https://github.com/ultralytics/assets/releases/download/v8.4.0/yolo26l.pt)
   - **YOLO V26x**（xlarge）：[yolo26x.pt](https://github.com/ultralytics/assets/releases/download/v8.4.0/yolo26x.pt)

   点击你所需模型文件名，浏览器会开始下载对应的 `.pt` 文件。例如，下载 `yolo26n.pt` 文件并保存到你的项目文件夹中。

4. **加载本地权重**：在代码中加载模型时，指定本地权重文件的路径。例如，如果你将 `yolo26n.pt` 下载到了 `D:\models\yolo26n.pt`，则可以这样加载：

   ```python
   from ultralytics import YOLO
   model = YOLO("D:/models/yolo26n.pt")
   ```

   这样就会直接加载本地的权重文件，而不会触发自动下载。

无论采用哪种方式，你最终都需要一个 `.pt` 格式的 YOLO V26 模型权重文件。在本例中，我们假设你已经通过上述方法获取了 **YOLO V26n** 的权重文件 `yolo26n.pt`。接下来，我们将演示如何将其转换为 ONNX 格式。

> **小结**：现在你手上应该已经有了 YOLO V26 的预训练模型权重。如果使用自动下载方式，Ultralytics 已经帮你准备好了；如果手动下载，也请确保将 `.pt` 文件保存在你能方便访问的位置。下一步，我们将把模型转换为 ONNX 格式，以便后续在不同环境中使用。

# 第三步：导出 ONNX 模型文件

**ONNX**（Open Neural Network Exchange）是一种开放的模型格式，由微软、Facebook 等公司共同开发，旨在让模型能够在不同框架和硬件上无缝部署。将 YOLO V26 模型导出为 ONNX 格式后，你就可以使用 ONNX Runtime 等推理引擎来运行模型，而不依赖 PyTorch。这在部署到 Windows 桌面程序（如 Winform）或嵌入式设备时非常有用，因为 ONNX Runtime 对 Windows 支持良好，且可利用 CPU/GPU 加速推理。

Ultralytics 提供了非常简便的 API 来导出模型为 ONNX 格式。你只需要加载一个 YOLO 模型，然后调用其 `export` 方法即可。

## 1. 导出 ONNX 模型的基本命令

下面是一个完整的 Python 脚本示例，演示如何将 `yolo26n.pt` 导出为 `yolo26n.onnx`：

```python
from ultralytics import YOLO

# 加载 YOLO V26 模型（这里以 nano 版本为例）
model = YOLO("yolo26n.pt")

# 将模型导出为 ONNX 格式
model.export(format="onnx")
```

运行上述代码后，Ultralytics 会在当前目录下生成一个名为 `yolo26n.onnx` 的文件，即转换后的 ONNX 模型。默认情况下，导出的 ONNX 模型文件名与原始模型相同，只是扩展名变为 `.onnx`，保存在与 `.pt` 文件相同的目录下。

*注意*：如果你使用的是 Windows，且 PyTorch 未配置 GPU 支持，那么导出过程可能较慢，因为默认在 CPU 上进行模型转换。导出过程中，Ultralytics 会调用 PyTorch 的 `torch.onnx.export` 功能，将模型结构和权重转换为 ONNX 格式。这个过程通常只需要几秒钟到几十秒，具体取决于模型大小和硬件性能。

## 2. 验证 ONNX 模型是否导出成功

导出完成后，你可以通过以下方式验证 ONNX 模型是否成功生成：

1. **检查文件存在**：在你的项目目录中查找 `yolo26n.onnx` 文件。如果文件存在且大小不为零，说明导出过程已经完成。Ultralytics 的 `export` 方法通常会打印出保存路径，例如：`ONNX model exported to: yolo26n.onnx`。

2. **加载并检查模型**（可选）：你可以使用 ONNX Python 库来加载并检查模型的结构，确保模型转换正确。例如：

   ```python
   import onnx
   
   # 加载 ONNX 模型
   onnx_model = onnx.load("yolo26n.onnx")
   
   # 检查模型是否正确
   try:
       onnx.checker.check_model(onnx_model)
       print("ONNX 模型检查通过，模型结构正确！")
   except Exception as e:
       print("ONNX 模型检查失败：", e)
   ```

   如果上述代码输出“ONNX 模型检查通过”，则表示导出的 ONNX 模型文件是有效的。

## 3. 常见导出问题与解决

在导出 ONNX 模型时，新手可能会遇到一些常见问题。下面列举几个可能的情况及解决方法：

1. **问题：导出时报错“ImportError: No module named 'onnx'”**
   *原因*：Ultralytics 在导出 ONNX 时需要调用 `onnx` 模块，但你的环境中可能未安装 ONNX Python 库。
   *解决*：使用 pip 安装 ONNX 库：

   ```bash
   pip install onnx
   ```

   安装后重新运行导出代码即可。

2. **问题：导出时提示缺少 `onnx-simplifier` 或 `onnxruntime`**
   *原因*：Ultralytics 的导出流程中，某些高级选项（如模型简化、动态输入等）需要额外的依赖库。
   *解决*：安装缺失的依赖库。例如：

   ```bash
   pip install onnx-simplifier onnxruntime
   ```

   安装完成后重试导出。

3. **问题：导出过程卡住或非常缓慢**
   *原因*：可能是因为默认在 CPU 上进行模型转换，且模型较大导致耗时较长。另外，如果你的电脑开启了杀毒软件或防火墙，可能会拦截 PyTorch 导出过程中的某些操作，导致卡顿。
   *解决*：耐心等待导出完成。如果长时间无响应，可以尝试关闭杀毒软件/防火墙后重试。确保你的电脑有足够内存（导出大模型时需要较多内存）。必要时，可以尝试在 GPU 环境下导出（需要安装 GPU 版本的 PyTorch）。

4. **问题：导出的 ONNX 模型在某些环境中无法使用**
   *原因*：可能是因为 ONNX 模型使用了动态输入尺寸，而部署环境不支持动态 shape。默认情况下，Ultralytics 导出的 ONNX 模型支持动态输入尺寸（方便不同分辨率输入），但某些推理引擎或硬件可能需要固定输入尺寸。
   *解决*：在导出时指定 `dynamic=False` 参数，以生成固定输入尺寸的 ONNX 模型。例如：

   ```python
   model.export(format="onnx", dynamic=False)
   ```

   这样导出的模型输入尺寸将被固定为模型训练时的尺寸（例如 640x640），在某些环境下更容易部署。

完成 ONNX 模型的导出后，你已经有了一个 `.onnx` 文件。下一步，我们将验证该 ONNX 模型是否包含“人”类别，并进行一次简单的推理测试，以确保模型可以正常工作。

> **小结**：通过简单的几行代码，你就成功将 YOLO V26 模型转换为了通用的 ONNX 格式。这使得模型不再局限于 PyTorch 框架，为后续在不同平台部署打下了基础。接下来，我们将加载这个 ONNX 模型并验证其功能。

# 第四步：使用 ONNX 模型进行推理与验证

导出 ONNX 模型后，我们有必要对模型进行一次**验证**，以确保：

- 模型确实包含了“人”（person）类别，这是我们后续应用所需的类别。
- ONNX 模型能够正常进行推理，给出合理的检测结果。

Ultralytics 提供了非常方便的接口来加载 ONNX 模型并进行推理验证。我们将使用一张示例图片来测试模型是否能够检测出画面中的“人”。

## 1. 使用 Ultralytics 加载 ONNX 模型

Ultralytics 的 `YOLO` 类不仅支持加载 `.pt` 权重文件，也支持直接加载 ONNX 格式的模型。加载 ONNX 模型的方法与加载权重文件类似，只需将文件路径指定为 `.onnx` 文件即可。

下面是一个完整的 Python 脚本示例，演示如何加载之前导出的 `yolo26n.onnx` 模型，并对一张图片进行预测：

```python
from ultralytics import YOLO

# 加载导出的 ONNX 模型
model = YOLO("yolo26n.onnx")

# 对示例图片进行预测（这里使用 Ultralytics 提供的示例图片 URL）
results = model("https://ultralytics.com/images/bus.jpg", save=True)
```

上述代码首先加载 `yolo26n.onnx` 模型，然后使用该模型对一张图片进行检测。图片来源是 Ultralytics 提供的一张示例图片（包含人物和公交车等物体）。`save=True` 参数表示将检测结果保存为图片文件。

运行上述代码后，Ultralytics 会在当前目录下生成一个 `runs/detect/predict` 文件夹，里面包含检测结果的可视化图片（例如 `bus.jpg`）。你可以打开该图片查看模型检测到的目标框和类别。

## 2. 确认模型包含 “person” 类别

为了确认模型确实包含“人”类别，我们可以检查模型的输出类别列表。Ultralytics 在模型对象中提供了类别名称列表，通常保存在 `model.names` 中。对于 COCO 数据集训练的 YOLO 模型，`model.names` 是一个字典，键是类别索引，值是对应的类别名称。

你可以通过以下代码打印出模型包含的所有类别名称：

```python
# 打印模型包含的类别名称
print("模型包含的类别有：", model.names)
```

对于 YOLO V26n 模型（COCO 80类），`model.names` 应该输出类似如下的内容（节选）：

```
{0: 'person', 1: 'bicycle', 2: 'car', 3: 'motorcycle', 4: 'airplane', ..., 79: 'toothbrush'}
```

可以看到，索引 0 对应的类别名称是 **'person'**，这正是我们需要的“人”类别。这证明了我们导出的 ONNX 模型确实包含了 COCO 数据集中的“人”类别。

## 3. 使用 ONNX Runtime 加载 ONNX 模型（可选）

除了使用 Ultralytics 自带的推理接口，你也可以使用微软开源的 **ONNX Runtime** 来加载和运行 ONNX 模型。这种方式在某些场景下性能更高，且不依赖 Ultralytics 库。下面是一个使用 ONNX Runtime 进行推理的简单示例：

```python
import onnxruntime as ort
import cv2
import numpy as np

# 加载 ONNX 模型
session = ort.InferenceSession("yolo26n.onnx")

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
```

上述代码演示了如何使用 ONNX Runtime 加载模型并对一张图片进行预处理和推理。需要注意的是，YOLO 模型的输出后处理比较复杂，需要解析边界框、置信度和类别，并执行非极大值抑制（NMS）等操作。这部分逻辑在 Ultralytics 中已经封装好，但如果使用 ONNX Runtime 直接推理，你需要自行实现后处理逻辑，或参考相关教程。

*提示*：对于新手而言，使用 Ultralytics 提供的接口来加载 ONNX 模型进行推理是更简单的方式，因为它内部已经处理了模型输入输出格式的转换和后处理逻辑。如果你只是想验证模型是否正常工作，使用 Ultralytics 的方法即可。如果后续需要在 C# 等环境中部署，再考虑使用 ONNX Runtime 的 API。

> **小结**：通过以上步骤，我们已经成功获取并验证了 YOLO V26 的 ONNX 模型。模型包含了“人”类别，并且能够对图像进行检测。这意味着你已经完成了从零开始获取并使用 YOLO V26 ONNX 模型的全过程。接下来，我们将讨论如何将这个模型集成到实际的工控领域应用中。

# 第五步：后续集成与 Winform 程序

在本指南中，我们专注于**获取并验证 ONNX 模型**这一核心任务。然而，你的最终目标可能是将模型集成到一个 Windows 桌面应用程序（Winform）中，实现对摄像头画面的实时监控和提醒。

将 YOLO V26 ONNX 模型集成到 Winform 程序中，主要涉及以下几个方面的技术选型和开发工作：

1. **推理引擎选择**：在 Winform 中，你可以选择使用 **ONNX Runtime** 的 C# 版本（`Microsoft.ML.OnnxRuntime`）来加载 ONNX 模型并执行推理。ONNX Runtime 对 Windows 支持良好，可以方便地通过 NuGet 包管理器集成到 C# 项目中。它支持 CPU 推理，并且如果机器有 GPU，也可以通过安装 GPU 版本的 ONNX Runtime 来加速推理。
2. **摄像头画面捕获**：在 Winform 中，你需要使用 OpenCV 的 C# 封装库（如 **OpenCvSharp**）来读取摄像头视频流。OpenCvSharp 提供了与 OpenCV 类似的接口，可以方便地捕获摄像头帧、显示视频画面等。你需要在 Winform 后台线程中循环读取摄像头画面，并将其传递给模型进行推理。
3. **模型推理与结果处理**：在 Winform 后台线程中，使用 ONNX Runtime 加载的模型对每一帧画面进行目标检测。由于 ONNX Runtime 的推理结果是原始的张量数据（例如包含边界框坐标和类别概率的数组），你需要在 C# 中实现与 Ultralytics 类似的后处理逻辑，包括解码边界框、过滤置信度、执行 NMS 等。这部分逻辑相对复杂，但可以参考 Ultralytics 的 Python 实现来移植到 C#。
4. **结果可视化与提醒**：将检测到的“人”位置在 Winform 界面上用矩形框标注出来，并根据业务逻辑判断工人的操作是否合规。例如，你可以检查检测到的“人”是否出现在某个特定区域，或者检测“手”是否靠近某个目标物体。如果检测到异常操作（如未拨动摇杆就进入危险区域），则在界面上弹出提醒或触发其他报警机制。
5. **性能优化**：实时视频处理对性能要求较高。你需要确保模型推理和图像处理在后台线程进行，不阻塞 UI 线程，保证界面流畅。同时，可以采取一些优化措施，如降低摄像头分辨率、使用多线程并行处理帧、或者利用 ONNX Runtime 的批量推理功能等，来提高整体帧率。

将模型集成到 Winform 程序是一个复杂的工程任务，涉及 C# 编程、多线程处理、计算机视觉等多个领域的知识。这超出了本指南的范围，但你可以参考一些开源项目和教程来逐步实现。例如，有开发者已经实现了基于 YOLOv8 的手势识别 Winform 系统源码，可供参考。你也可以在 CSDN、Stack Overflow 等社区搜索相关问题，获取更多灵感。

# 总结

通过本指南的学习，你已经掌握了**从零开始获取并使用 YOLO V26 ONNX 模型**的完整流程。我们一步步讲解了环境搭建、模型获取、ONNX 导出以及模型验证的全过程，并对常见问题给出了详细解答。现在，你已经拥有了一个包含“人”类别的 YOLO V26 ONNX 模型，并了解了如何在 Python 中加载和使用它。

回顾整个旅程，你可能会感叹其实并没有想象中那么难。正如我们所见，Ultralytics 提供了高度封装的 API，使得获取和使用模型变得非常简单。ONNX 格式的开放性和 ONNX Runtime 的强大支持，也为模型部署扫清了障碍。

当然，将模型集成到实际应用中还有许多工作要做，特别是在 Winform 程序中实现实时监控和智能提醒。但请记住，**千里之行，始于足下**。你已经迈出了最关键的一步——成功获取并验证了模型。后续的开发工作虽然复杂，但有了这个基础，你就可以逐步构建出完整的应用了。

希望本指南对你有所帮助！如果在实践过程中遇到任何问题，不要气馁，多查阅资料、多尝试，你一定能够克服困难。祝你在工控领域的深度学习应用开发之路上越走越远，最终打造出属于自己的智能监控系统！