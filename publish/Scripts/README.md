# Scripts Directory

This directory contains installation and uninstallation scripts for different applications.

## Structure

```
Scripts/
├── MySQL/
│   ├── install_linux.sh
│   ├── install_windows.ps1
│   ├── uninstall_linux.sh
│   ├── uninstall_windows.ps1
│   ├── check_linux.sh
│   └── check_windows.ps1
├── Redis/
│   └── ...
├── Elasticsearch/
│   └── ...
├── RabbitMQ/
│   └── ...
├── Nacos/
│   └── ...
└── Nginx/
    └── ...
```

## Script Format

### Install Script Output

Scripts should output progress in the following format:
```
PROGRESS:StageName:Percentage
```

Example:
```
PROGRESS:Extracting:30
PROGRESS:Configuring:60
PROGRESS:Installing:90
```

### Check Script Output

Check scripts should output:
```
INSTALLED:true|false
VERSION:x.y.z
RUNNING:active|inactive
PORT:port_number
```

## TODO

Add actual installation scripts for each application.
