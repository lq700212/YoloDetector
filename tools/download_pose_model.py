# -*- coding: utf-8 -*-
"""
download_pose_model.py — 下载 YOLO11n-pose 姿态模型(ONNX) 到 Detection/model/

用途：
    静电杆触摸检测需要 YOLO-pose 系列姿态模型（COCO 17 关键点，含左右手腕）。
    本脚本把 yolo11n-pose.onnx 下载到 Detection/model/yolo11n-pose.onnx，
    供 esdConfig.json 的 PoseModelPath 使用。

下载策略（多级回退，全自动）：
    1. 依次尝试 ONNX 直链候选（HuggingFace 官方组织），校验 HTTP 200 + 大小 + 非 HTML；
    2. 全部失败时回退正规渠道：GitHub assets 下载官方 .pt 权重，
       再用 ultralytics 官方 API 本地导出 ONNX（--export 时自动 pip 安装依赖）。

    实测结论（2026-08）：Ultralytics 官方只发布 .pt 权重、不提供现成 ONNX，
    所以 --export 才是正源路线；直链仅是网络通时的捷径。

网络说明：
    VPN/公司网常见"系统代理模式"，Python urllib 默认不读 Windows 系统代理，
    会全部连接超时——本脚本自动读注册表挂上系统代理（127.0.0.1:端口）。

用法：
    python tools/download_pose_model.py             # 先试直链，失败下载 .pt 并提示
    python tools/download_pose_model.py --export    # 直链失败时自动装 ultralytics 导出 ONNX
    python tools/download_pose_model.py --force     # 忽略已有模型强制重新获取

注意：
    - 脚本含中文，保存为 UTF-8 编码（Python3 默认即 UTF-8）。
      ★维护警告：不要用 PowerShell Get-Content/Set-Content 改本文件（会转乱码），
      用编辑器或 AI 的文件写入工具。
    - 仅依赖标准库；--export 路线按需 pip 安装 ultralytics/onnx/onnxruntime。
"""

import os
import sys
import urllib.request
import urllib.error

# 目标文件路径（相对仓库根；脚本可从任意目录执行）
REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
TARGET_DIR = os.path.join(REPO_ROOT, "Detection", "model")
TARGET_ONNX = os.path.join(TARGET_DIR, "yolo11n-pose.onnx")

MIN_ONNX_BYTES = 5 * 1024 * 1024   # yolo11n-pose.onnx 约 11MB，低于 5MB 视为损坏页
UA = {"User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64)"}  # 部分 CDN 拒绝默认 UA

# ONNX 直链候选（按可靠性排序；失败自动换下一个）
ONNX_URLS = [
    "https://huggingface.co/Ultralytics/YOLO11/resolve/main/yolo11n-pose.onnx",
]

# 官方 .pt 权重（GitHub assets，长期稳定；用于 --export 导出路线）
PT_URL = "https://github.com/ultralytics/assets/releases/download/v8.3.0/yolo11n-pose.pt"
TARGET_PT = os.path.join(TARGET_DIR, "yolo11n-pose.pt")


def _detect_system_proxy():
    """读 Windows 注册表里的系统代理（IE/VPN 客户端设置的 127.0.0.1:端口）。"""
    try:
        import winreg
        key = winreg.OpenKey(winreg.HKEY_CURRENT_USER,
                             r"Software\Microsoft\Windows\CurrentVersion\Internet Settings")
        enable, _ = winreg.QueryValueEx(key, "ProxyEnable")
        server, _ = winreg.QueryValueEx(key, "ProxyServer")
        winreg.CloseKey(key)
        if enable and server:
            if not server.startswith("http"):
                server = "http://" + server
            return server
    except Exception:
        pass
    return None


_PROXY = _detect_system_proxy()
_OPENER = urllib.request.build_opener(urllib.request.ProxyHandler(
    {"http": _PROXY, "https": _PROXY})) if _PROXY else urllib.request.build_opener()


def log(msg):
    print("[download] " + msg, flush=True)


def looks_like_html(head_bytes):
    """错误页/软链页通常是 HTML，检查文件头即可识别"""
    head = head_bytes[:256].lower()
    return b"<html" in head or b"<!doctype html" in head or b"<?xml" in head


def download(url, dest, min_bytes):
    """流式下载到临时文件，校验通过后原子改名。返回 True/False。"""
    tmp = dest + ".part"
    try:
        req = urllib.request.Request(url, headers=UA)
        with _OPENER.open(req, timeout=60) as resp:
            if resp.status != 200:
                log("  HTTP %d, 跳过 %s" % (resp.status, url))
                return False

            total = 0
            first_chunk = b""
            with open(tmp, "wb") as f:
                while True:
                    chunk = resp.read(1 << 20)  # 1MB
                    if not chunk:
                        break
                    if total == 0:
                        first_chunk = chunk[:256]
                    f.write(chunk)
                    total += len(chunk)

        if total < min_bytes:
            log("  文件过小(%d bytes), 判定无效: %s" % (total, url))
            os.remove(tmp)
            return False
        if looks_like_html(first_chunk):
            log("  返回的是HTML页面而非模型文件: %s" % url)
            os.remove(tmp)
            return False

        if os.path.exists(dest):
            os.remove(dest)
        os.replace(tmp, dest)
        log("  下载成功: %s (%.1f MB)" % (dest, total / 1048576.0))
        return True

    except (urllib.error.URLError, urllib.error.HTTPError, OSError) as ex:
        log("  失败: %s (%s)" % (url, ex))
        if os.path.exists(tmp):
            try:
                os.remove(tmp)
            except OSError:
                pass
        return False


