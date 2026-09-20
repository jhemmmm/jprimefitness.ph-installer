using System.Drawing.Imaging;
using JPrime.Panel.App;
using JPrime.Panel.Config;
using JPrime.Panel.UI.Panel;
using JPrime.Panel.UI.Wizard;

namespace JPrime.Panel.Setup;

/// <summary>Hidden developer mode: <c>JPrimePanel.exe --dev-screenshots &lt;outDir&gt; [--install-root &lt;root&gt;]</c>.
/// Renders every wizard page and the panel main window to PNG files without user interaction.</summary>
public static class DevScreenshots
{
    public static int Run(string outDir, string? installRoot)
    {
        Directory.CreateDirectory(outDir);
        ApplicationConfiguration.Initialize();

        // Wizard pages: instantiate the form, walk pages by reflection-free navigation.
        var cli = CommandLine.Parse(installRoot is null ? Array.Empty<string>() : new[] { "--install-root", installRoot });
        using (var wizard = new WizardForm(cli))
        {
            // JPRIME_SHOT_SIZE=min (minimum window) or WxH (e.g. 960x1200 to see a whole scrolling page) to catch clipped layouts.
            var shotSize = Environment.GetEnvironmentVariable("JPRIME_SHOT_SIZE");
            if (shotSize == "min") wizard.Size = wizard.MinimumSize;
            else if (shotSize?.Split('x') is [var w, var h] && int.TryParse(w, out var sw) && int.TryParse(h, out var sh)) wizard.Size = new Size(sw, sh);
            wizard.Show();
            Application.DoEvents();
            var i = 0;
            foreach (var title in wizard.PageTitlesForScreenshots())
            {
                wizard.ShowPageForScreenshot(i);
                Application.DoEvents();
                Thread.Sleep(150);
                Application.DoEvents();
                Capture(wizard, Path.Combine(outDir, $"wizard-{i:00}-{Slug(title)}.png"));
                i++;
            }
            wizard.Hide();
        }

        // Panel main window (needs an install root with panel.json).
        if (installRoot is not null && File.Exists(Path.Combine(installRoot, "config", "panel.json")))
        {
            var paths = new AppPaths(installRoot);
            var store = new ConfigStore(paths.PanelConfigFile);
            var ctx = AppServices.Initialize(paths, store, store.Load());
            UiThread.Capture();
            using var instance = SingleInstance.TryAcquire(500);
            var app = new PanelAppContext(cli, instance ?? throw new InvalidOperationException("panel already running"));
            Application.DoEvents();
            Thread.Sleep(300);
            Application.DoEvents();
            var main = Application.OpenForms.OfType<MainForm>().FirstOrDefault();
            if (main is not null)
            {
                Capture(main, Path.Combine(outDir, "panel-main.png"));
                using (var settings = new SettingsForm(ctx)) { settings.Show(); Application.DoEvents(); Capture(settings, Path.Combine(outDir, "panel-settings.png")); settings.Hide(); }
                using (var env = new EnvEditorForm(ctx)) { env.Show(); Application.DoEvents(); Capture(env, Path.Combine(outDir, "panel-env.png")); env.Hide(); }
                using (var art = new ArtisanConsoleForm(ctx)) { art.Show(); Application.DoEvents(); Capture(art, Path.Combine(outDir, "panel-artisan.png")); art.Hide(); }
                using (var upd = new UpdateForm(ctx)) { upd.Show(); Application.DoEvents(); Capture(upd, Path.Combine(outDir, "panel-update.png")); upd.Hide(); }
            }
            ctx.Services.Dispose();
        }
        return 0;
    }

    private static void Capture(Form form, string file)
    {
        using var bmp = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(bmp, new Rectangle(0, 0, form.Width, form.Height));
        bmp.Save(file, ImageFormat.Png);
    }

    private static string Slug(string s) => new string(s.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');
}
