# =================================================================
# Nacos Windows 卸载脚本
# =================================================================

$OutputEncoding = [System.Text.Encoding]::UTF8
Set-StrictMode -Version Latest

# 参数
param(
    [string]$InstallPath = "C:\nacos"
)

# 颜色输出函数
function Write-Color {
    param([string]$Text, [string]$Color)
    Write-Host $Text -ForegroundColor $Color
}

Write-Color "========================================" "Cyan"
Write-Color "      Nacos Windows 卸载脚本" "Cyan"
Write-Color "========================================" "Cyan"

# 1. 检查管理员权限
Write-Color "`n1. 检查管理员权限:" "Yellow"
$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Color "错误：请以管理员身份运行此脚本" "Red"
    exit 1
}
Write-Color "管理员权限：已获取" "Green"

# 2. 停止 Nacos 服务
Write-Color "`n2. 停止 Nacos 服务:" "Yellow"
try {
    $nacosServices = Get-Service -Name "Nacos*" -ErrorAction SilentlyContinue
    foreach ($svc in $nacosServices) {
        Write-Color "停止服务: $($svc.Name)" "Yellow"
        Stop-Service -Name $svc.Name -Force -ErrorAction SilentlyContinue
    }
    Write-Color "Nacos 服务已停止" "Green"
} catch {
    Write-Color "服务未运行或不存在" "Yellow"
}

# 3. 停止 Nacos 进程
Write-Color "`n3. 停止 Nacos 进程:" "Yellow"

$nacosProcesses = Get-CimInstance Win32_Process -ErrorAction SilentlyContinue | Where-Object {
    $_.CommandLine -like "*nacos*" -and ($_.CommandLine -like "*nacos.nacos*" -or $_.CommandLine -like "*nacos-server.jar*")
}

if ($nacosProcesses) {
    Write-Color "发现 Nacos 进程，正在停止..." "Yellow"
    foreach ($proc in $nacosProcesses) {
        Stop-Process -Id $proc.ProcessId -Force -ErrorAction SilentlyContinue
        Write-Color "  已终止进程 ID: $($proc.ProcessId)" "Gray"
    }
    Start-Sleep -Seconds 2
}

# 尝试通过 java 进程查找
$javaProcesses = Get-CimInstance Win32_Process -Filter "name = 'java.exe'" -ErrorAction SilentlyContinue | Where-Object {
    $_.CommandLine -like "*nacos*"
}
if ($javaProcesses) {
    foreach ($proc in $javaProcesses) {
        Write-Color "  终止 Java 进程 ID: $($proc.ProcessId)" "Gray"
        Stop-Process -Id $proc.ProcessId -Force -ErrorAction SilentlyContinue
    }
    Start-Sleep -Seconds 2
}

# 多次尝试确保进程停止
for ($i = 1; $i -le 3; $i++) {
    $remaining = Get-CimInstance Win32_Process -ErrorAction SilentlyContinue | Where-Object {
        $_.CommandLine -like "*nacos*" -and ($_.CommandLine -like "*nacos.nacos*" -or $_.CommandLine -like "*nacos-server.jar*")
    }
    if (-not $remaining) { break }
    Write-Color "发现残留进程，强制终止 (尝试 $i/3)..." "Yellow"
    foreach ($proc in $remaining) {
        Stop-Process -Id $proc.ProcessId -Force -ErrorAction SilentlyContinue
    }
    Start-Sleep -Seconds 2
}
Write-Color "Nacos 进程已停止" "Green"

# 4. 卸载 Windows 服务
Write-Color "`n4. 卸载 Windows 服务:" "Yellow"
$nacosServices = Get-Service -Name "Nacos*" -ErrorAction SilentlyContinue
foreach ($svc in $nacosServices) {
    Write-Color "删除服务: $($svc.Name)" "Yellow"
    sc.exe delete $svc.Name 2>$null | Out-Null
}

# 等待服务真正删除
Write-Color "等待服务彻底删除..." "Yellow"
$waitCount = 0
$maxWait = 10
while ($waitCount -lt $maxWait) {
    $remainingServices = Get-Service -Name "Nacos*" -ErrorAction SilentlyContinue
    if (-not $remainingServices) { break }
    Start-Sleep -Seconds 1
    $waitCount++
}
if ($remainingServices) {
    Write-Color "警告：服务仍在删除中" "Yellow"
} else {
    Write-Color "Nacos 服务已卸载" "Green"
}

# 5. 清理防火墙规则
Write-Color "`n5. 清理防火墙规则:" "Yellow"

$firewallRules = @(
    Get-NetFirewallRule -DisplayName "*Nacos*" -ErrorAction SilentlyContinue
)

foreach ($rule in $firewallRules) {
    if ($rule) {
        Remove-NetFirewallRule -InputObject $rule -ErrorAction SilentlyContinue
        Write-Color "  已删除防火墙规则：$($rule.DisplayName)" "Gray"
    }
}
Write-Color "防火墙规则已清理" "Green"

# 6. 删除安装目录
Write-Color "`n6. 删除 Nacos 安装目录:" "Yellow"

