# RasGate as a Windows service

1. Extract the archive to its permanent directory, for example
   C:\Program Files\RasGate.
2. Set RasGate.ApiKey and Rac.ExecutablePath in appsettings.json.
3. Open Windows PowerShell as Administrator in that directory.
4. Run:

       .\install-service.ps1

Useful commands:

    Get-Service -Name RasGate
    Restart-Service -Name RasGate
    Invoke-RestMethod http://127.0.0.1:5050/rasgate/status

To remove only the service registration while preserving files and logs:

    .\uninstall-service.ps1

The installer does not open firewall ports. Keep the default localhost binding
unless remote access has been secured separately.
