# Build & Run (Windows)

This project is designed to run on **Windows 11 x64**.

## Requirements
- .NET SDK 10.x (or newer)

## Build
From repo root:

```powershell
cd src/TbxExecutor
# restore & build
 dotnet build -c Release
```

## Run

```powershell
cd src/TbxExecutor
 dotnet run
```

The app starts in the **system tray** and hosts an HTTP API bound to the tailnet interface (configurable).

## Config

On first run, a config file is created under:

- `%APPDATA%\TbxExecutor\config.json`

It contains:
- `listenPort` (default 17890)
- `allowlistIps` (default `["100.64.0.1"]`)
- `token` (generated randomly on first run)

You must provide this token to the controller when calling the API:

```http
Authorization: Bearer <token>
```

## Safety
- Input endpoints are refused when the session is locked.
- UAC / Secure Desktop is not supported (requests should fail fast).
