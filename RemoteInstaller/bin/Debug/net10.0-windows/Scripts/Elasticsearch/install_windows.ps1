# =================================================================
# Elasticsearch Windows 安装脚本
# =================================================================

param(
    [string]$PackagePath = "",
    [string]$InstallPath = "C:\Program Files\Elasticsearch",
    [int]$HttpPort = 0,
    [string]$ClusterName = "",
    [string]$NodeName = "",
    [string]$MemoryLimit = ""
)

$OutputEncoding = [System.Text.Encoding]::UTF8
Set-StrictMode -Version Latest

# 如果脚本参数为空，使用环境变量（兼容 PowerShell 5.1）
if ($HttpPort -eq 0) {
    if ($env:HTTP_PORT) { $HttpPort = [int]$env:HTTP_PORT } else { $HttpPort = 9200 }
}
if ([string]::IsNullOrEmpty($ClusterName)) {
    if ($env:CLUSTER_NAME) { $ClusterName = $env:CLUSTER_NAME } else { $ClusterName = "my-cluster" }
}
if ([string]::IsNullOrEmpty($NodeName)) {
    if ($env:NODE_NAME) { $NodeName = $env:NODE_NAME } else { $NodeName = "node-1" }
}
if ([string]::IsNullOrEmpty($MemoryLimit)) {
    if ($env:MEMORY_LIMIT) { $MemoryLimit = $env:MEMORY_LIMIT } else { $MemoryLimit = "2g" }
}

# 颜色输出函数
function Write-Color {
    param([string]$Text, [string]$Color)
    Write-Host $Text -ForegroundColor $Color
}

function Write-ProgressInfo {
    param([string]$Stage, [int]$Percent)
    Write-Host "PROGRESS:$Stage`:$Percent"
}

# 初始化
Write-ProgressInfo "Initializing" 5
Write-Color "========================================" "Cyan"
Write-Color "      Elasticsearch Windows 安装脚本" "Cyan"
Write-Color "========================================" "Cyan"
Write-Color "安装路径: $InstallPath" "Yellow"
Write-Color "HTTP 端口: $HttpPort" "Yellow"
Write-Color "集群名称: $ClusterName" "Yellow"
Write-Color "节点名称: $NodeName" "Yellow"
Write-Color "内存限制: $MemoryLimit" "Yellow"

# 1. 检查管理员权限
Write-ProgressInfo "CheckingPermissions" 10
Write-Color "`n1. 检查管理员权限:" "Yellow"
$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Color "错误：请以管理员身份运行此脚本" "Red"
    exit 1
}
Write-Color "管理员权限：已获取" "Green"

# 2. 检查 Java 环境
Write-ProgressInfo "CheckingJava" 15
Write-Color "`n2. 检查 Java 环境:" "Yellow"
$javaPath = Get-Command java -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -ErrorAction SilentlyContinue
if ($javaPath) {
    Write-Color "Java 路径: $javaPath" "Green"
    $javaVersion = java -version 2>&1 | Out-String
    Write-Color $javaVersion "Gray"
} else {
    Write-Color "警告：未找到 Java，请确保已安装 Java 17 或更高版本" "Yellow"
}

# 3. 创建安装目录
Write-ProgressInfo "CreatingDirectories" 20
Write-Color "`n3. 创建安装目录:" "Yellow"
if (Test-Path $InstallPath) {
    Write-Color "安装目录已存在，清空..." "Yellow"
    Remove-Item -Path $InstallPath -Recurse -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 2
}
New-Item -ItemType Directory -Path $InstallPath -Force | Out-Null
Write-Color "创建目录: $InstallPath" "Green"

# 4. 解压安装包
Write-ProgressInfo "ExtractingPackage" 30
Write-Color "`n4. 解压安装包:" "Yellow"

