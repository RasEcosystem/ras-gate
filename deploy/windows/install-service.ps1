#Requires -Version 5.1
#Requires -RunAsAdministrator

[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$serviceName = "RasGate"
$serviceDisplayName = "RasGate"
$serviceDescription = "HTTP gateway for secure execution of 1C:Enterprise RAC commands"
$serviceAccount = "NT AUTHORITY\LocalService"
$applicationDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
$executablePath = Join-Path $applicationDirectory "RasGate.Web.exe"
$configurationPath = Join-Path $applicationDirectory "appsettings.json"
$logsPath = Join-Path $applicationDirectory "logs"

function Invoke-ServiceController {
    param(
        [Parameter(Mandatory = $true)]
        [string[]] $Arguments
    )

    & "$env:SystemRoot\System32\sc.exe" @Arguments

    if ($LASTEXITCODE -ne 0) {
        throw "sc.exe failed with exit code $LASTEXITCODE."
    }
}

if (Get-Service -Name $serviceName -ErrorAction SilentlyContinue) {
    throw "The '$serviceName' service already exists. Uninstall it before installing this release."
}

if (-not (Test-Path -LiteralPath $executablePath -PathType Leaf)) {
    throw "RasGate executable was not found: $executablePath"
}

if (-not (Test-Path -LiteralPath $configurationPath -PathType Leaf)) {
    throw "RasGate configuration was not found: $configurationPath"
}

Write-Host "Validating RasGate configuration..."
& $executablePath "--validate-config"

if ($LASTEXITCODE -ne 0) {
    throw "RasGate configuration validation failed."
}

New-Item -ItemType Directory -Path $logsPath -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $logsPath "requests") -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $logsPath "errors") -Force | Out-Null

Write-Host "Applying service file permissions..."
$applicationAclArguments = @(
    $applicationDirectory,
    "/grant:r",
    "*S-1-5-19:(OI)(CI)(RX)"
)
& "$env:SystemRoot\System32\icacls.exe" @applicationAclArguments | Out-Null

if ($LASTEXITCODE -ne 0) {
    throw "Failed to grant the service account access to the application directory."
}

$logsAclArguments = @(
    $logsPath,
    "/grant:r",
    "*S-1-5-19:(OI)(CI)(M)"
)
& "$env:SystemRoot\System32\icacls.exe" @logsAclArguments | Out-Null

if ($LASTEXITCODE -ne 0) {
    throw "Failed to grant the service account access to the log directory."
}

$configurationAclArguments = @(
    $configurationPath,
    "/inheritance:r",
    "/grant:r",
    "*S-1-5-32-544:F",
    "*S-1-5-18:F",
    "*S-1-5-19:R"
)
& "$env:SystemRoot\System32\icacls.exe" @configurationAclArguments | Out-Null

if ($LASTEXITCODE -ne 0) {
    throw "Failed to protect appsettings.json."
}

$quotedExecutablePath = '"' + $executablePath + '"'
$registered = $false

try {
    Write-Host "Registering the $serviceName Windows service..."

    Invoke-ServiceController @(
        "create",
        $serviceName,
        "binPath=",
        $quotedExecutablePath,
        "start=",
        "delayed-auto",
        "obj=",
        $serviceAccount,
        "DisplayName=",
        $serviceDisplayName
    )

    $registered = $true

    Invoke-ServiceController @(
        "description",
        $serviceName,
        $serviceDescription
    )

    Invoke-ServiceController @(
        "failure",
        $serviceName,
        "reset=",
        "86400",
        "actions=",
        "restart/5000/restart/15000/restart/60000"
    )

    Invoke-ServiceController @(
        "failureflag",
        $serviceName,
        "1"
    )

    Start-Service -Name $serviceName

    $service = Get-Service -Name $serviceName
    $service.WaitForStatus(
        [System.ServiceProcess.ServiceControllerStatus]::Running,
        [TimeSpan]::FromSeconds(30))

    Write-Host ""
    Write-Host "RasGate is installed and running."
    Write-Host "Status:  Get-Service -Name $serviceName"
    Write-Host "Restart: Restart-Service -Name $serviceName"
    Write-Host "Health:  http://127.0.0.1:5050/rasgate/status"
    Write-Host "Logs:    $logsPath"
}
catch {
    if ($registered) {
        Write-Warning "The service was registered but installation did not complete."
        Write-Warning "After inspecting the error, roll back with: .\uninstall-service.ps1"
    }

    throw
}
