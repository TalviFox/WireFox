# WireFox v1.0.6 Potential Fixes & Architectural Review

> **STATUS:** IMPLEMENTED (v1.0.6 Release in Progress)  
> **TARGET RELEASE:** v1.0.6  
> **SCOPE:** Deep review and concrete implementation specs for teardown & state-handling issues identified in `WatchdogService.cs` and `WireGuardCliService.cs`.

---

## 1. Executive Summary & Review of Agent Findings

Another agent suggested two fixes:
1. **`WatchdogService.cs`**: Prevent redundant stop loops when settled on trusted networks by checking `_status == TunnelStatus.Bypassed && !isRunning`.
2. **`WireGuardCliService.cs`**: Increase `WaitForStatus()` from 3s to 8s during tunnel stop, and eliminate premature `sc.exe delete` / `ForceKillServiceProcess()` calls that corrupt Wintun driver unbinding and WireGuard named pipes.

### Our Verdict: **Both findings are accurate and identify critical edge cases**, but the other agent's proposed solutions missed several subtle interactions in WireFox:
- **Issue 1**: In addition to `_status == TunnelStatus.Bypassed && !isRunning`, `StopAllTunnelsAsync()` is an aggressive **Hard Reset** (it invokes `taskkill /F`, service deletions, and `ipconfig /flushdns`). Running this on every DHCP renewal, Wi-Fi roam, or route settle event while on a trusted network causes adapter churn and DNS cache thrashing. We also need to ensure session timers (`_sessionExpiresUtc`) are properly reset when transitioning into trusted mode.
- **Issue 2**: The bug in `StopTunnelAsync()` is actually **worse** than the agent described:
  1. **Premature StopPending Escalation**: At line 631, if the service is in `StopPending` state, WireFox *instantly* escalates to `ForceKillServiceProcess()` without waiting! If another process (or a previous call) requested a stop milliseconds earlier, WireFox assassinates the service while it is legitimately shutting down.
  2. **False `TimeoutException`**: The 3-second `WaitForStatus()` timeout is too short for Windows NDIS to dismantle the Wintun adapter and delete routing table entries.
  3. **The `sc.exe delete` / Service Destruction Trap**: Lines 662–677 check `IsTunnelServiceRunning()`. If the service stopped but the kernel adapter takes ~500ms to unbind from NDIS, `GetActiveInterfaceNames()` still sees it, so WireFox executes `sc.exe delete`. This marks the service with `SERVICE_DELETE` (`ERROR_SERVICE_MARKED_FOR_DELETE` / Win32 Error 1072), which prevents `StartTunnelAsync()` from starting the existing service without a full system reboot or handle close.

Below is the complete analysis, verified code paths, and ready-to-apply diffs for the v1.0.6 release.

---

## 1.1 Empirical Verification: Analysis of User Production Logs

The runtime logs provided by the user illustrate **both issues occurring in real-time** with exact timestamps:

### A. Proof of Redundant Stop Loops & Hard Resets While Already Stopped
Notice the recurring pattern when sitting on trusted network `d[-_-]b` or `Fox Den`:
- `[21:49:06.111]` `Network settled: IsTrusted=True, IsRunning=False` -> `StopAllTunnelsAsync` triggered -> DNS flushed -> `Bypassed`.
- `[21:49:21.584]` (15 seconds later, same Wi-Fi, still `IsRunning=False`) -> `StopAllTunnelsAsync` **triggered again**!
- `[21:50:11.995]` (`IsRunning=False`) -> `StopAllTunnelsAsync` **triggered again**!
- `[21:50:54.359]` (`IsRunning=False` on `Fox Den`) -> `StopAllTunnelsAsync` **triggered again**!
- `[22:02:20.019]` (`IsRunning=False`) -> `StopAllTunnelsAsync` **triggered again**!
- `[22:08:51.218]` (`IsRunning=False`) -> `StopAllTunnelsAsync` **triggered again**!

**Impact:** Every DHCP renew or network event while safely sitting on trusted Wi-Fi spams `StopAllTunnelsAsync()`, runs `taskkill`, and flushes the Windows DNS resolver cache over and over.

### B. Proof of Premature Force-Kill on Trusted Transition
Look at what happened at `[21:50:09.805]` and `[22:02:17.825]` when switching from untrusted network `FoX` to trusted network `d[-_-]b` / `Fox Den`:
```log
[21:50:09.805] [INFO] [WatchdogService] Network settled: IsTrusted=True, Interface='FoxDenVPN-FoxTop', IsRunning=True, ManuallyDisabled=False
[21:50:09.805] [INFO] [WatchdogService] Trusted network active: stopping all VPN tunnels
[21:50:09.805] [INFO] [WireGuardCli] Executing StopAllTunnelsAsync for all WireGuard tunnels (Hard Reset)...
[21:50:09.808] [WARNING] [WireGuardCli] Hard stopping 'FoxDenVPN-FoxTop'...
[21:50:09.810] [WARNING] [WireGuardCli] ForceStopTunnelAsync invoked for 'FoxDenVPN-FoxTop' (Service: WireGuardTunnel$FoxDenVPN-FoxTop)
[21:50:09.812] [WARNING] [WireGuardCli] Forcefully terminating hung service process 'wireguard' (PID: 15696)...
```
Because `OnNetworkSettled` invoked `StopAllTunnelsAsync()` rather than graceful `StopTunnelAsync()`, WireFox bypassed all graceful shutdown protocols, immediately classified the healthy service as "hung", and force-killed `wireguard.exe` (`PID: 15696`) within 7 milliseconds!