if ([string]::IsNullOrEmpty($PackagePath) -or -not (Test-Path $PackagePath)) {
    Write-Color "警告：未提供安装包路径，假设已解压到当前目录" "Yellow"
    $ExtractPath = Get-Location
} else {
    $Extension = [System.IO.Path]::GetExtension($PackagePath).ToLower()
    Write-Color "正在解压: $PackagePath" "Yellow"

    if ($Extension -eq ".zip") {
        Write-Color "使用 Expand-Archive 解压 ZIP..." "Gray"
        Expand-Archive -Path $PackagePath -DestinationPath $InstallPath -Force -ErrorAction Stop
        $ExtractPath = $InstallPath

        # 如果解压后产生单个子目录，提升其内容到安装目录
        $firstLevelDirs = @(Get-ChildItem $InstallPath -Directory -ErrorAction SilentlyContinue)
        if ($firstLevelDirs.Count -eq 1) {
            $subDir = $firstLevelDirs[0].FullName
            $items = Get-ChildItem $subDir -ErrorAction SilentlyContinue
            foreach ($item in $items) {
                Move-Item -Path $item.FullName -Destination "$InstallPath\$($item.Name)" -Force -ErrorAction SilentlyContinue
            }
            Remove-Item -Path $subDir -Force -Recurse -ErrorAction SilentlyContinue
        }
    }
    elseif ($Extension -eq ".tar.gz" -or $Extension -eq ".tgz") {
        # Windows 原生不支持 tar.gz，尝试使用 tar
        $tarExe = Get-Command tar -ErrorAction SilentlyContinue
        if ($tarExe) {
            Write-Color "使用 tar 解压..." "Gray"
            tar -xzf $PackagePath -C $InstallPath
            $ExtractPath = $InstallPath
        } else {
            Write-Color "警告：tar 命令不可用，尝试使用 .NET 解压..." "Yellow"
            # 备选：使用 .NET GZipStream
            try {
                Add-Type -AssemblyName System.IO.Compression.FileSystem
                $zip = [System.IO.Compression.ZipFile]::OpenRead($PackagePath)
                $zip.Entries | ForEach-Object {
                    $destPath = Join-Path $InstallPath $_.FullName
                    $destDir = Split-Path $destPath -Parent
                    if (-not (Test-Path $destDir)) {
                        New-Item -ItemType Directory -Path $destDir -Force | Out-Null
                    }
                    if ($_.Name -ne "") {
                        [System.IO.Compression.ZipFileExtensions]::ExtractToFile($_, $destPath, $true)
                    }
                }
                $zip.Dispose()
                $ExtractPath = $InstallPath
            } catch {
                Write-Color "解压失败: $_" "Red"
            }
        }
    }
    else {
        Write-Color "警告：不支持的格式 $Extension，假设已是解压目录" "Yellow"
        $ExtractPath = $PackagePath
    }
    Write-Color "解压完成" "Green"
}

# 5. 配置 elasticsearch.yml
Write-ProgressInfo "ConfiguringElasticsearch" 50
Write-Color "`n5. 配置 elasticsearch.yml:" "Yellow"

$configFile = Join-Path $InstallPath "config\elasticsearch.yml"
if (Test-Path $configFile) {
    # 备份原配置
    Copy-Item $configFile "$configFile.bak" -Force -ErrorAction SilentlyContinue

    $lines = Get-Content $configFile -ErrorAction SilentlyContinue | Where-Object { $_ -notmatch "^\s*#" }

    # 定义要更新的配置
    $configs = @{
        "cluster.name"      = $ClusterName
        "node.name"          = $NodeName
        "http.port"          = $HttpPort
        "network.host"       = "0.0.0.0"
        "discovery.type"     = "single-node"
        "xpack.security.enabled" = "false"
    }

    # 移除现有配置（未注释的）
    $newLines = @()
    $skipNext = @()
    foreach ($line in $lines) {
        $skip = $false
        foreach ($key in $configs.Keys) {
            if ($line -match "^\s*$key\s*:") {
                $skip = $true
                break
            }
        }
        if (-not $skip) {
            $newLines += $line
        }
    }

    # 追加新配置
    foreach ($key in $configs.Keys) {
        $newLines += "$key`: $($configs[$key])"
    }

    Set-Content -Path $configFile -Value $newLines -NoNewline -ErrorAction SilentlyContinue
    Write-Color "配置文件已更新" "Green"
} else {
    Write-Color "警告：未找到 elasticsearch.yml，跳过配置" "Yellow"
}

# 6. 配置 JVM 内存
Write-ProgressInfo "ConfiguringJVM" 60
Write-Color "`n6. 配置 JVM 内存 ($MemoryLimit):" "Yellow"

$jvmOptionsFile = Join-Path $InstallPath "config\jvm.options"
if (Test-Path $jvmOptionsFile) {
    # 备份
    Copy-Item $jvmOptionsFile "$jvmOptionsFile.bak" -Force -ErrorAction SilentlyContinue

    $jvmLines = Get-Content $jvmOptionsFile -ErrorAction SilentlyContinue

    # 替换 -Xms 和 -Xmx 行
    $newJvmLines = @()
    $xmsReplaced = $false
    $xmxReplaced = $false

    foreach ($line in $jvmLines) {
        if ($line -match "^-Xms") {
            $newJvmLines += "-Xms$MemoryLimit"
            $xmsReplaced = $true
        } elseif ($line -match "^-Xmx") {
            $newJvmLines += "-Xmx$MemoryLimit"
            $xmxReplaced = $true
        } else {
            $newJvmLines += $line
        }
    }

    # 如果没有找到，追加
    if (-not $xmsReplaced) {
        $newJvmLines += "-Xms$MemoryLimit"
    }
    if (-not $xmxReplaced) {
        $newJvmLines += "-Xmx$MemoryLimit"
    }

    Set-Content -Path $jvmOptionsFile -Value $newJvmLines -NoNewline -ErrorAction SilentlyContinue
    Write-Color "JVM 内存配置已更新" "Green"
} else {
    Write-Color "警告：未找到 jvm.options" "Yellow"
}

