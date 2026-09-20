using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace JPrime.Panel.Health;

public sealed record LanAddress(string Ip, string Adapter, bool IsDhcp);

/// <summary>IPv4 addresses other devices on the gym network can use to reach this PC.</summary>
public static class LanAddresses
{
    public static IReadOnlyList<LanAddress> List()
    {
        var result = new List<LanAddress>();
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
                var name = nic.Name.ToLowerInvariant();
                if (name.Contains("vethernet") || name.Contains("wsl") || name.Contains("docker") || name.Contains("virtualbox") || name.Contains("vmware")) continue;
                var props = nic.GetIPProperties();
                IPv4InterfaceProperties? v4 = null;
                try { v4 = props.GetIPv4Properties(); } catch { }
                foreach (var addr in props.UnicastAddresses)
                {
                    if (addr.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    var ip = addr.Address.ToString();
                    if (ip.StartsWith("169.254.")) continue;
                    result.Add(new LanAddress(ip, nic.Name, v4?.IsDhcpEnabled ?? false));
                }
            }
        }
        catch
        {
        }
        return result;
    }
}
