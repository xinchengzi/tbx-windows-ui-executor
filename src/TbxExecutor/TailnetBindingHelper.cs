using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace TbxExecutor;

public static class TailnetBindingHelper
{
    public static string? TryGetTailnetIp()
    {
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up) continue;
            if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

            var ipProps = nic.GetIPProperties();
            foreach (var uni in ipProps.UnicastAddresses)
            {
                var ip = uni.Address;
                if (ip.AddressFamily != AddressFamily.InterNetwork) continue;
                if (IsTailnetIp(ip)) return ip.ToString();
            }
        }

        return null;
    }

    private static bool IsTailnetIp(IPAddress ip)
    {
        var bytes = ip.GetAddressBytes();
        if (bytes.Length != 4) return false;
        if (bytes[0] != 100) return false;
        return bytes[1] >= 64 && bytes[1] <= 127;
    }
}
