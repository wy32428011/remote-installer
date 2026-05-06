$OutputEncoding = [System.Text.Encoding]::UTF8
Set-StrictMode -Version Latest

param(
    [string]$InstallPath = "C:\Program Files\JDK17"
)

function Write-ProgressInfo {
    param([string]$Stage, [int]$Percent)
    Write-Host "PROGRESS:$Stage`:$Percent"
}

Write-ProgressInfo "Initializing" 5
Write-Host "JDK 17 Windows 卸载脚本开始..."

$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Error "请以管理员身份运行此脚本"
    exit 1
}

Write-ProgressInfo "Cleaning" 45
if (Test-Path $InstallPath) {
    Remove-Item -Path $InstallPath -Recurse -Force -ErrorAction SilentlyContinue
}
[Environment]::SetEnvironmentVariable("JDK17_HOME", $null, "Machine")
$javaHome = [Environment]::GetEnvironmentVariable("JAVA_HOME", "Machine")
if ($javaHome -eq $InstallPath) {
    [Environment]::SetEnvironmentVariable("JAVA_HOME", $null, "Machine")
}
$machinePath = [Environment]::GetEnvironmentVariable("Path", "Machine")
$jdkBin = Join-Path $InstallPath "bin"
if ($machinePath -like "*$jdkBin*") {
    $newPath = ($machinePath -split ';' | Where-Object { $_ -ne $jdkBin }) -join ';'
    [Environment]::SetEnvironmentVariable("Path", $newPath, "Machine")
}

Write-ProgressInfo "Complete" 100
$installed = Test-Path $InstallPath
Write-Host "INSTALLED:$installed"
Write-Host "VERSION:removed"
Write-Host "RUNNING:inactive"