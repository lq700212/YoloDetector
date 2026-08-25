# ============================================================
# Invoke-SmokeTest.ps1 — GUI 进程级冒烟测试
#
# 验证内容：
#   1. 主程序 exe 能启动并存活（6 秒观察期）
#   2. 发送关闭消息后能在限定时间内正常退出（ExitCode=0）
#   3. 日志文件出现配对的"程序启动 / 程序退出"标记
#
# 用法（在仓库任意位置）：
#   powershell -ExecutionPolicy Bypass -File "<本文件路径>"
# 退出码：0=全部通过，1=存在失败项
# ============================================================

param(
    # 主程序 exe 路径；默认自动定位仓库根的 bin\Debug\net472\YoloDetector.exe
    [string]$ExePath = ""
)

$ErrorActionPreference = "Stop"

# ---- 定位 exe：未显式传参时按脚本位置向上五级=仓库根推导 ----
if ([string]::IsNullOrEmpty($ExePath)) {
    $repoRoot = $PSCommandPath
    for ($i = 0; $i -lt 5; $i++) { $repoRoot = Split-Path -Parent $repoRoot }
    $ExePath = Join-Path $repoRoot "bin\Debug\net472\YoloDetector.exe"
}

$failures = New-Object System.Collections.Generic.List[string]

function Assert-True([bool]$Condition, [string]$What) {
    if ($Condition) {
        Write-Host ("[SMOKE][PASS] " + $What)
    } else {
        Write-Host ("[SMOKE][FAIL] " + $What)
        $script:failures.Add($What)
    }
}

if (-not (Test-Path -LiteralPath $ExePath)) {
    Write-Host "[SMOKE][FAIL] 找不到主程序: $ExePath （请先构建主项目）"
    exit 1
}

# ---- 记录运行前的日志文件大小，用于只校验本次启动产生的日志段 ----
$logFile = Join-Path (Split-Path -Parent $ExePath) ("logs\log_" + (Get-Date -Format "yyyy-MM-dd") + ".txt")
$logOffset = 0
if (Test-Path -LiteralPath $logFile) {
    $logOffset = (Get-Item -LiteralPath $logFile).Length
}

# ---- 步骤1：启动并观察存活 ----
Write-Host "[SMOKE] 启动: $ExePath"
$proc = Start-Process -FilePath $ExePath -PassThru
Start-Sleep -Seconds 6

if ($proc.HasExited) {
    Assert-True $false ("进程启动即退出 ExitCode=" + $proc.ExitCode)
} else {
    Assert-True $true "进程存活超过 6 秒观察期"

    # ---- 步骤2：优雅关闭并等待退出 ----
    $null = $proc.CloseMainWindow()
    if ($proc.WaitForExit(8000)) {
        Assert-True ($proc.ExitCode -eq 0) ("关闭消息后正常退出 ExitCode=" + $proc.ExitCode)
    } else {
        # 关闭超时：强杀兜底（不留僵尸进程），并记为失败
        $proc.Kill()
        $proc.WaitForExit(3000) | Out-Null
        Assert-True $false "关闭消息后 8 秒内未退出（已强杀），排查 FormClosing 是否阻塞"
    }
}

# ---- 步骤3：日志配对校验（只看本次运行写入的段落）----
Start-Sleep -Milliseconds 500
if (Test-Path -LiteralPath $logFile) {
    $bytes = New-Object byte[] ((Get-Item -LiteralPath $logFile).Length - $logOffset)
    $fs = [IO.File]::OpenRead($logFile)
    try {
        $null = $fs.Seek($logOffset, "Begin")
        $null = $fs.Read($bytes, 0, $bytes.Length)
    } finally {
        $fs.Dispose()
    }
    $logText = [Text.Encoding]::UTF8.GetString($bytes)
    Assert-True ($logText.Contains("程序启动")) "日志含『程序启动』标记"
    Assert-True ($logText.Contains("程序退出")) "日志含『程序退出』标记（启动/退出配对）"
} else {
    Assert-True $false "本次运行未产生日志文件: $logFile"
}

# ---- 汇总 ----
Write-Host "------------------------------------------------------------"
if ($failures.Count -eq 0) {
    Write-Host "[SMOKE] 结果: 全部通过"
    exit 0
} else {
    Write-Host ("[SMOKE] 结果: 失败 " + $failures.Count + " 项")
    foreach ($f in $failures) { Write-Host ("  - " + $f) }
    exit 1
}
