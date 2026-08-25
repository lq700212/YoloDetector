# ============================================================
# Run-AllTests.ps1 — 全量回归验证总入口（一键执行）
#
# 流程：构建主项目 → 构建+运行 harness(58 用例) → GUI 冒烟 → 汇总
# 任何一步失败立即以非 0 退出码结束，适合交付前/提交前一键验证。
#
# 用法：
#   powershell -ExecutionPolicy Bypass -File "<本文件路径>"
# 退出码：0=全绿；1=构建失败或用例失败；2=冒烟失败
# ============================================================

$ErrorActionPreference = "Stop"
$scriptsDir = Split-Path -Parent $PSCommandPath

Write-Host "============================================================"
Write-Host " YoloDetector 全量回归验证"
Write-Host "============================================================"

# ---- 阶段1：进程内回归 harness（含构建）----
Write-Host ""
Write-Host ">>> 阶段 1/2: 进程内回归测试"
powershell -ExecutionPolicy Bypass -File (Join-Path $scriptsDir "Invoke-Harness.ps1")
if ($LASTEXITCODE -ne 0) {
    Write-Host "[ALL][FAIL] 进程内回归未通过，终止后续阶段"
    exit 1
}

# ---- 阶段2：GUI 进程级冒烟 ----
Write-Host ""
Write-Host ">>> 阶段 2/2: GUI 冒烟测试"
powershell -ExecutionPolicy Bypass -File (Join-Path $scriptsDir "Invoke-SmokeTest.ps1")
if ($LASTEXITCODE -ne 0) {
    Write-Host "[ALL][FAIL] GUI 冒烟未通过"
    exit 2
}

# ---- 汇总 ----
Write-Host ""
Write-Host "============================================================"
Write-Host " [ALL][PASS] 构建通过 + 回归用例全绿 + GUI 冒烟通过"
Write-Host "============================================================"
exit 0