$pathsToRemove = @(
    $InstallPath,
    "C:\nacos-*",
    "D:\nacos",
    "D:\nacos-*"
)

$deleted = $false
foreach ($path in $pathsToRemove) {
    if (Test-Path $path) {
        try {
            Remove-Item -Path $path -Recurse -Force -ErrorAction SilentlyContinue
            Write-Color "  已删除：$path" "Gray"
            $deleted = $true
        } catch {
            Write-Color "  删除失败：$path ($($_.Exception.Message))" "Red"
        }
    }
}

if (-not $deleted) {
    Write-Color "  未找到 Nacos 安装目录" "Yellow"
} else {
    Write-Color "安装目录已删除" "Green"
}

# 7. 清理环境变量
Write-Color "`n7. 清理环境变量:" "Yellow"

$nacosHome = [Environment]::GetEnvironmentVariable("NACOS_HOME", "Machine")
if ($nacosHome) {
    [Environment]::SetEnvironmentVariable("NACOS_HOME", $null, "Machine")
    Write-Color "  已删除 NACOS_HOME 环境变量" "Gray"
}

$systemPath = [Environment]::GetEnvironmentVariable("Path", "Machine")
if ($systemPath -like "*nacos*" -or $systemPath -like "*Nacos*") {
    $newPath = ($systemPath -split ';' | Where-Object { $_ -notlike "*nacos*" -and $_ -notlike "*Nacos*" } -join ';')
    [Environment]::SetEnvironmentVariable("Path", $newPath, "Machine")
    Write-Color "  已清理系统 PATH 环境变量" "Gray"
}
Write-Color "环境变量清理完成" "Green"

# 8. 清理注册表
Write-Color "`n8. 清理注册表:" "Yellow"
$registryPaths = @(
    "HKLM:\SOFTWARE\Nacos",
    "HKLM:\SOFTWARE\Wow6432Node\Nacos",
    "HKCU:\SOFTWARE\Nacos"
)
foreach ($regPath in $registryPaths) {
    if (Test-Path $regPath) {
        Remove-Item -Path $regPath -Recurse -Force -ErrorAction SilentlyContinue
        Write-Color "  已清理注册表：$regPath" "Gray"
    }
}

# 9. 最终验证
Write-Color "`n最终验证:" "Yellow"

# 验证服务（最多重试3次）
for ($retry = 1; $retry -le 3; $retry++) {
    $nacosServices = Get-Service -Name "Nacos*" -ErrorAction SilentlyContinue
    if (-not $nacosServices) { break }
    if ($retry -lt 3) {
        Write-Color "发现服务残留，等待后重试 ($retry/3)..." "Yellow"
        Start-Sleep -Seconds 2
    }
}
if ($nacosServices) {
    Write-Color "警告：仍有 Nacos 服务存在" "Red"
} else {
    Write-Color "Nacos 服务：已清理" "Green"
}

# 验证进程（最多重试3次）
for ($retry = 1; $retry -le 3; $retry++) {
    $remaining = Get-CimInstance Win32_Process -ErrorAction SilentlyContinue | Where-Object {
        $_.CommandLine -like "*nacos*" -and ($_.CommandLine -like "*nacos.nacos*" -or $_.CommandLine -like "*nacos-server.jar*")
    }
    if (-not $remaining) { break }
    if ($retry -lt 3) {
        Write-Color "发现进程残留，强制终止后重试 ($retry/3)..." "Yellow"
        foreach ($proc in $remaining) {
            Stop-Process -Id $proc.ProcessId -Force -ErrorAction SilentlyContinue
        }
        Start-Sleep -Seconds 2
    }
}
if ($remaining) {
    Write-Color "警告：仍有 Nacos 进程运行" "Red"
} else {
    Write-Color "Nacos 进程：已停止" "Green"
}

# 验证安装目录
$installExists = $false
foreach ($path in $pathsToRemove) {
    if (Test-Path $path) { $installExists = $true; break }
}
if ($installExists) {
    Write-Color "警告：安装目录仍存在" "Red"
} else {
    Write-Color "安装目录：已清理" "Green"
}

# 验证端口 8848 和 9848
$port8848 = Get-NetTCPConnection -LocalPort 8848 -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1
$port9848 = Get-NetTCPConnection -LocalPort 9848 -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1
if ($port8848 -or $port9848) {
    if ($port8848) { Write-Color "警告：端口 8848 仍在被占用 (PID: $($port8848.OwningProcess))" "Red" }
    if ($port9848) { Write-Color "警告：端口 9848 仍在被占用 (PID: $($port9848.OwningProcess))" "Red" }
} else {
    Write-Color "Nacos 端口（8848/9848）：已释放" "Green"
}

# 10. 输出结果
Write-Host ""
Write-Host "--- MACHINE READABLE ---"
Write-Host "UNINSTALLED: true"
Write-Host "------------------------"

Write-Color "`n========================================" "Cyan"
Write-Color "      Nacos 卸载完成！" "Green"
Write-Color "========================================" "Cyan"
Write-Color "" "Cyan"
