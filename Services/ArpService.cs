using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace WireFox.Services
{
    public static class ArpService
    {
        [DllImport("iphlpapi.dll", ExactSpelling = true)]
        private static extern int SendARP(int destIp, int srcIp, byte[] pMacAddr, ref uint pdwPhysAddrLen);

        public static string? GetMacAddress(IPAddress? ipAddress)
        {
            if (ipAddress == null || ipAddress.AddressFamily != AddressFamily.InterNetwork)
            {
                return null;
            }

            try
            {
                byte[] macAddr = new byte[6];
                uint macAddrLen = (uint)macAddr.Length;
                byte[] ipBytes = ipAddress.GetAddressBytes();
                int destIp = BitConverter.ToInt32(ipBytes, 0);

                int result = SendARP(destIp, 0, macAddr, ref macAddrLen);
                if (result == 0 && macAddrLen == 6)
                {
                    return string.Join(":", macAddr.Select(b => b.ToString("X2")));
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ArpService] SendARP failed for {ipAddress}: {ex.Message}");
            }

            return null;
        }

        public static string? GetMacAddress(string? ipString)
        {
            if (string.IsNullOrWhiteSpace(ipString)) return null;
            if (IPAddress.TryParse(ipString, out var ip))
            {
                return GetMacAddress(ip);
            }
            return null;
        }
    }
}
