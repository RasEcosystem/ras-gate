#Requires -Version 5.1
#Requires -RunAsAdministrator

[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$serviceName = "RasGate"
$service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue

if ($null -eq $service) {
    Write-Host "The '$serviceName' service is not installed."
    exit 0
}

if ($service.Status -ne [System.ServiceProcess.ServiceControllerStatus]::Stopped) {
    Write-Host "Stopping the $serviceName service..."
    Stop-Service -Name $serviceName

    $service = Get-Service -Name $serviceName
    $service.WaitForStatus(
        [System.ServiceProcess.ServiceControllerStatus]::Stopped,
        [TimeSpan]::FromSeconds(90))
}

Write-Host "Removing the $serviceName service registration..."
& "$env:SystemRoot\System32\sc.exe" delete $serviceName

if ($LASTEXITCODE -ne 0) {
    throw "sc.exe failed to delete the service (exit code $LASTEXITCODE)."
}

Write-Host ""
Write-Host "RasGate service registration was removed."
Write-Host "Application files, configuration, and logs were preserved."
