using System.Net.Http;
using System.Net.Http.Headers;
using JPrime.Panel.Config;
using JPrime.Panel.Services;

namespace JPrime.Panel.App;

/// <summary>Static composition root for panel mode. Built once in <c>Program</c> after the install is located.</summary>
public sealed class AppServices
{
    public const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) JPrimePanel/1.0 (+https://github.com/jhemmmm/jprimefitness.ph)";

    private static AppServices? _current;

    private AppServices(AppPaths paths, ConfigStore store, PanelConfig config)
    {
        Paths = paths;
        Store = store;
        Config = config;
        Secrets = new SecretStore(paths.SecretsFile);
        Http = CreateHttpClient();
        Services = new ServiceManager(this);
    }

    public static AppServices Current => _current ?? throw new InvalidOperationException("AppServices not initialized");
    public static bool IsInitialized => _current is not null;

    public AppPaths Paths { get; }
    public ConfigStore Store { get; }
    public PanelConfig Config { get; private set; }
    public SecretStore Secrets { get; }
    public HttpClient Http { get; }
    public ServiceManager Services { get; }

    public static AppServices Initialize(AppPaths paths, ConfigStore store, PanelConfig config)
    {
        Log.Configure(paths.LogsDir);
        _current = new AppServices(paths, store, config);
        return _current;
    }

    public void SaveConfig()
    {
        try
        {
            Store.Save(Config);
        }
        catch (Exception ex)
        {
            Log.Error("Saving panel.json failed", ex);
        }
    }

    public void ReloadConfig()
    {
        Config = Store.Load();
    }

    public static HttpClient CreateHttpClient()
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            ConnectTimeout = TimeSpan.FromSeconds(10),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        };
        var http = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));
        return http;
    }
}
