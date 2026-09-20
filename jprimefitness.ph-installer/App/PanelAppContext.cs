using System.Diagnostics;
using JPrime.Panel.UI;
using JPrime.Panel.UI.Panel;
using Microsoft.Win32;

namespace JPrime.Panel.App;

/// <summary>Owns the tray icon, the main window and the service manager for the lifetime of panel mode.</summary>
public sealed class PanelAppContext : ApplicationContext
{
    private readonly CommandLine _cli;
    private readonly SingleInstance _instance;
    private readonly AppServices _ctx;
    private readonly TrayIcon _tray;
    private MainForm? _main;
    private bool _exiting;
    private readonly System.Threading.Timer _backupTimer;
    private int _backupRunning;

    public PanelAppContext(CommandLine cli, SingleInstance instance)
    {
        _cli = cli;
        _instance = instance;
        _ctx = AppServices.Current;
        UiThread.Capture();

        _tray = new TrayIcon(this);
        _instance.ListenForShow(() => UiThread.Post(ShowMain));
        SystemEvents.SessionEnding += OnSessionEnding;

        var swept = _ctx.Services.SweepOrphans();
        if (swept > 0) Log.Warn($"Killed {swept} orphaned runtime process(es) from a previous session.");

        _ctx.Services.AnyStatusChanged += _ => UiThread.Post(() => _tray.RefreshState(_ctx.Services));
        _ctx.Services.StartPolling();
        _backupTimer = new System.Threading.Timer(_ => BackupTick(), null, TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(10));

        if (!cli.Autostart)
        {
            ShowMain();
        }

        if (cli.Autostart || cli.Show)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await _ctx.Services.StartAllAsync(onlyAutostart: true).ConfigureAwait(false);
                    if (cli.OpenBrowser)
                    {
                        // Give nginx a moment to bind before the browser hits it.
                        await Task.Delay(1500).ConfigureAwait(false);
                        OpenApp();
                    }
                }
                catch (Exception ex)
                {
                    Log.Error("Autostart failed", ex);
                }
            });
        }
    }

    public AppServices Ctx => _ctx;

    /// <summary>Runs the scheduled database backup when due (checked every 10 minutes).</summary>
    private void BackupTick()
    {
        if (_exiting) return;
        var backup = new Runtime.SqliteBackup(_ctx.Paths, _ctx.Config);
        if (!backup.IsDue() || !File.Exists(_ctx.Paths.SqliteDb)) return;
        if (Interlocked.Exchange(ref _backupRunning, 1) == 1) return;
        _ = Task.Run(async () =>
        {
            try
            {
                Log.Info("Scheduled backup starting");
                await backup.RunAsync(CancellationToken.None, Log.Info).ConfigureAwait(false);
                _ctx.SaveConfig();
            }
            catch (Exception ex)
            {
                Log.Error("Scheduled backup failed", ex);
            }
            finally
            {
                Interlocked.Exchange(ref _backupRunning, 0);
            }
        });
    }

    public void ShowMain()
    {
        if (_exiting) return;
        if (_main is null || _main.IsDisposed)
        {
            _main = new MainForm(this);
            _main.FormClosing += OnMainClosing;
        }
        if (!_main.Visible) _main.Show();
        if (_main.WindowState == FormWindowState.Minimized) _main.WindowState = FormWindowState.Normal;
        _main.Activate();
        _main.BringToFront();
    }

    private void OnMainClosing(object? sender, FormClosingEventArgs e)
    {
        if (_exiting) return;
        if (e.CloseReason == CloseReason.WindowsShutDown || e.CloseReason == CloseReason.TaskManagerClosing)
        {
            _ctx.Services.KillAllNow();
            return;
        }
        if (e.CloseReason == CloseReason.UserClosing && _ctx.Config.Ui.MinimizeToTray)
        {
            e.Cancel = true;
            _main!.Hide();
            _tray.ShowBalloonOnce("JPrime Control Panel keeps running here. Right-click for options.");
            return;
        }
        e.Cancel = true;
        _ = ExitAsync();
    }

    public async Task StartAllAsync()
    {
        try { await _ctx.Services.StartAllAsync().ConfigureAwait(false); }
        catch (Exception ex) { Log.Error("Start all failed", ex); }
    }

    public async Task StopAllAsync()
    {
        try { await _ctx.Services.StopAllAsync().ConfigureAwait(false); }
        catch (Exception ex) { Log.Error("Stop all failed", ex); }
    }

    public void OpenApp()
    {
        OpenUrl($"http://localhost:{_ctx.Config.Web.Port}/");
    }

    public static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not open {url}: {ex.Message}");
        }
    }

    /// <summary>Stops everything (after confirmation when services run) and exits the panel.</summary>
    public async Task ExitAsync()
    {
        if (_exiting) return;
        if (_ctx.Services.AnyRunning && _ctx.Config.Ui.ConfirmStopOnExit)
        {
            var answer = MessageBox.Show(
                "Quitting the panel stops the web server, PHP, the scheduler, the biometric helper and the tunnel.\n\nStop all services and quit?",
                "JPrime Control Panel", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
            if (answer != DialogResult.Yes) return;
        }
        _exiting = true;
        try
        {
            _tray.SetText("Stopping services...");
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await _ctx.Services.StopAllAsync(cts.Token).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Log.Warn($"Stop-all during exit: {ex.Message}");
        }
        finally
        {
            Shutdown();
        }
    }

    private void OnSessionEnding(object sender, SessionEndingEventArgs e)
    {
        // ~5 s budget: best-effort graceful signals, job objects do the rest.
        _exiting = true;
        try
        {
            _ctx.Services.KillAllNow();
        }
        catch { }
    }

    private void Shutdown()
    {
        SystemEvents.SessionEnding -= OnSessionEnding;
        _backupTimer.Dispose();
        try { _ctx.Services.Dispose(); } catch { }
        _tray.Dispose();
        _main?.Dispose();
        _instance.Release();
        ExitThread();
    }
}