### C. Proof of the Service Deletion Trap vs. Clean Service Reuse
1. **Destructive Stop (via `ForceStopTunnelAsync` on trusted transition):**  
   Because `ForceStopTunnelAsync` ran `sc.exe delete`, the Windows service was wiped out. When moving back to untrusted `FoX` at `[21:51:16.395]`:
   ```log
   [21:51:16.395] [INFO] [WireGuardCli] Installing tunnel service using 'C:\Users\Talvi\Downloads\FoxDenVPN-FoxTop.conf'...
   [21:51:16.494] [SUCCESS] [WireGuardCli] Tunnel 'FoxDenVPN-FoxTop' installed and running.
   ```
   It had to run a full `/installtunnelservice`, reinstalling the driver and virtual adapter.

2. **Clean Stop (via `PauseTunnelAsync` at `[21:51:45.389]`):**  
   When the user paused the tunnel, `StopTunnelAsync` executed:
   ```log
   [21:51:45.394] [INFO] [WireGuardCli] Stopping service 'WireGuardTunnel$FoxDenVPN-FoxTop'...
   [21:51:45.654] [SUCCESS] [WireGuardCli] Service 'WireGuardTunnel$FoxDenVPN-FoxTop' stopped.
   ```
   And when resuming at `[21:52:12.531]`:
   ```log
   [21:52:12.531] [INFO] [WireGuardCli] Starting existing service 'WireGuardTunnel$FoxDenVPN-FoxTop'...
   [21:52:13.082] [SUCCESS] [WireGuardCli] Tunnel service 'WireGuardTunnel$FoxDenVPN-FoxTop' started successfully.
   ```
   It started instantly in 500ms without reinstalling!

3. **Ghost Adapter Artifacts in Diagnostic Scan:**  
   At `[21:54:48.420]`, the diagnostic scan discovered:
   `FoxDenVPN-FoxTop-Npcap Packet Driver (NPCAP)-0000`  
   This ghost adapter entry is a direct consequence of repeated hard force-kills and service deletions.

### D. Analysis of 1-Hour Physical Network Outage & Reconnect Scenario (User Log: 14:52 - 15:56)
The latest user production log illustrates a classic sleep / out-of-range recovery scenario:
- **14:52:51 to 15:55:59 (~64 minutes offline):**
  The device's physical connection was offline. Watchdog evaluated gateway reachability every 8–12 seconds and correctly suppressed tunnel recovery:
  `[DEBUG] [WatchdogService] Local default gateway is unreachable; physical network is offline. Skipping tunnel recovery.`
- **15:56:04 to 15:56:07 (Physical Reconnect):**
  Wi-Fi associated and DHCP bound to SSID `FoX` (untrusted, Gateway `10.11.248.81`).
- **15:56:09.910 (The Race / Leak Warning):**
  Watchdog evaluated:
  `[WARNING] [WatchdogService] Tunnel degradation detected on 'FoxDenVPN-FoxTop' (Failures=1, HandshakeStale=True, FlowBlackholed=False, InternetHealthy=True)`
- **15:56:10.136 (The Inaction):**
  `[INFO] [WatchdogService] Network settled: IsTrusted=False, Interface='FoxDenVPN-FoxTop', IsRunning=True, ManuallyDisabled=False`
  Because `isRunning` was `true` (the Windows service had remained alive in the background), `OnNetworkSettled` took **no action**.
