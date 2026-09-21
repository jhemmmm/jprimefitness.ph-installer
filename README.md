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
.\publish.ps1                  # -> dist\JPrimePanel.exe (single file, self-contained win-x64)
.\publish.ps1 -Version 1.2.3   # same, with the version stamped into the exe
```

## Release

```powershell
.
elease.ps1 -Version 1.2.3   # tags v1.2.3 and pushes it; GitHub Actions builds the exe and attaches it to the release
```

`.github/workflows/release.yml` runs on every `v*` tag: publishes win-x64, writes `JPrimePanel.exe.sha256`, and creates the
GitHub release with both files. `-Local` builds here and uploads with the `gh` CLI instead of waiting for CI. The exe version
(panel status bar, `config\panel.json` → `Versions.Panel`) is taken from the tag.

## Updates

The panel's **Updates** button lists the JPrime app, the biometric helper and the panel itself against
`releases/latest` of their GitHub repositories and installs any of them (the app can also be installed from a local
bundle zip). Each install stops only the affected services, keeps `.env`, `storagepp`, the database and the helper's
`appsettings.json`, and rolls back when migrations fail. A panel update swaps `panel\JPrimePanel.exe` (previous copy
kept as `.old`) and restarts the panel.

Settings → Updates: check GitHub once a day (default on; the tray shows a notice once per new version) and, optionally,
install everything automatically inside a nightly window while the web app has been idle for 10 minutes.

## Developer switches (hidden)

```
JPrimePanel.exe --install-root C:\JPrime            run the panel against a folder
JPrimePanel.exe --wizard [--install-root C:\JPrime] force the wizard (pre-filled when a panel.json exists)
JPrimePanel.exe --dev-install <root> [--payloads <dir>] [--with-hikvision <devicePwd>]
                                                     headless InstallEngine run from cached payloads
JPrimePanel.exe --dev-provision <root>               write configs + bootstrap DB for an already-extracted tree
JPrimePanel.exe --dev-smoke <root> [holdSeconds]     headless start/probe/kill/backup/stop checks
JPrimePanel.exe --dev-update <root> <bundle.zip|hikvision-*.zip|JPrimePanel.exe|check|install:app|install:helper>
                                                     apply a local bundle/helper zip/panel exe, run the GitHub check, or download+install one target
JPrimePanel.exe --dev-screenshots <dir> [--install-root <root>]   render every wizard page + panel to PNG (JPRIME_SHOT_SIZE=min|WxH)
JPrimePanel.exe --admin-task <name> [args]           elevated helper mode (used internally via UAC)
```

Install layout: `<root>\{panel,php,nginx,cloudflared,hikvision,app,data,logs,downloads,config}`.
Payloads dropped into `<root>\downloads` beforehand are reused (offline install).
