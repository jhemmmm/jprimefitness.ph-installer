using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace JPrime.Panel.Health;

public sealed record PortOwner(int Pid, string ProcessName, string? Hint);

/// <summary>Free-port checks and "who is listening on this port" via GetExtendedTcpTable.</summary>
public static class PortScanner
{
    /// <summary>True when nothing listens on the port (bind test on 0.0.0.0).</summary>
    public static bool IsFree(int port)
    {
        try
        {
            var listener = new TcpListener(IPAddress.Any, port);
            listener.Start();
            listener.Stop();
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    public static PortOwner? WhoHolds(int port)
    {
        foreach (var row in ListeningRows())
        {
            if (row.Port != port) continue;
            var pid = row.Pid;
            string name;
            string? hint = null;
            if (pid == 4)
            {
                name = "System (HTTP.sys)";
                hint = "Usually IIS / World Wide Web Publishing Service, Web Deploy or SQL Reporting Services. Pick another port (e.g. 8080).";
            }
            else
            {
                try
                {
                    using var p = Process.GetProcessById(pid);
                    name = p.ProcessName;
                }
                catch
                {
                    name = "unknown";
                }
                hint = name.ToLowerInvariant() switch
                {
                    "nginx" or "httpd" or "apache" => "Another web server (XAMPP/Laragon?) is using this port.",
                    "php-cgi" or "php" => "A PHP process is already bound here.",
                    "skype" => "Skype occupies the port; change its settings or pick another port.",
                    "com.docker.backend" or "vpnkit" => "Docker Desktop is forwarding this port.",
                    _ => null,
                };
            }
            return new PortOwner(pid, name, hint);
        }
        return null;
    }

    private static IEnumerable<(int Port, int Pid)> ListeningRows()
    {
        var size = 0;
        GetExtendedTcpTable(IntPtr.Zero, ref size, false, AF_INET, TCP_TABLE_OWNER_PID_LISTENER, 0);
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (GetExtendedTcpTable(buffer, ref size, false, AF_INET, TCP_TABLE_OWNER_PID_LISTENER, 0) != 0)
            {
                yield break;
            }
            var count = Marshal.ReadInt32(buffer);
            var rowPtr = IntPtr.Add(buffer, 4);
            var rowSize = Marshal.SizeOf<MIB_TCPROW_OWNER_PID>();
            for (var i = 0; i < count; i++)
            {
                var row = Marshal.PtrToStructure<MIB_TCPROW_OWNER_PID>(rowPtr);
                var port = (int)(((row.dwLocalPort & 0xFF) << 8) | ((row.dwLocalPort >> 8) & 0xFF));
                yield return (port, (int)row.dwOwningPid);
                rowPtr = IntPtr.Add(rowPtr, rowSize);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private const int AF_INET = 2;
    private const int TCP_TABLE_OWNER_PID_LISTENER = 3;

    [StructLayout(LayoutKind.Sequential)]
    private struct MIB_TCPROW_OWNER_PID
    {
        public uint dwState;
        public uint dwLocalAddr;
        public uint dwLocalPort;
        public uint dwRemoteAddr;
        public uint dwRemotePort;
        public uint dwOwningPid;
    }

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(IntPtr pTcpTable, ref int dwOutBufLen, bool sort, int ipVersion, int tableClass, uint reserved);
}
