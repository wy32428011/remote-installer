$OutputEncoding = [System.Text.Encoding]::UTF8
Set-StrictMode -Version Latest

param(
    [string]$InstallPath = "C:\Program Files\JDK11"
)

$javaExe = Join-Path $InstallPath "bin\java.exe"
$installed = $false
$version = "unknown"
if (Test-Path $javaExe) {
    $installed = $true
    $version = (& $javaExe -version 2>&1 | Select-Object -First 1)
} else {
    $javaPath = Get-Command java -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -ErrorAction SilentlyContinue
    if ($javaPath) {
        $versionLine = (& $javaPath -version 2>&1 | Select-Object -First 1)
        if ($versionLine -match '11\.') {
            $installed = $true
            $version = $versionLine
        }
    }
}
Write-Host "INSTALLED:$installed"
Write-Host "VERSION:$version"
Write-Host "RUNNING:inactive"
Write-Host "JAVA_HOME:$InstallPath"