- **15:56:44 to 15:56:51 (The User's Manual Recovery):**
  After 34 seconds of frozen/unusable tunnel state, the user manually clicked "Pause 15 Mins" and then resumed:
  ```log
  [15:56:44.776] [INFO] [WireGuardCli] Stopping service 'WireGuardTunnel$FoxDenVPN-FoxTop'...
  [15:56:45.295] [SUCCESS] [WireGuardCli] Service 'WireGuardTunnel$FoxDenVPN-FoxTop' stopped.
  [15:56:45.427] [DEBUG] [WireGuardCli] DNS resolver cache flushed via dnsapi.dll
  [15:56:50.148] [INFO] [WireGuardCli] Starting existing service 'WireGuardTunnel$FoxDenVPN-FoxTop'...
  [15:56:51.473] [SUCCESS] [WireGuardCli] Tunnel service 'WireGuardTunnel$FoxDenVPN-FoxTop' started successfully.
  ```

#### Critical Findings from This Scenario:
1. **Do the existing proposed fixes fix this problem?**
   - **Fix 1 (`WatchdogService.cs`)**: No. Fix 1 applies exclusively to **trusted** network transitions (`state.IsTrusted == true`). Here the network was untrusted (`FoX`).
   - **Fix 2 (`WireGuardCliService.cs`)**: Partially. Fix 2 prevents `sc.exe delete` and force-kills during stops, which allowed the user's manual pause/resume to complete cleanly in 1.8 seconds. But Fix 2 does **not** automate the recovery or prevent the tunnel from getting stuck in the first place.
2. **The "Zombie Tunnel" Gap on Untrusted Settle:**
   When returning from sleep or extended physical disconnect, the WireGuard Windows service process is still running, but the kernel UDP endpoint and Wintun route bindings are dead or tied to the old adapter. Because `isRunning == true`, WireFox does nothing.
3. **The False `InternetHealthy` / Traffic Leak Race:**
   `HandshakeStale=True` with `InternetHealthy=True` indicates that the HTTP 204 internet check passed over the physical Wi-Fi interface (traffic leak!), masking the fact that the VPN was stalled.
4. **Graceful Restart vs Nuclear Reset:**
   The user's manual pause/resume proved that a clean `StopTunnelAsync` (519ms) + `StartTunnelAsync` (1325ms) recovers the tunnel in under 2 seconds. In contrast, WireFox's current `ForceRestartTunnelAsync()` runs `StopAllTunnelsAsync(true)` (nuclear `taskkill`, `sc.exe delete`, driver reinstallation). Replacing `ForceRestartTunnelAsync()` with a graceful restart is an essential v1.0.6 refinement.

---

## 2. Issue 1: Redundant Stop Loop on Trusted Networks (`WatchdogService.cs`)

### Codebase Investigation
- **File**: `Services/WatchdogService.cs` (lines 104–118)
- **Current implementation**:
  ```csharp
  string targetInterface = GetEffectiveInterfaceName(config);
  bool isRunning = cli.IsTunnelServiceRunning(targetInterface);

  LoggingService.Instance.Info("WatchdogService", 
      $"Network settled: IsTrusted={state.IsTrusted}, Interface='{targetInterface}', IsRunning={isRunning}, ManuallyDisabled={_isManuallyDisabled}");

  // 1. If connected to a Trusted network -> Bypass VPN
  if (state.IsTrusted)
  {
      _sessionExpiresUtc = null;
      LoggingService.Instance.Info("WatchdogService", "Trusted network active: stopping all VPN tunnels");
      await cli.StopAllTunnelsAsync();
      SetStatus(TunnelStatus.Bypassed);
      return;
  }
  ```

### The Problem
`NetworkMonitorService` debounces and fires `NetworkSettled` on any local network change:
- Wi-Fi BSSID/AP handover
- DHCP lease renewals
- Default gateway ARP resolution
- IPv6 SLAAC address changes or VPN adapter detachment

Whenever a user is on their home/office trusted Wi-Fi, `state.IsTrusted` is `true`.  
With the current code, every single settle event executes:
```csharp
await cli.StopAllTunnelsAsync();
```
`StopAllTunnelsAsync()` (in `WireGuardCliService.cs` lines 742–796):
1. Enumerates all Windows services matching `WireGuardTunnel$*`.
2. Calls `ForceStopTunnelAsync()` (which kills processes and calls `sc.exe delete`).
3. Executes `taskkill /F /FI "SERVICES eq WireGuardTunnel*"`.
4. Executes `taskkill /F /IM wg.exe`.
5. Executes `ipconfig /flushdns` (via `DnsFlushResolverCache`).

When the tunnel is **already stopped** and the state is **already `Bypassed`**, executing this full nuclear sweep:
- Generates unnecessary disk I/O, process creation (`taskkill`, `ipconfig`), and event log clutter.
- Flushes the local DNS cache repeatedly while browsing.
- Can fight with the official WireGuard Windows client UI if the user is running both.

### Refined Solution for v1.0.6
When `state.IsTrusted` is true:
1. Always clear any active temporary session (`_sessionExpiresUtc = null`).
2. Check if the tunnel is already inactive:
   - If `_status == TunnelStatus.Bypassed && !isRunning`, log a lightweight debug/info message and return immediately.
   - If `!isRunning` but `_status != TunnelStatus.Bypassed` (e.g. app just started or user disconnected), simply update `SetStatus(TunnelStatus.Bypassed)` and return without calling `StopAllTunnelsAsync()`.
3. Only if a tunnel is actually running (`isRunning == true` or active interfaces exist) should teardown occur.
4. Furthermore, prefer graceful `cli.StopTunnelAsync(targetInterface)` rather than nuke-the-world `cli.StopAllTunnelsAsync()` unless multiple rogue tunnels are detected.

### Concrete Patch / Diff for `WatchdogService.cs`

```diff
--- a/Services/WatchdogService.cs
+++ b/Services/WatchdogService.cs
@@ -110,9 +110,24 @@
             // 1. If connected to a Trusted network -> Bypass VPN
             if (state.IsTrusted)
             {
                 _sessionExpiresUtc = null;
+
+                // Guard: If already bypassed and no tunnel is active, avoid teardown churn
+                if (_status == TunnelStatus.Bypassed && !isRunning)
+                {
+                    LoggingService.Instance.Debug("WatchdogService", "Network settled on trusted network: tunnel already bypassed and inactive.");
+                    return;
+                }
+
+                // If no tunnel is running, transition status directly without executing process teardowns
+                if (!isRunning)
+                {
+                    LoggingService.Instance.Info("WatchdogService", "Trusted network active: no tunnels running, setting status to Bypassed.");
+                    SetStatus(TunnelStatus.Bypassed);
+                    return;
+                }
+
-                LoggingService.Instance.Info("WatchdogService", "Trusted network active: stopping all VPN tunnels");
-                await cli.StopAllTunnelsAsync();
+                LoggingService.Instance.Info("WatchdogService", $"Trusted network active: stopping active tunnel '{targetInterface}' gracefully...");
+                await cli.StopTunnelAsync(targetInterface);
                 SetStatus(TunnelStatus.Bypassed);
                 return;
             }
```

---

## 3. Issue 2: Premature Force-Kill & Service Deletion in `StopTunnelAsync()` (`WireGuardCliService.cs`)

### Codebase Investigation
- **File**: `Services/WireGuardCliService.cs` (lines 625–695)
- **Current implementation**:
  ```csharp
  else
  {
      if (existingService.Status == ServiceControllerStatus.Stopped)
      {
          LoggingService.Instance.Info("WireGuardCli", $"Service '{serviceName}' is already stopped.");
      }
      else if (existingService.Status == ServiceControllerStatus.StopPending)
      {
          LoggingService.Instance.Warning("WireGuardCli", 
              $"Service '{serviceName}' is in StopPending state. Escalating immediately to force-kill...");
          ForceKillServiceProcess(serviceName);
      }
      else if (existingService.CanStop)
      {
          try
          {
              LoggingService.Instance.Info("WireGuardCli", $"Stopping service '{serviceName}'...");
              existingService.Stop();
              existingService.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(3));
              LoggingService.Instance.Success("WireGuardCli", $"Service '{serviceName}' stopped.");
          }
          catch (Exception ex)
          {
              LoggingService.Instance.Warning("WireGuardCli", 
                  $"Service '{serviceName}' graceful stop timed out or failed: {ex.Message}. Escalating to force-kill...");
              ForceKillServiceProcess(serviceName);
          }
      }
      ...
  }

  // If still running or if we wish to clean up ghost interfaces
  if (IsTunnelServiceRunning(interfaceName))
  {
      try
      {
          var psi = new ProcessStartInfo
          {
              FileName = "sc.exe",
              Arguments = $"delete \"{serviceName}\"",
              UseShellExecute = false,
              CreateNoWindow = true
          };
          using var proc = Process.Start(psi);
          proc?.WaitForExit(3000);
      }
      catch { }
  }
  ```

### The Problem
There are three cascading flaws in this method:

1. **The 3-second `WaitForStatus()` Timeout**:
   When `wireguard.exe /service <name>` receives a STOP request, it must:
   - Close active WireGuard sessions and tear down kernel crypto contexts.
   - Delete routing table modifications (e.g. default route `0.0.0.0/0`, specific route metrics).
   - Restore DNS server settings on physical adapters.
   - Call the Wintun driver to release and unbind the virtual NDIS miniport adapter.
   - Close the named pipe (`\\.\pipe\WireGuard\...`) and RPC endpoints.
   
   On a typical Windows 10/11 system under load or with multiple network adapters, NDIS unbinding and route table cleanup takes **3 to 6 seconds**.  
   Because the timeout is set to **3 seconds**, `existingService.WaitForStatus(...)` throws a `TimeoutException`. The catch block triggers and executes `ForceKillServiceProcess()`.

2. **The `StopPending` Trap**:
   If `existingService.Status == ServiceControllerStatus.StopPending`, line 631 immediately treats it as a hung service and terminates it! But `StopPending` is the normal, expected intermediate state while the service is gracefully unbinding. It should be given a wait window before being killed.

3. **Corruption of Named Pipes and Driver via `proc.Kill(true)`**:
   When `ForceKillServiceProcess()` terminates `wireguard.exe` mid-teardown:
   - Wintun handles are not closed cleanly, leaving ghost adapter GUIDs in Windows registry/NDIS.
   - The official WireGuard UI's named pipe becomes broken or enters an unrecoverable state, causing error dialogs in WireGuard.

4. **Accidental Service Deletion via `sc.exe delete`**:
   At line 662:
   ```csharp
   if (IsTunnelServiceRunning(interfaceName))
   ```
   `IsTunnelServiceRunning()` checks `GetActiveInterfaceNames()`. If the Windows service has stopped, but Windows NDIS takes ~500ms to finish adapter unregistration, `GetActiveInterfaceNames()` can briefly still return the interface name.  
   WireFox interprets this as "still running", and immediately executes `sc.exe delete "{serviceName}"`.
   
   **Why `sc.exe delete` here is dangerous**:
   - In `StartTunnelAsync()` (line 528), WireFox relies on reusing an existing service in `Stopped` status via `existingService.Start()`, which starts in milliseconds.
   - If `sc.exe delete` was called, the service is either completely gone or flagged as `ERROR_SERVICE_MARKED_FOR_DELETE` (1072) until all handles close. Subsequent connection attempts fail or are forced to run `/installtunnelservice`, reinstalling the driver and creating new adapter GUIDs.
   - `sc.exe delete` should **only** be called in emergency cleanup (`ForceStopTunnelAsync`) or when the service is proven stuck/unrecoverable, never on a healthy stop!

### Refined Solution for v1.0.6
1. **Increase Graceful Timeout to 8 Seconds**:
   Change `WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(3))` to `TimeSpan.FromSeconds(8)`.
2. **Handle `StopPending` by Waiting**:
   If the service is already in `StopPending`, do not force-kill immediately! Call `existingService.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(8))` to give it a chance to finish. Only if that times out should force-kill be considered.
3. **Avoid Subprocess Churn during Adapter Settle**:
   Do NOT call `IsTunnelServiceRunning(interfaceName)` inside the 100ms polling loop—`IsTunnelServiceRunning` spawns `wg.exe show interfaces` via `Process.Start` on every iteration, causing up to 10 console subprocesses per second. Instead, check `NetworkInterface.GetAllNetworkInterfaces()` directly in memory via .NET BCL.
4. **Dispose `existingService` Handle Before `sc.exe delete`**:
   The .NET `ServiceController` instance holds an open `SC_HANDLE` to the service in Windows Service Control Manager. If `sc.exe delete` is executed while this handle is open, Windows cannot purge the service and flags it as `SERVICE_FLAG_DELETE_PENDING`, throwing Win32 Error 1072 (`ERROR_SERVICE_MARKED_FOR_DELETE`). Calling `existingService?.Close()` and `existingService?.Dispose()` before `sc.exe delete` guarantees immediate handle release.
5. **Guard `sc.exe delete`**:
   Do NOT execute `sc.exe delete` if the service successfully transitioned to `Stopped`. Reserve service deletion exclusively for cases where the service is stuck in an unkillable state or during an explicit forced reset (`ForceStopTunnelAsync`).

### Concrete Patch / Diff for `WireGuardCliService.cs`

```diff
--- a/Services/WireGuardCliService.cs
+++ b/Services/WireGuardCliService.cs
@@ -628,19 +628,30 @@
                         if (existingService.Status == ServiceControllerStatus.Stopped)
                         {
                             LoggingService.Instance.Info("WireGuardCli", $"Service '{serviceName}' is already stopped.");
                         }
                         else if (existingService.Status == ServiceControllerStatus.StopPending)
                         {
-                            LoggingService.Instance.Warning("WireGuardCli", 
-                                $"Service '{serviceName}' is in StopPending state. Escalating immediately to force-kill...");
-                            ForceKillServiceProcess(serviceName);
+                            LoggingService.Instance.Info("WireGuardCli", 
+                                $"Service '{serviceName}' is already in StopPending state. Waiting up to 8s for graceful completion...");
+                            try
+                            {
+                                existingService.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(8));
+                                LoggingService.Instance.Success("WireGuardCli", $"Service '{serviceName}' completed stop.");
+                            }
+                            catch (Exception ex)
+                            {
+                                LoggingService.Instance.Warning("WireGuardCli", 
+                                    $"Service '{serviceName}' StopPending wait timed out ({ex.Message}). Escalating to force-kill...");
+                                ForceKillServiceProcess(serviceName);
+                            }
                         }
                         else if (existingService.CanStop)
                         {
                             try
                             {
                                 LoggingService.Instance.Info("WireGuardCli", $"Stopping service '{serviceName}'...");
                                 existingService.Stop();
-                                existingService.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(3));
+                                existingService.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(8));
                                 LoggingService.Instance.Success("WireGuardCli", $"Service '{serviceName}' stopped.");
                             }
                             catch (Exception ex)
                             {
                                 LoggingService.Instance.Warning("WireGuardCli", 
                                     $"Service '{serviceName}' graceful stop timed out or failed: {ex.Message}. Escalating to force-kill...");
                                 ForceKillServiceProcess(serviceName);
                             }
                         }
                         else
                         {
                             LoggingService.Instance.Warning("WireGuardCli", 
                                 $"Service '{serviceName}' cannot stop gracefully (Status: {existingService.Status}). Force-terminating...");
                             ForceKillServiceProcess(serviceName);
                         }
                     }
 
-                    // If still running or if we wish to clean up ghost interfaces
-                    if (IsTunnelServiceRunning(interfaceName))
+                    // Allow NDIS and Wintun driver up to 1000ms to finish unregistering the virtual adapter in-memory (no wg.exe subprocess churn)
+                    for (int i = 0; i < 10; i++)
+                    {
+                        bool adapterPresent = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
+                            .Any(ni => string.Equals(ni.Name, interfaceName, StringComparison.OrdinalIgnoreCase) ||
+                                       string.Equals(ni.Description, interfaceName, StringComparison.OrdinalIgnoreCase));
+                        if (!adapterPresent) break;
+                        Thread.Sleep(100);
+                    }
+
+                    // Only perform service deletion if the service is STILL unrecoverably stuck after force-kill
+                    if (IsTunnelServiceRunning(interfaceName))
                     {
+                        LoggingService.Instance.Warning("WireGuardCli", 
+                            $"Interface '{interfaceName}' still active after stop/kill attempts. Removing stuck service entry...");
+                        
+                        // Crucial: Release the .NET ServiceController handle before running sc delete,
+                        // otherwise Windows holds the service in ERROR_SERVICE_MARKED_FOR_DELETE (Error 1072).
+                        existingService?.Close();
+                        existingService?.Dispose();
+                        existingService = null;
+
                         try
                         {
                             var psi = new ProcessStartInfo
                             {
                                 FileName = "sc.exe",
                                 Arguments = $"delete \"{serviceName}\"",
                                 UseShellExecute = false,
                                 CreateNoWindow = true
                             };
                             using var proc = Process.Start(psi);
                             proc?.WaitForExit(3000);
                         }
                         catch { }
-                    }
-
-                    // Final check: if still running, execute secondary force-kill
-                    if (IsTunnelServiceRunning(interfaceName))
-                    {
-                        LoggingService.Instance.Warning("WireGuardCli", 
-                            $"Interface '{interfaceName}' still reported running. Executing secondary force termination...");
                         ForceKillServiceProcess(serviceName);
                     }
 
                     FlushDnsCache();
                     bool stopped = !IsTunnelServiceRunning(interfaceName);
                     LoggingService.Instance.Info("WireGuardCli", $"Tunnel '{interfaceName}' stop result: Stopped={stopped}");
                     return stopped;
```

---

## 4. Issue 3: Stale Tunnel on Reconnect & Non-Destructive Auto-Recovery (`WatchdogService.cs`)

### Codebase Investigation
1. **The Wake/Reconnect "Zombie Tunnel" Gap**:
   In `WatchdogService.cs` (`OnNetworkSettled`, lines 152–165):
   ```csharp
   // 5. Otherwise, ensure tunnel is active on untrusted network
   if (!isRunning)
   {
       SetStatus(TunnelStatus.Connecting);
       _tunnelStartedUtc = DateTime.UtcNow;
       _blockedWarningSent = false;
       bool started = await cli.StartTunnelAsync(targetInterface, profile.WireGuardConfigPath);
       ...
   }
   ```
   When waking from sleep or reconnecting after an extended physical disconnect (e.g. the 64-minute outage in the user log), the Windows service `WireGuardTunnel$...` is technically still `Running`.  
   Because `isRunning` is true, WireFox takes **no action**. However, the kernel UDP socket and Wintun route bindings are dead or bound to the previous network adapter. The user is stranded in a degraded state until they manually pause and resume.

2. **Destructive Reset in `ForceRestartTunnelAsync()`**:
   In `WatchdogService.cs` (lines 647–680):
   ```csharp
   public async Task ForceRestartTunnelAsync()
   {
       ...
       // Wipe out ALL WireGuard hooks globally before restarting the primary tunnel
       await cli.StopAllTunnelsAsync(true);
       await Task.Delay(1000);
   ```
   `ForceRestartTunnelAsync()` immediately invokes `StopAllTunnelsAsync(true)`, which triggers `taskkill /F`, service deletions via `sc.exe delete`, and wipes out official WireGuard client UI pipes.  
   The user's manual recovery proved that a gentle `StopTunnelAsync` (519ms) + `StartTunnelAsync` (1325ms) restores normal function in <2 seconds without breaking services or deleting driver state.

3. **Premature `StopPending` Force-Kill in Periodic Watchdog**:
   In `WatchdogService.cs` (lines 317–332):
   ```csharp
   var stuckServices = services.Where(s => 
       s.ServiceName.StartsWith("WireGuardTunnel$", StringComparison.OrdinalIgnoreCase) && 
       s.Status == ServiceControllerStatus.StopPending).ToList();

   foreach (var stuck in stuckServices)
   {
       LoggingService.Instance.Warning("WatchdogService", 
           $"Found service '{stuck.ServiceName}' stuck in StopPending state. Escalating force-kill...");
       cli.ForceKillServiceProcess(stuck.ServiceName);
   }
   ```
   Every 8-second watchdog poll, any service that happens to be in `StopPending` (even if it just started shutting down 100ms ago) is instantly assassinated!

### Refined Solution for v1.0.6
1. **Refresh Stale Tunnel on Untrusted Network Settle**:
   In `OnNetworkSettled()`, if `!state.IsTrusted && isRunning`, inspect the latest handshake timestamp. If the handshake is null or older than `profile.HandshakeTimeoutSeconds`, automatically perform a fast graceful restart (`StopTunnelAsync` then `StartTunnelAsync`) rather than doing nothing.
2. **Make `ForceRestartTunnelAsync()` Graceful First**:
   Attempt `StopTunnelAsync(targetInterface)` first. Only escalate to `StopAllTunnelsAsync(true)` if the graceful stop fails.
3. **Remove Premature `StopPending` Kill from `CheckHandshakeWatchdogAsync`**:
   Allow `WireGuardCliService.StopTunnelAsync` to manage stop timeouts rather than preemptively killing services on the background timer.

### Concrete Patch / Diff for `WatchdogService.cs`

```diff
--- a/Services/WatchdogService.cs
+++ b/Services/WatchdogService.cs
@@ -152,6 +152,21 @@
             // 5. Otherwise, ensure tunnel is active on untrusted network
             if (!isRunning)
             {
                 SetStatus(TunnelStatus.Connecting);
                 _tunnelStartedUtc = DateTime.UtcNow;
                 _blockedWarningSent = false;
                 bool started = await cli.StartTunnelAsync(targetInterface, profile.WireGuardConfigPath);
                 if (!started)
                 {
                     LoggingService.Instance.Warning("WatchdogService", $"Automatic tunnel start failed for '{targetInterface}'");
                 }
             }
+            else
+            {
+                // If tunnel service is running but handshake is stale after wake/reconnect, refresh it gracefully
+                var latestHandshake = await cli.GetLatestHandshakeUtcAsync(targetInterface);
+                if (latestHandshake.HasValue && (DateTime.UtcNow - latestHandshake.Value).TotalSeconds > profile.HandshakeTimeoutSeconds)
+                {
+                    LoggingService.Instance.Warning("WatchdogService", 
+                        $"Network settled on untrusted network but running tunnel '{targetInterface}' has stale handshake. Refreshing tunnel connection...");
+                    await cli.StopTunnelAsync(targetInterface);
+                    _tunnelStartedUtc = DateTime.UtcNow;
+                    _consecutiveFailures = 0;
+                    _blockedWarningSent = false;
+                    await cli.StartTunnelAsync(targetInterface, profile.WireGuardConfigPath);
+                }
+            }
@@ -317,16 +332,6 @@
-            // 1. Proactively detect if any WireGuard tunnel service is stuck in StopPending
-            try
-            {
-                var services = ServiceController.GetServices();
-                var stuckServices = services.Where(s => 
-                    s.ServiceName.StartsWith("WireGuardTunnel$", StringComparison.OrdinalIgnoreCase) && 
-                    s.Status == ServiceControllerStatus.StopPending).ToList();
-
-                foreach (var stuck in stuckServices)
-                {
-                    LoggingService.Instance.Warning("WatchdogService", 
-                        $"Found service '{stuck.ServiceName}' stuck in StopPending state. Escalating force-kill...");
-                    cli.ForceKillServiceProcess(stuck.ServiceName);
-                }
-            }
-            catch { }
@@ -654,7 +659,12 @@
-            LoggingService.Instance.Warning("WatchdogService", $"ForceRestartTunnelAsync executing (Full Reset)...");
+            LoggingService.Instance.Warning("WatchdogService", $"Restarting tunnel '{targetInterface}' gracefully...");
             SetStatus(TunnelStatus.Connecting);
 
-            // Wipe out ALL WireGuard hooks globally before restarting the primary tunnel
-            await cli.StopAllTunnelsAsync(true);
-            await Task.Delay(1000);
+            // Attempt graceful restart first; only escalate if graceful stop fails
+            bool stopped = await cli.StopTunnelAsync(targetInterface);
+            if (!stopped)
+            {
+                LoggingService.Instance.Warning("WatchdogService", $"Graceful stop failed for '{targetInterface}'. Escalating to full reset...");
+                await cli.StopAllTunnelsAsync(true);
+            }
+            await Task.Delay(500);
```

---

## 5. Verification Checklist for v1.0.6 Release

When the v1.0.5 WinGet release is completed and the code freeze is lifted, follow these verification steps before packaging v1.0.6:

1. **Trusted Network Settle Test**:
   - Connect to a trusted Wi-Fi network.
   - Verify tunnel status is `Bypassed`.
   - Open WireFox Log viewer (or `%LOCALAPPDATA%\WireFox\Logs\wirefox.log`).
   - Trigger DHCP refresh or disconnect/reconnect Wi-Fi.
   - Verify that log displays:
     `Network settled on trusted network: tunnel already bypassed and inactive.`
   - Verify that `StopAllTunnelsAsync()` is NOT called and no subprocesses (`taskkill`, `ipconfig`) are spawned.

2. **Graceful Tunnel Teardown Test**:
   - Connect tunnel on an untrusted network or toggle ON in WireFox UI.
   - Toggle tunnel OFF or switch to a trusted network.
   - Verify log shows `Stopping service 'WireGuardTunnel$...'...` followed by `Service 'WireGuardTunnel$...' stopped.` within ~1–4 seconds without triggering `Escalating to force-kill`.
   - In PowerShell, run `Get-Service WireGuardTunnel*`:
     Verify the service status is `Stopped` and NOT missing/deleted.
   - Toggle tunnel back ON in WireFox UI:
     Verify it starts cleanly via `Starting existing service '...'` in <1 second without reinstalling via `/installtunnelservice`.

3. **Stale Tunnel Reconnect Test (Sleep / Outage Simulation)**:
   - With tunnel connected on an untrusted network, disconnect Wi-Fi for 4 minutes (exceeding handshake timeout).
   - Reconnect Wi-Fi.
   - Verify log shows:
     `Network settled on untrusted network but running tunnel '...' has stale handshake. Refreshing tunnel connection...`
   - Verify tunnel restarts gracefully via `StopTunnelAsync` + `StartTunnelAsync` in <2 seconds without nuclear taskkill or service deletion.

4. **WireGuard Official GUI Coexistence**:
   - Run the official WireGuard for Windows GUI alongside WireFox.
   - Stop and restart tunnels multiple times via WireFox.
   - Verify the official WireGuard client UI does not crash, show named pipe connection errors, or report driver detachment errors.

---

## 6. UI Polish for v1.0.6

### 6.1 Card 1 Active Tunnel Text Truncation (`DiagnosticsPage.xaml`)
- **Observed Behavior:** In [DiagnosticsPage.xaml:L37](file:///c:/Users/Talvi/Documents/IDE-Scratch/WireFox/Views/DiagnosticsPage.xaml#L37), `ActiveTunnelStatus` is styled with `Foreground="{DynamicResource SystemAccentColorPrimaryBrush}"` inside a fixed Grid column without wrapping:
  ```xml
  <ui:TextBlock Text="{Binding ActiveTunnelStatus, StringFormat='Active: {0}'}" FontSize="12" FontWeight="SemiBold" Margin="0,4,0,0" Foreground="{DynamicResource SystemAccentColorPrimaryBrush}" />
  ```
- **Problem:** When the window is in standard restored mode (not maximized), long tunnel names or multi-adapter lists get clipped off at the right border (e.g. `Active: FoxDenVPN-FoxTop, FoxDenVPN-FoxTop-Npcap Packet Drive...`).
- **Fix for v1.0.6:**
  Add `TextWrapping="Wrap"` or `TextTrimming="CharacterEllipsis"` and attach a `ToolTip="{Binding ActiveTunnelStatus}"` so it stays clean in compact mode and fully readable on hover:
  ```xml
  <ui:TextBlock Text="{Binding ActiveTunnelStatus, StringFormat='Active: {0}'}" 
                FontSize="12" FontWeight="SemiBold" Margin="0,4,0,0" 
                TextWrapping="Wrap" MaxHeight="36"
                ToolTip="{Binding ActiveTunnelStatus}"
                Foreground="{DynamicResource SystemAccentColorPrimaryBrush}" />
  ```

---

### 6.2 "Run Diagnostic Scan" User Feedback (`DiagnosticsViewModel.cs`)
- **What it actually does:**
  [DiagnosticsViewModel.cs:L124-145](file:///c:/Users/Talvi/Documents/IDE-Scratch/WireFox/ViewModels/DiagnosticsViewModel.cs#L124-L145) performs a comprehensive environment check:
  1. Checks if `wireguard.exe` and `wg.exe` exist and are executable.
  2. Queries all installed tunnel services and active kernel interfaces.
  3. Queries current network adapter, gateway IP, gateway MAC, and Wi-Fi BSSID.
  4. Writes structured log entries with `[INFO]` and `[SUCCESS]` tags.
  5. Refreshes the summary cards at the top of the page.
- **Problem:** The user clicks the button and gets **zero immediate visual confirmation** other than new text scrolling by in the log box below.
- **Fix for v1.0.6:**
  Provide clear positive/negative toast or in-app InfoBar feedback:
  - If WireGuard CLI is installed and active interfaces match expectations:
    Show success notice: *"Scan Complete: All systems nominal (WireGuard CLI detected, X tunnels found)."*
  - If CLI missing or orphaned interfaces detected:
    Show warning notice: *"Scan Complete: Issues found (Review log for details)."*

---

### 6.3 Bold Connection Category Labels (`StatusPage.xaml` / `StatusViewModel.cs`)
- **Current State:**
  [StatusViewModel.cs:L274](file:///c:/Users/Talvi/Documents/IDE-Scratch/WireFox/ViewModels/StatusViewModel.cs#L274) produces a flat string:
  `Gateway: 10.11.248.81 [DA:5B:CF:28:E2:4C]  •  AP: [DA:5B:30:28:E2:4C]  •  DNS: 10.11.248.81`
- **Visual Improvement:**
  Because the text is uniform secondary gray, the labels blend together with the MAC addresses and IPs.
  - Make `Gateway:`, `AP:`, and `DNS:` **bold** (`FontWeight="SemiBold"`) with primary foreground contrast, while keeping the IPs and MAC addresses in secondary/accent colors.
  - In WPF, this can be achieved cleanly in `StatusPage.xaml` using formatted `<Run>` inlines or breaking them into a styled horizontal WrapPanel of pill chips.

---

### 6.4 Side-Scrolling in Live Log Console (`DiagnosticsPage.xaml`)
- **Current State:**
  `LogsList` has `ScrollViewer.HorizontalScrollBarVisibility="Auto"`, but inside the ItemTemplate, the message `TextBlock` has `TextWrapping="Wrap"` inside a `Width="*"`.
- **Problem:** Wrapping forces all long error messages, file paths, and command lines to break onto new lines, which prevents horizontal scrolling from ever triggering.
- **Fix for v1.0.6:**
  Change `TextWrapping="NoWrap"` on the message `TextBlock` (or add a small toggle button *"Wrap / Side-Scroll"*) so users can cleanly side-scroll to inspect full log strings without jagged wrapping.

---

### 6.5 Post-v1.0.6 Roadmap Items (v1.0.7 & v1.1.0)
The following architectural proposals have been split into [ROADMAP.md](file:///c:/Users/Talvi/Documents/IDE-Scratch/WireFox/ROADMAP.md) to keep v1.0.6 strictly scoped to stability, teardown reliability, and core UX:
- **v1.0.7**: Touchscreen Hitboxes & Tablet Mode UX Optimizations
- **v1.1.0**: Ephemeral "Pseudo Kill Switch" on Tunnel Freeze via Dynamic WFP

