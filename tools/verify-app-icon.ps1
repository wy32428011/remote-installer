[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$errors = [System.Collections.Generic.List[string]]::new()

function Add-ValidationError {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Message
    )

    $script:errors.Add($Message)
}

function Test-FileExists {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [string]$Description
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        Add-ValidationError ("Missing {0}: {1}" -f $Description, $Path)
        return $false
    }

    return $true
}

function Test-IcoFile {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    if (-not (Test-FileExists -Path $Path -Description 'ICO icon file')) {
        return
    }

    try {
        $bytes = [System.IO.File]::ReadAllBytes($Path)

        if ($bytes.Length -lt 6) {
            Add-ValidationError ("ICO header is too short: {0}" -f $Path)
            return
        }

        $reserved = [System.BitConverter]::ToUInt16($bytes, 0)
        $type = [System.BitConverter]::ToUInt16($bytes, 2)
        $count = [System.BitConverter]::ToUInt16($bytes, 4)

        if ($reserved -ne 0 -or $type -ne 1) {
            Add-ValidationError ("ICO header is invalid. Expected reserved=0 and type=1, actual reserved={0}, type={1}: {2}" -f $reserved, $type, $Path)
            return
        }

        if ($count -ne 7) {
            Add-ValidationError ("ICO frame count mismatch. Expected 7, actual {0}: {1}" -f $count, $Path)
        }

        $expectedSizes = @(16, 24, 32, 48, 64, 128, 256)
        $actualSizes = [System.Collections.Generic.List[int]]::new()
        $entrySize = 16
        $directorySize = 6 + ($count * $entrySize)

        if ($bytes.Length -lt $directorySize) {
            Add-ValidationError ("ICO directory entries are incomplete: {0}" -f $Path)
            return
        }

        for ($index = 0; $index -lt $count; $index++) {
            $offset = 6 + ($index * $entrySize)
            $width = if ($bytes[$offset] -eq 0) { 256 } else { [int]$bytes[$offset] }
            $height = if ($bytes[$offset + 1] -eq 0) { 256 } else { [int]$bytes[$offset + 1] }
            $bytesInRes = [System.BitConverter]::ToUInt32($bytes, $offset + 8)
            $imageOffset = [System.BitConverter]::ToUInt32($bytes, $offset + 12)
            $frameNumber = $index + 1

            $actualSizes.Add($width)

            if ($width -ne $height) {
                Add-ValidationError ("ICO frame {0} is not square. Actual {1}x{2}: {3}" -f $frameNumber, $width, $height, $Path)
            }

            if ($bytesInRes -le 0) {
                Add-ValidationError ("ICO frame {0} has invalid BytesInRes={1}. Expected a value greater than 0: {2}" -f $frameNumber, $bytesInRes, $Path)
            }

            if ($imageOffset -ge $bytes.Length) {
                Add-ValidationError ("ICO frame {0} has ImageOffset={1} outside file length {2}: {3}" -f $frameNumber, $imageOffset, $bytes.Length, $Path)
                continue
            }

            if ($bytesInRes -gt ([uint32]($bytes.Length - $imageOffset))) {
                Add-ValidationError ("ICO frame {0} exceeds file bounds. ImageOffset={1}, BytesInRes={2}, file length={3}: {4}" -f $frameNumber, $imageOffset, $bytesInRes, $bytes.Length, $Path)
            }
        }

        $expectedJoined = ($expectedSizes | Sort-Object) -join '/'
        $actualJoined = ($actualSizes | Sort-Object) -join '/'

        if ($expectedJoined -ne $actualJoined) {
            Add-ValidationError ("ICO size set mismatch. Expected {0}, actual {1}: {2}" -f $expectedJoined, $actualJoined, $Path)
        }
    }
    catch {
        Add-ValidationError ("Failed to read ICO file: {0}. {1}" -f $Path, $_.Exception.Message)
    }
}

$scriptPath = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptPath

$icoPath = Join-Path $repoRoot 'RemoteInstaller/Assets/Brand/remoteinstaller-icon.ico'
$pngPath = Join-Path $repoRoot 'RemoteInstaller/Assets/Brand/remoteinstaller-icon-256.png'
$csprojPath = Join-Path $repoRoot 'RemoteInstaller/RemoteInstaller.csproj'
$issPath = Join-Path $repoRoot 'installer/installer.iss'

Test-IcoFile -Path $icoPath
Test-FileExists -Path $pngPath -Description 'PNG icon file' | Out-Null

if (Test-FileExists -Path $csprojPath -Description 'project file') {
    $csprojContent = [System.IO.File]::ReadAllText($csprojPath)
    if ($csprojContent -notmatch '<ApplicationIcon>Assets\\Brand\\remoteinstaller-icon\.ico</ApplicationIcon>') {
        Add-ValidationError ("Missing ApplicationIcon wiring in project file: {0}" -f $csprojPath)
    }
}

if (Test-FileExists -Path $issPath -Description 'installer script') {
    $issContent = [System.IO.File]::ReadAllText($issPath)
    if ($issContent -notmatch 'SetupIconFile=\{#MySetupIconFile\}') {
        Add-ValidationError ("Missing SetupIconFile={{#MySetupIconFile}} wiring in installer script: {0}" -f $issPath)
    }
}

if ($errors.Count -gt 0) {
    $originalErrorActionPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'

    foreach ($message in $errors) {
        Write-Error $message
    }

    $ErrorActionPreference = $originalErrorActionPreference
    exit 1
}

Write-Output 'RemoteInstaller 图标资源与接线校验通过。'