# 7. 配置防火墙
Write-ProgressInfo "ConfiguringFirewall" 70
Write-Color "`n7. 配置防火墙:" "Yellow"

$transportPort = $HttpPort + 100

# HTTP 端口规则
$httpRuleName = "Elasticsearch HTTP $HttpPort"
if (-not (Get-NetFirewallRule -DisplayName $httpRuleName -ErrorAction SilentlyContinue)) {
    New-NetFirewallRule -DisplayName $httpRuleName -Direction Inbound -LocalPort $HttpPort -Protocol TCP -Action Allow -Profile Any -ErrorAction SilentlyContinue | Out-Null
    Write-Color "已开放 HTTP 端口: $HttpPort" "Green"
}

# Transport 端口规则
$transportRuleName = "Elasticsearch Transport $transportPort"
if (-not (Get-NetFirewallRule -DisplayName $transportRuleName -ErrorAction SilentlyContinue)) {
    New-NetFirewallRule -DisplayName $transportRuleName -Direction Inbound -LocalPort $transportPort -Protocol TCP -Action Allow -Profile Any -ErrorAction SilentlyContinue | Out-Null
    Write-Color "已开放 Transport 端口: $transportPort" "Green"
}

# 8. 注册为 Windows 服务
Write-ProgressInfo "InstallingService" 80
Write-Color "`n8. 注册 Windows 服务:" "Yellow"

$esServiceBat = Join-Path $InstallPath "bin\elasticsearch-service.bat"
if (Test-Path $esServiceBat) {
    Write-Color "使用 elasticsearch-service.bat 注册服务..." "Yellow"
    & $esServiceBat install 2>$null | Out-Null
    Start-Sleep -Seconds 3

    $esService = Get-Service -Name "Elasticsearch*" -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($esService) {
        Write-Color "Elasticsearch 服务已注册" "Green"
    } else {
        Write-Color "警告：服务注册可能失败，尝试直接启动" "Yellow"
    }
} else {
    Write-Color "警告：未找到 elasticsearch-service.bat，跳过服务注册" "Yellow"
}

# 9. 启动服务
Write-ProgressInfo "StartingService" 85
Write-Color "`n9. 启动 Elasticsearch:" "Yellow"

try {
    $esService = Get-Service -Name "Elasticsearch*" -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($esService) {
        Start-Service -Name $esService.Name -ErrorAction Stop
        Write-Color "Elasticsearch 服务已启动" "Green"
    } else {
        throw "服务未注册"
    }
} catch {
    Write-Color "服务未注册，尝试直接运行..." "Yellow"
    $esBat = Join-Path $InstallPath "bin\elasticsearch.bat"
    if (Test-Path $esBat) {
        Start-Process -FilePath $esBat -WorkingDirectory $InstallPath -WindowStyle Hidden -PassThru -ErrorAction SilentlyContinue | Out-Null
        Write-Color "Elasticsearch 已启动 (后台进程)" "Green"
    }
}

# 10. 等待启动并验证
Write-ProgressInfo "VerifyingService" 95
Write-Color "`n10. 验证服务状态:" "Yellow"

$success = $false
for ($i = 1; $i -le 30; $i++) {
    try {
        $response = Invoke-WebRequest -Uri "http://localhost`:$HttpPort" -TimeoutSec 3 -UseBasicParsing -ErrorAction SilentlyContinue
        if ($response -and $response.StatusCode -eq 200) {
            Write-Color "Elasticsearch 服务已成功启动" "Green"
            $success = $true
            break
        }
    } catch {
        # 忽略，继续等待
    }
    Write-Color "等待服务启动 (尝试 $i/30)..." "Gray"
    Start-Sleep -Seconds 3
}

if (-not $success) {
    Write-Color "警告：Elasticsearch 在 90 秒内未能在端口 $HttpPort 启动" "Yellow"
    $logPath = Join-Path $InstallPath "logs"
    if (Test-Path $logPath) {
        Write-Color "请检查日志: $logPath" "Yellow"
    }
}

# 完成
Write-ProgressInfo "Complete" 100
Write-Color "`n========================================" "Cyan"
Write-Color "      Elasticsearch 安装完成！" "Green"
Write-Color "========================================" "Cyan"
Write-Color "安装路径: $InstallPath" "Yellow"
Write-Color "HTTP 端口: $HttpPort" "Yellow"
Write-Color "访问地址: http://localhost`:$HttpPort" "Yellow"
Write-Color "`nSTAGE`:SUCCESS" "Green"
