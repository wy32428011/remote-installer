# =================================================================
# Elasticsearch 状态检测脚本 (Windows)
# =================================================================

$OutputEncoding = [System.Text.Encoding]::UTF8
Set-StrictMode -Version Latest

# 参数
param(
    [int]$HttpPort = 9200
)

# 颜色模拟
function Write-Color {
    param([string]$Text, [string]$Color)
    Write-Host $Text -ForegroundColor $Color
}

function Test-Port {
    param([int]$Port)
    $listener = Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1
    return ($null -ne $listener)
}

Write-Color "========================================" "Cyan"
Write-Color "      Elasticsearch 状态检测" "Cyan"
Write-Color "========================================" "Cyan"

$isInstalled = $false
$isRunning = $false
$version = "未知"
$esHome = ""

# 1. 检查安装情况
Write-Color "`n1. 检查安装情况:" "Yellow"

# 检查常见安装路径
$possiblePaths = @(
    "C:\Program Files\Elasticsearch\bin\elasticsearch.exe",
    "C:\Program Files\Elasticsearch\elasticsearch.exe",
    "C:\elasticsearch\bin\elasticsearch.exe",
    "C:\elasticsearch\elasticsearch.exe"
)

foreach ($path in $possiblePaths) {
    if (Test-Path $path) {
        $isInstalled = $true
        $esHome = Split-Path (Split-Path $path)
        Write-Color "Elasticsearch 已安装：是" "Green"
        Write-Color "安装路径: $esHome" "Gray"
        break
    }
}

# 检查服务
if (-not $isInstalled) {
    $esService = Get-Service -Name "Elasticsearch*" -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($esService) {
        $isInstalled = $true
        Write-Color "Elasticsearch 已安装：是 (服务)" "Green"
    }
}

# 检查环境变量
if (-not $isInstalled) {
    $esEnvPath = [Environment]::GetEnvironmentVariable("ES_HOME", "Machine")
    if ($esEnvPath -and (Test-Path "$esEnvPath\bin\elasticsearch.bat" -or Test-Path "$esEnvPath\bin\elasticsearch.exe")) {
        $isInstalled = $true
        $esHome = $esEnvPath
        Write-Color "Elasticsearch 已安装：是 (ES_HOME)" "Green"
        Write-Color "安装路径: $esHome" "Gray"
    }
}

if (-not $isInstalled) {
    Write-Color "Elasticsearch 已安装：否" "Red"
}

# 2. 检查进程
Write-Color "`n2. 检查运行进程:" "Yellow"
$esProcs = Get-CimInstance Win32_Process -Filter "name = 'java.exe'" -ErrorAction SilentlyContinue | Where-Object {
    $_.CommandLine -like "*elasticsearch*" -or $_.CommandLine -like "*org.elasticsearch.bootstrap.Elasticsearch*"
}
if ($esProcs) {
    $isRunning = $true
    Write-Color "Elasticsearch 运行状态：运行中" "Green"
    $esProcs | Select-Object ProcessId, CommandLine | Format-Table -AutoSize -Wrap
} else {
    Write-Color "Elasticsearch 运行状态：未运行" "Red"
}

# 3. 检查端口监听
Write-Color "`n3. 检查端口监听 ($HttpPort):" "Yellow"
if (Test-Port $HttpPort) {
    $isRunning = $true
    Write-Color "端口 $HttpPort 监听：是" "Green"
    Get-NetTCPConnection -LocalPort $HttpPort -State Listen -ErrorAction SilentlyContinue |
        Select-Object LocalAddress, LocalPort, OwningProcess |
        Format-Table -AutoSize
} else {
    Write-Color "端口 $HttpPort 监听：否" "Red"
}

# 4. API 测试
Write-Color "`n4. API 连接测试:" "Yellow"
try {
    $response = Invoke-WebRequest -Uri "http://localhost`:$HttpPort" -TimeoutSec 5 -UseBasicParsing -ErrorAction SilentlyContinue
    if ($response -and $response.StatusCode -eq 200) {
        Write-Color "Elasticsearch API：响应正常" "Green"
        $isRunning = $true

        # 从响应中提取版本
        $content = $response.Content
        if ($content -match '"number"\s*:\s*"([0-9]+\.[0-9]+\.[0-9]+)"') {
            $version = $matches[1]
            Write-Color "API 版本: $version" "Green"
        }

        # 提取集群名称
        if ($content -match '"cluster_name"\s*:\s*"([^"]+)"') {
            $clusterName = $matches[1]
            Write-Color "集群名称: $clusterName" "Blue"
        }
    } else {
        Write-Color "Elasticsearch API：无响应 (状态码: $($response.StatusCode))" "Red"
    }
} catch {
    Write-Color "Elasticsearch API：无法连接 ($_)" "Red"
}

# 5. 服务状态
Write-Color "`n5. Windows 服务状态:" "Yellow"
$esServices = Get-Service -Name "Elasticsearch*" -ErrorAction SilentlyContinue
if ($esServices) {
    foreach ($svc in $esServices) {
        if ($svc.Status -eq 'Running') {
            Write-Color "服务 $($svc.Name)：运行中" "Green"
            $isRunning = $true
        } else {
            Write-Color "服务 $($svc.Name)：$($svc.Status)" "Yellow"
        }
    }
} else {
    Write-Color "未找到 Elasticsearch 服务" "Gray"
}

# 输出机器可读状态
Write-Host ""
Write-Host "--- MACHINE READABLE ---"
Write-Host "INSTALLED: $isInstalled"
Write-Host "VERSION: $version"
Write-Host "RUNNING: $isRunning"
Write-Host "PORT: $HttpPort"
Write-Host "------------------------"

# 最终状态摘要
Write-Host ""
if ($isInstalled) {
    if ($isRunning) {
        Write-Color "最终状态：已安装且运行中 (v$version)" "Green"
    } else {
        Write-Color "最终状态：已安装但未运行" "Yellow"
    }
} else {
    Write-Color "最终状态：未安装" "Red"
}
