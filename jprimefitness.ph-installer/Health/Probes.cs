using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;

namespace JPrime.Panel.Health;

public enum HealthLevel
{
    Ok,
    Degraded,
    Down,
}

public sealed record HealthResult(HealthLevel Level, string? Message = null)
{
    public static readonly HealthResult Healthy = new(HealthLevel.Ok);
    public static HealthResult Degraded(string message) => new(HealthLevel.Degraded, message);
    public static HealthResult Down(string message) => new(HealthLevel.Down, message);
}

public interface IHealthProbe
{
    Task<HealthResult> ProbeAsync(CancellationToken ct);
}

/// <summary>TCP connect to 127.0.0.1:port.</summary>
public sealed class TcpPortProbe : IHealthProbe
{
    private readonly int _port;
    private readonly string _host;

    public TcpPortProbe(int port, string host = "127.0.0.1")
    {
        _port = port;
        _host = host;
    }

    public async Task<HealthResult> ProbeAsync(CancellationToken ct)
    {
        try
        {
            using var client = new TcpClient();
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(1500);
            await client.ConnectAsync(_host, _port, cts.Token).ConfigureAwait(false);
            return HealthResult.Healthy;
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException)
        {
            return HealthResult.Down($"port {_port} not accepting connections");
        }
    }
}

/// <summary>HTTP GET; 2xx = Ok, other status = Degraded, connection failure = Down. Optional JSON classifier.</summary>
public sealed class HttpProbe : IHealthProbe
{
    private readonly HttpClient _http;
    private readonly string _url;
    private readonly Func<int, string, HealthResult>? _classify;

    public HttpProbe(HttpClient http, string url, Func<int, string, HealthResult>? classify = null)
    {
        _http = http;
        _url = url;
        _classify = classify;
    }

    public async Task<HealthResult> ProbeAsync(CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(3000);
            using var response = await _http.GetAsync(_url, HttpCompletionOption.ResponseContentRead, cts.Token).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
            var status = (int)response.StatusCode;
            if (_classify is not null) return _classify(status, body);
            return status is >= 200 and < 300
                ? HealthResult.Healthy
                : HealthResult.Degraded($"HTTP {status}");
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or IOException)
        {
            return HealthResult.Down("not responding");
        }
    }

    public static bool TryGetJsonBool(string body, string property, out bool value)
    {
        value = false;
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty(property, out var el)
                && (el.ValueKind is JsonValueKind.True or JsonValueKind.False))
            {
                value = el.GetBoolean();
                return true;
            }
        }
        catch (JsonException)
        {
        }
        return false;
    }
}
