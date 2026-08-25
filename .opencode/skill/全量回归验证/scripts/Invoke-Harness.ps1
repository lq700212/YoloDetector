# ============================================================
# Invoke-Harness.ps1 — 构建并运行进程内回归测试 harness
#
# harness 覆盖：配置层 / Mat↔SKBitmap 无损互转 / 后处理器 /
#   可视化器 / YOLO 检测器(真实模型) / 检测管道线程协议 /
#   帧源生命周期 / 控制器端到端 / 相机控制器 / 日志门面 / UI 构造冒烟
# （用例明细见 SKILL.md 与 harness\*.cs）
#
# 用法：
#   powershell -ExecutionPolicy Bypass -File "<本文件路径>"
# 退出码：0=全部通过，非0=构建或用例失败
# ============================================================

$ErrorActionPreference = "Stop"

# ---- 定位仓库根：脚本位于 <根>\.opencode\skill\<技能名>\scripts\ 下，向上五级 ----
$repoRoot = $PSCommandPath
for ($i = 0; $i -lt 5; $i++) { $repoRoot = Split-Path -Parent $repoRoot }
$mainProj = Join-Path $repoRoot "YoloDetector.csproj"
$harnessProj = Join-Path $repoRoot ".opencode\skill\全量回归验证\harness\YoloDetector.Tests.csproj"
$harnessExe = Join-Path $repoRoot "bin\Debug\net472\YoloDetector.Tests.exe"

# ---- 步骤1：构建主项目（harness 引用其产物，必须先构建）----
Write-Host "[HARNESS] 构建主项目..."
dotnet build $mainProj -v q
if ($LASTEXITCODE -ne 0) { Write-Host "[HARNESS][FAIL] 主项目构建失败"; exit 1 }

# ---- 步骤2：构建 harness（0 警告标准与主项目一致）----
Write-Host "[HARNESS] 构建 harness..."
dotnet build $harnessProj -v q
if ($LASTEXITCODE -ne 0) { Write-Host "[HARNESS][FAIL] harness 构建失败"; exit 1 }

# ---- 步骤3：运行 harness（工作目录=主 bin，配置/模型就地可用）----
Write-Host "[HARNESS] 运行用例..."
$env:NO_COLOR = "1"
& $harnessExe
$code = $LASTEXITCODE

if ($code -eq 0) {
    Write-Host "[HARNESS][PASS] 全部用例通过"
} else {
    Write-Host ("[HARNESS][FAIL] 存在失败用例，退出码=" + $code)
}
exit $code
