namespace JPrime.Panel.App;

/// <summary>Parsed command line. Supported switches:
/// <c>--autostart</c> (start minimized, launch autostart services), <c>--wizard</c> (force setup wizard),
/// <c>--show</c> (front an existing instance), <c>--admin-task &lt;name&gt; [args...]</c> (elevated helper mode, repeatable),
/// <c>--install-root &lt;dir&gt;</c> (override install location), <c>--dev-download &lt;dir&gt;</c> (download payloads only).</summary>
public sealed class CommandLine
{
    public bool Autostart { get; private set; }
    public bool Wizard { get; private set; }
    public bool Show { get; private set; }
    public bool OpenBrowser { get; private set; }
    public bool Uninstall { get; private set; }
    public string? InstallRootOverride { get; private set; }
    public string? DevDownloadDir { get; private set; }
    public string? DevProvisionRoot { get; private set; }
    public string? DevDevicePassword { get; private set; }
    public string? DevSmokeRoot { get; private set; }
    public string? DevUpdateRoot { get; private set; }
    public string? DevUpdateZip { get; private set; }
    public string? DevInstallRoot { get; private set; }
    public string? DevScreenshotDir { get; private set; }
    public string? DevPayloadDir { get; private set; }
    public int DevSmokeHoldSeconds { get; private set; }
    public List<AdminTaskRequest> AdminTasks { get; } = new();

    public static CommandLine Parse(string[] args)
    {
        var cli = new CommandLine();
        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];
            switch (a.ToLowerInvariant())
            {
                case "--autostart":
                    cli.Autostart = true;
                    break;
                case "--wizard":
                    cli.Wizard = true;
                    break;
                case "--show":
                    cli.Show = true;
                    break;
                case "--open-browser":
                    cli.OpenBrowser = true;
                    break;
                case "--uninstall":
                    cli.Uninstall = true;
                    break;
                case "--install-root":
                    if (i + 1 < args.Length) cli.InstallRootOverride = args[++i];
                    break;
                case "--dev-download":
                    if (i + 1 < args.Length) cli.DevDownloadDir = args[++i];
                    break;
                case "--dev-provision":
                    if (i + 1 < args.Length) cli.DevProvisionRoot = args[++i];
                    break;
                case "--dev-update":
                    if (i + 2 < args.Length) { cli.DevUpdateRoot = args[++i]; cli.DevUpdateZip = args[++i]; }
                    break;
                case "--dev-smoke":
                    if (i + 1 < args.Length) cli.DevSmokeRoot = args[++i];
                    if (i + 1 < args.Length && int.TryParse(args[i + 1], out var hold)) { cli.DevSmokeHoldSeconds = hold; i++; }
                    break;
                case "--dev-screenshots":
                    if (i + 1 < args.Length) cli.DevScreenshotDir = args[++i];
                    break;
                case "--dev-install":
                    if (i + 1 < args.Length) cli.DevInstallRoot = args[++i];
                    break;
                case "--payloads":
                    if (i + 1 < args.Length) cli.DevPayloadDir = args[++i];
                    break;
                case "--with-hikvision":
                    if (i + 1 < args.Length) cli.DevDevicePassword = args[++i];
                    break;
                case "--admin-task":
                    {
                        if (i + 1 >= args.Length) break;
                        var name = args[++i];
                        var taskArgs = new List<string>();
                        while (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                        {
                            taskArgs.Add(args[++i]);
                        }
                        cli.AdminTasks.Add(new AdminTaskRequest(name, taskArgs));
                        break;
                    }
            }
        }
        return cli;
    }
}

public sealed record AdminTaskRequest(string Name, IReadOnlyList<string> Args);
