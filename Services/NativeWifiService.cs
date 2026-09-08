using System;
using System.Runtime.InteropServices;
using System.Text;

namespace WireFox.Services
{
    public static class NativeWifiService
    {
        private const uint WLAN_API_VERSION_2_0 = 2;
        private const uint ERROR_SUCCESS = 0;
        private const int WLAN_INTF_OPCODE_CURRENT_CONNECTION = 7;

        [DllImport("wlanapi.dll", SetLastError = true)]
        private static extern uint WlanOpenHandle(
            uint dwClientVersion,
            IntPtr pReserved,
            out uint pdwNegotiatedVersion,
            out IntPtr phClientHandle);

        [DllImport("wlanapi.dll", SetLastError = true)]
        private static extern uint WlanCloseHandle(
            IntPtr hClientHandle,
            IntPtr pReserved);

        [DllImport("wlanapi.dll", SetLastError = true)]
        private static extern uint WlanEnumInterfaces(
            IntPtr hClientHandle,
            IntPtr pReserved,
            out IntPtr ppInterfaceList);

        [DllImport("wlanapi.dll", SetLastError = true)]
        private static extern uint WlanQueryInterface(
            IntPtr hClientHandle,
            [In] ref Guid pInterfaceGuid,
            int OpCode,
            IntPtr pReserved,
            out uint pdwDataSize,
            out IntPtr ppData,
            IntPtr pWlanOpcodeValueType);

        [DllImport("wlanapi.dll", SetLastError = true)]
        private static extern void WlanFreeMemory(IntPtr pMemory);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WLAN_INTERFACE_INFO
        {
            public Guid InterfaceGuid;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string strInterfaceDescription;
            public int isState;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WLAN_INTERFACE_INFO_LIST
        {
            public uint dwNumberOfItems;
            public uint dwIndex;
            public WLAN_INTERFACE_INFO InterfaceInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DOT11_SSID
        {
            public uint uSSIDLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
            public byte[] ucSSID;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WLAN_ASSOCIATION_ATTRIBUTES
        {
            public DOT11_SSID dot11Ssid;
            public int dot11BssType;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
            public byte[] dot11Bssid;
            public int dot11PhyType;
            public uint uDot11PhyIndex;
            public uint wlanSignalQuality;
            public uint ulRxRate;
            public uint ulTxRate;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WLAN_CONNECTION_ATTRIBUTES
        {
            public int isState;
            public int wlanConnectionMode;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string strProfileName;
            public WLAN_ASSOCIATION_ATTRIBUTES wlanAssociationAttributes;
            public int wlanSecurityAttributes;
        }

        public static (string? Ssid, string? Bssid) GetCurrentConnectionInfo()
        {
            IntPtr clientHandle = IntPtr.Zero;
            IntPtr pInterfaceList = IntPtr.Zero;

            try
            {
                uint negotiatedVersion;
                uint result = WlanOpenHandle(WLAN_API_VERSION_2_0, IntPtr.Zero, out negotiatedVersion, out clientHandle);
                if (result != ERROR_SUCCESS)
                {
                    return (null, null);
                }

                result = WlanEnumInterfaces(clientHandle, IntPtr.Zero, out pInterfaceList);
                if (result != ERROR_SUCCESS || pInterfaceList == IntPtr.Zero)
                {
                    return (null, null);
                }

                var interfaceList = Marshal.PtrToStructure<WLAN_INTERFACE_INFO_LIST>(pInterfaceList);
                long currentPtr = pInterfaceList.ToInt64() + Marshal.OffsetOf<WLAN_INTERFACE_INFO_LIST>("InterfaceInfo").ToInt64();

                for (int i = 0; i < interfaceList.dwNumberOfItems; i++)
                {
                    var info = Marshal.PtrToStructure<WLAN_INTERFACE_INFO>(new IntPtr(currentPtr));
                    currentPtr += Marshal.SizeOf<WLAN_INTERFACE_INFO>();

                    // 1 = wlan_interface_state_connected
                    if (info.isState == 1)
                    {
                        IntPtr pData = IntPtr.Zero;
                        try
                        {
                            uint dataSize;
                            Guid guid = info.InterfaceGuid;
                            uint queryResult = WlanQueryInterface(
                                clientHandle,
                                ref guid,
                                WLAN_INTF_OPCODE_CURRENT_CONNECTION,
                                IntPtr.Zero,
                                out dataSize,
                                out pData,
                                IntPtr.Zero);

                            if (queryResult == ERROR_SUCCESS && pData != IntPtr.Zero)
                            {
                                var connAttr = Marshal.PtrToStructure<WLAN_CONNECTION_ATTRIBUTES>(pData);
                                var ssidBytes = connAttr.wlanAssociationAttributes.dot11Ssid.ucSSID;
                                uint ssidLength = connAttr.wlanAssociationAttributes.dot11Ssid.uSSIDLength;

                                string? ssid = null;
                                if (ssidLength > 0 && ssidLength <= 32)
                                {
                                    ssid = Encoding.UTF8.GetString(ssidBytes, 0, (int)ssidLength);
                                }

                                string? bssid = null;
                                byte[] bssidBytes = connAttr.wlanAssociationAttributes.dot11Bssid;
                                if (bssidBytes != null && bssidBytes.Length == 6)
                                {
                                    // Verify not all zeroes
                                    bool hasNonZero = false;
                                    for (int b = 0; b < 6; b++) if (bssidBytes[b] != 0) { hasNonZero = true; break; }
                                    if (hasNonZero)
                                    {
                                        bssid = string.Format("{0:X2}:{1:X2}:{2:X2}:{3:X2}:{4:X2}:{5:X2}",
                                            bssidBytes[0], bssidBytes[1], bssidBytes[2], bssidBytes[3], bssidBytes[4], bssidBytes[5]);
                                    }
                                }

                                if (!string.IsNullOrWhiteSpace(ssid))
                                {
                                    return (ssid, bssid);
                                }
                            }
                        }
                        finally
                        {
                            if (pData != IntPtr.Zero)
                            {
                                WlanFreeMemory(pData);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Error("NativeWifiService", "Error querying WiFi", ex);
            }
            finally
            {
                if (pInterfaceList != IntPtr.Zero)
                {
                    WlanFreeMemory(pInterfaceList);
                }
                if (clientHandle != IntPtr.Zero)
                {
                    WlanCloseHandle(clientHandle, IntPtr.Zero);
                }
            }

            return (null, null);
        }

        public static string? GetCurrentConnectedSsid()
        {
            var info = GetCurrentConnectionInfo();
            return info.Ssid;
        }
    }
}