def validate_onnx(path):
    """有 onnxruntime 时加载会话验证输入输出形状（可选增强校验）"""
    try:
        import onnxruntime as ort
        sess = ort.InferenceSession(path, providers=["CPUExecutionProvider"])
        for inp in sess.get_inputs():
            log("  校验通过: input=%s shape=%s" % (inp.name, inp.shape))
        for out in sess.get_outputs():
            log("           output=%s shape=%s" % (out.name, out.shape))
        return True
    except ImportError:
        log("  （未安装 onnxruntime，跳过运行时校验——仅做基础完整性检查）")
        return True
    except Exception as ex:
        log("  onnxruntime 校验失败: %s" % ex)
        return False


def export_from_pt(pt_path):
    """用 ultralytics 把 .pt 权重导出为 ONNX（需要 pip install ultralytics onnx）"""
    log("尝试 ultralytics 官方导出路线 ...")
    try:
        from ultralytics import YOLO
    except ImportError:
        log("  未安装 ultralytics，正在 pip 安装（含 torch，体积较大请耐心等待）...")
        import subprocess
        cmd = [sys.executable, "-m", "pip", "install"]
        if _PROXY:
            cmd += ["--proxy", _PROXY]
        cmd += ["ultralytics", "onnx", "onnxruntime"]
        if subprocess.call(cmd) != 0:
            log("  pip 安装失败，导出终止")
            return False

    model = YOLO(pt_path)

    # 静态 640 输入与 C# 检测器约定一致（InputMetadata 读出固定 shape）
    exported = model.export(format="onnx", imgsz=640, opset=12, batch=1, simplify=True)

    # export 返回导出文件路径；兼容 str/list 返回值
    src = exported[0] if isinstance(exported, (list, tuple)) else exported
    if not src or not os.path.exists(src):
        log("  导出产物未找到")
        return False

    if os.path.abspath(src) != os.path.abspath(TARGET_ONNX):
        if os.path.exists(TARGET_ONNX):
            os.remove(TARGET_ONNX)
        os.replace(src, TARGET_ONNX)
    log("  导出成功并就位: %s" % TARGET_ONNX)

    # .pt 中间产物不再需要（重新导出时本脚本会重新下载），删除以省空间
    try:
        if os.path.exists(pt_path) and "--keep-pt" not in sys.argv:
            os.remove(pt_path)
            log("  已清理中间权重: %s" % pt_path)
    except OSError:
        pass
    return True


def main():
    if not os.path.isdir(TARGET_DIR):
        os.makedirs(TARGET_DIR)

    if _PROXY:
        log("使用系统代理: %s" % _PROXY)
    else:
        log("未检测到系统代理，直连尝试")

    # 已存在且大小合理则直接复用（--force 可重下）
    if os.path.exists(TARGET_ONNX) and os.path.getsize(TARGET_ONNX) >= MIN_ONNX_BYTES and "--force" not in sys.argv:
        log("已存在有效模型: %s（--force 可强制重新下载）" % TARGET_ONNX)
        validate_onnx(TARGET_ONNX)
        return 0

    log("目标: %s" % TARGET_ONNX)

    # 第 1 级：ONNX 直链候选
    for url in ONNX_URLS:
        log("尝试 ONNX 直链: %s" % url)
        if download(url, TARGET_ONNX, MIN_ONNX_BYTES):
            if validate_onnx(TARGET_ONNX):
                log("完成! 姿态模型已就位")
                return 0
            log("  校验不通过，继续下一个候选")

    # 第 2 级：官方 .pt 直链 + 本地导出（正源路线）
    log("ONNX 直链全部不可用，转官方权重导出路线")
    need_pt = not (os.path.exists(TARGET_PT) and os.path.getsize(TARGET_PT) >= MIN_ONNX_BYTES)
    if need_pt and not download(PT_URL, TARGET_PT, MIN_ONNX_BYTES):
        log("全部路线失败：请检查网络/VPN，或手动下载 %s 放到 %s 后加 --export 重试" % (PT_URL, TARGET_PT))
        return 1

    if export_from_pt(TARGET_PT):
        validate_onnx(TARGET_ONNX)
        log("完成! 姿态模型已就位")
        return 0

    return 1


if __name__ == "__main__":
    sys.exit(main())
