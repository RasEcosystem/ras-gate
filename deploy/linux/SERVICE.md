# RasGate as a systemd service

1. Extract the archive to a temporary directory.
2. Set RasGate.ApiKey and Rac.ExecutablePath in appsettings.json.
3. Run:

       sudo ./install-service.sh

The installer copies RasGate to /opt/rasgate, creates an unprivileged rasgate
user, installs rasgate.service, and starts it.

Useful commands:

    systemctl status rasgate.service
    sudo systemctl restart rasgate.service
    journalctl -u rasgate.service -f
    curl http://127.0.0.1:5050/rasgate/status

To remove only the service registration while preserving files and logs:

    sudo /opt/rasgate/uninstall-service.sh

The installer does not install RAC or open firewall ports. Keep the default
localhost binding unless remote access has been secured separately.
