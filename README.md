# JPrime Control Panel (installer + panel)

One Windows executable (`JPrimePanel.exe`, .NET 8 WinForms) that

* on first run walks through a **setup wizard**: downloads PHP 8.4 (NTS), nginx, the JPrime Fitness PH
  app bundle, optionally the Hikvision biometric helper and cloudflared; writes `php.ini`, `nginx.conf`,
  `.env`, `appsettings.json`; creates the SQLite database, runs migrations, seeds the super admin.
* afterwards runs as an **XAMPP-style tray panel**: start/stop/restart each service, autostart at login,
  health polling, log viewer, `.env` editor, artisan console, online SQLite backups, app updates from
  GitHub releases.

Services supervised (each in its own Windows job object, so nothing outlives the panel):

| Row | Process | Notes |
|---|---|---|
| Web server | `nginx.exe` | listens on `0.0.0.0:8001` (LAN/Wi-Fi reachable), FastCGI to the pool |
| PHP pool | `php-cgi.exe` x4 | ports 9001-9004, auto-respawn, `PHP_FCGI_MAX_REQUESTS` recycling |
| Scheduler | `php artisan schedule:work` | Laravel scheduler (expiry, notifications, sync:* when role=local) |
| Biometric helper | `HikVision.exe` | optional; port 5077; `DeviceNotFound` backoff when the terminal is offline |
| Cloudflare Tunnel | `cloudflared.exe` | optional; publishes the helper as `hikvision.jprimefitness.ph` |

## Build

```powershell
.\publish.ps1          # -> dist\JPrimePanel.exe (single file, self-contained win-x64)
```

## Developer switches (hidden)

```
JPrimePanel.exe --install-root C:\JPrime            run the panel against a folder
JPrimePanel.exe --wizard [--install-root C:\JPrime] force the wizard (pre-filled when a panel.json exists)
JPrimePanel.exe --dev-install <root> [--payloads <dir>] [--with-hikvision <devicePwd>]
                                                     headless InstallEngine run from cached payloads
JPrimePanel.exe --dev-provision <root>               write configs + bootstrap DB for an already-extracted tree
JPrimePanel.exe --dev-smoke <root> [holdSeconds]     headless start/probe/kill/backup/stop checks
JPrimePanel.exe --admin-task <name> [args]           elevated helper mode (used internally via UAC)
```

Install layout: `<root>\{panel,php,nginx,cloudflared,hikvision,app,data,logs,downloads,config}`.
Payloads dropped into `<root>\downloads` beforehand are reused (offline install).
