using JPrime.Panel.App;
using JPrime.Panel.Config;
using JPrime.Panel.UI;
using JPrime.Panel.UI.Wizard;

namespace JPrime.Panel;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var cli = CommandLine.Parse(args);

        // Elevated helper mode: no UI, no single-instance, no config. Just do the admin work and exit.
        if (cli.AdminTasks.Count > 0)
        {
            return AdminTaskRunner.Run(cli);
        }

        if (cli.DevProvisionRoot is not null)
        {
            return Setup.DevProvision.Run(cli.DevProvisionRoot, cli.DevDevicePassword);
        }

        if (cli.DevScreenshotDir is not null)
        {
            return Setup.DevScreenshots.Run(cli.DevScreenshotDir, cli.InstallRootOverride);
        }

        if (cli.DevInstallRoot is not null)
        {
            return Setup.DevInstall.Run(cli.DevInstallRoot, cli.DevDevicePassword, cli.DevPayloadDir);
        }

        if (cli.DevUpdateRoot is not null && cli.DevUpdateZip is not null)
        {
            return Setup.DevUpdate.Run(cli.DevUpdateRoot, cli.DevUpdateZip);
        }
        if (cli.DevSmokeRoot is not null)
        {
            return Setup.DevSmoke.Run(cli.DevSmokeRoot, cli.DevSmokeHoldSeconds);
        }

        ApplicationConfiguration.Initialize();
        GlobalExceptionHandler.Install();

        using var instance = SingleInstance.TryAcquire();
        if (instance is null)
        {
            // Another panel is running: ask it to show itself and quit.
            SingleInstance.SignalShow();
            return 0;
        }
        UI.Wizard.Pages.SingleInstanceHandle.Current = instance;

        if (Elevation.IsElevated())
        {
            Log.Warn("Panel is running elevated. Child services would inherit admin rights; prefer launching as a normal user.");
        }

        var installRoot = cli.Wizard ? null : (cli.InstallRootOverride ?? InstallLocator.Find());
        if (installRoot is null)
        {
            // First run (or explicit --wizard): setup wizard.
            var wizard = new WizardForm(cli);
            Application.Run(wizard);
            return 0;
        }

        var paths = new AppPaths(installRoot);
        var store = new ConfigStore(paths.PanelConfigFile);
        PanelConfig config;
        try
        {
            config = store.Load();
        }
        catch (Exception ex)
        {
            Log.Error("Failed to load panel.json", ex);
            MessageBox.Show($"The panel configuration could not be read:\n\n{ex.Message}\n\nRun the setup wizard again to repair it.",
                "JPrime Control Panel", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 2;
        }

        AppServices.Initialize(paths, store, config);
        Log.Info($"Panel starting. Install root: {installRoot}. Autostart={cli.Autostart}");

        if (cli.Uninstall)
        {
            return Setup.Uninstaller.Run(AppServices.Current);
        }

        Application.Run(new PanelAppContext(cli, instance));
        return 0;
    }
}
