using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using WireFox.Models;

namespace WireFox.Services
{
    public class ConfigManager
    {
        private static readonly Lazy<ConfigManager> _instance = new(() => new ConfigManager());
        public static ConfigManager Instance => _instance.Value;

        private readonly object _lock = new();
        private readonly string _configFilePath;
        private AppConfig _currentConfig;

        public event Action<AppConfig>? ConfigChanged;

        private bool _isFirstRun;
        public bool IsFirstRun => _isFirstRun;

        private ConfigManager()
        {
            bool configExistsOnDisk = false;
            try
            {
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string appFolder = Path.Combine(localAppData, "WireFox");
                Directory.CreateDirectory(appFolder);
                _configFilePath = Path.Combine(appFolder, "config.json");

                string roamingAppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string roamingConfigPath = Path.Combine(roamingAppData, "WireFox", "config.json");
                string legacyLocalWG = Path.Combine(localAppData, "WGManager", "config.json");
                string legacyRoamingWG = Path.Combine(roamingAppData, "WGManager", "config.json");
                string legacyBaseDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");

                // Check if current config exists, else migrate from previous versions
                if (File.Exists(_configFilePath))
                {
                    configExistsOnDisk = true;
                }
                else
                {
                    string[] migrationSources = { legacyLocalWG, roamingConfigPath, legacyRoamingWG, legacyBaseDir };
                    foreach (var src in migrationSources)
                    {
                        if (File.Exists(src))
                        {
                            try
                            {
                                File.Copy(src, _configFilePath, true);
                                configExistsOnDisk = true;
                                break;
                            }
                            catch { }
                        }
                    }
                }
            }
            catch
            {
                _configFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
                configExistsOnDisk = File.Exists(_configFilePath);
            }

            _isFirstRun = !configExistsOnDisk;
            _currentConfig = LoadOrCreate();

            if (!configExistsOnDisk)
            {
                _currentConfig.InitialSetupCompleted = false;
            }
            else
            {
                _currentConfig.InitialSetupCompleted = true;
            }
        }

        public AppConfig Config
        {
            get
            {
                lock (_lock)
                {
                    return _currentConfig;
                }
            }
        }

        public AppConfig LoadOrCreate()
        {
            lock (_lock)
            {
                try
                {
                    if (File.Exists(_configFilePath))
                    {
                        string json = File.ReadAllText(_configFilePath);
                        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                        var loaded = JsonSerializer.Deserialize<AppConfig>(json, options);
                        if (loaded != null)
                        {
                            loaded.Tunnels ??= new(StringComparer.OrdinalIgnoreCase);
                            if (!string.IsNullOrWhiteSpace(loaded.WireGuardInterface) && !loaded.Tunnels.ContainsKey(loaded.WireGuardInterface))
                            {
                                // Migrate legacy settings to this profile
                                loaded.GetProfile(loaded.WireGuardInterface);
                            }

                            _currentConfig = loaded;
                            return _currentConfig;
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[ConfigManager] Failed to load config, fallback to default: {ex.Message}");
                }

                _currentConfig = new AppConfig();
                Save(_currentConfig);
                return _currentConfig;
            }
        }

        public void Save(AppConfig config)
        {
            lock (_lock)
            {
                try
                {
                    _currentConfig = config;
                    var options = new JsonSerializerOptions { WriteIndented = true };
                    string json = JsonSerializer.Serialize(config, options);
                    
                    string tempPath = _configFilePath + ".tmp";
                    File.WriteAllText(tempPath, json);
                    
                    if (File.Exists(_configFilePath))
                    {
                        File.Replace(tempPath, _configFilePath, null);
                    }
                    else
                    {
                        File.Move(tempPath, _configFilePath);
                    }

                    ConfigChanged?.Invoke(_currentConfig);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[ConfigManager] Failed to save config: {ex.Message}");
                }
            }
        }

        public void SaveConfig()
        {
            Save(_currentConfig);
        }

        public bool AddTrustedNetwork(string ssid)
        {
            if (string.IsNullOrWhiteSpace(ssid)) return false;

            lock (_lock)
            {
                if (!_currentConfig.TrustedNetworks.Contains(ssid, StringComparer.OrdinalIgnoreCase))
                {
                    _currentConfig.TrustedNetworks.Add(ssid);
                    _currentConfig.KnownUntrustedNetworks.RemoveAll(s => string.Equals(s, ssid, StringComparison.OrdinalIgnoreCase));
                    Save(_currentConfig);
                    return true;
                }
                return false;
            }
        }

        public bool RemoveTrustedNetwork(string ssid)
        {
            if (string.IsNullOrWhiteSpace(ssid)) return false;

            lock (_lock)
            {
                int removed = _currentConfig.TrustedNetworks.RemoveAll(s => string.Equals(s, ssid, StringComparison.OrdinalIgnoreCase));

                // Also clean up from stacked names in trusted gateways without destroying the physical gateway entry
                foreach (var g in _currentConfig.TrustedGateways)
                {
                    int remStacked = g.StackedNames.RemoveAll(s => string.Equals(s, ssid, StringComparison.OrdinalIgnoreCase));
                    if (remStacked > 0) removed += remStacked;

                    if (string.Equals(g.Name, ssid, StringComparison.OrdinalIgnoreCase))
                    {
                        if (g.StackedNames.Count > 0)
                        {
                            g.Name = g.StackedNames[0];
                            g.StackedNames.RemoveAt(0);
                            removed++;
                        }
                        else
                        {
                            g.Name = $"Gateway {g.IpAddress}";
                            removed++;
                        }
                    }
                }

                if (removed > 0)
                {
                    if (!_currentConfig.KnownUntrustedNetworks.Contains(ssid, StringComparer.OrdinalIgnoreCase))
                    {
                        _currentConfig.KnownUntrustedNetworks.Add(ssid);
                    }
                    Save(_currentConfig);
                    return true;
                }
                return false;
            }
        }

        public bool UntrustNetwork(string? ssid, string? gatewayIp = null, string? gatewayMac = null)
        {
            lock (_lock)
            {
                bool changed = false;

                if (!string.IsNullOrWhiteSpace(ssid))
                {
                    int remNet = _currentConfig.TrustedNetworks.RemoveAll(s => string.Equals(s, ssid, StringComparison.OrdinalIgnoreCase));
                    if (remNet > 0) changed = true;

                    foreach (var g in _currentConfig.TrustedGateways)
                    {
                        int remStacked = g.StackedNames.RemoveAll(s => string.Equals(s, ssid, StringComparison.OrdinalIgnoreCase));
                        if (remStacked > 0) changed = true;

                        if (string.Equals(g.Name, ssid, StringComparison.OrdinalIgnoreCase))
                        {
                            if (g.StackedNames.Count > 0)
                            {
                                g.Name = g.StackedNames[0];
                                g.StackedNames.RemoveAt(0);
                                changed = true;
                            }
                            else
                            {
                                g.Name = $"Gateway {g.IpAddress}";
                                changed = true;
                            }
                        }
                    }

                    if (!_currentConfig.KnownUntrustedNetworks.Contains(ssid, StringComparer.OrdinalIgnoreCase))
                    {
                        _currentConfig.KnownUntrustedNetworks.Add(ssid);
                        changed = true;
                    }
                }

                // Only remove the entire gateway if there is no specific SSID being untrusted, or gateway explicitly untrusted
                if (string.IsNullOrWhiteSpace(ssid) && !string.IsNullOrWhiteSpace(gatewayIp))
                {
                    int remIp = _currentConfig.TrustedGateways.RemoveAll(g => 
                        string.Equals(g.IpAddress, gatewayIp, StringComparison.OrdinalIgnoreCase) && 
                        (string.IsNullOrWhiteSpace(gatewayMac) || string.Equals(g.MacAddress, gatewayMac, StringComparison.OrdinalIgnoreCase)));
                    if (remIp > 0) changed = true;
                }

                if (changed)
                {
                    Save(_currentConfig);
                }
                return changed;
            }
        }

        public bool AutoRegisterTrustedSsid(string ssid, string? gatewayIp, string? gatewayMac)
        {
            if (string.IsNullOrWhiteSpace(ssid)) return false;

            lock (_lock)
            {
                if (_currentConfig.KnownUntrustedNetworks.Contains(ssid, StringComparer.OrdinalIgnoreCase))
                    return false;

                bool changed = false;
                if (!_currentConfig.TrustedNetworks.Contains(ssid, StringComparer.OrdinalIgnoreCase))
                {
                    _currentConfig.TrustedNetworks.Add(ssid);
                    changed = true;
                }

                var gw = _currentConfig.TrustedGateways.FirstOrDefault(g =>
                    (!string.IsNullOrWhiteSpace(gatewayMac) && !string.IsNullOrWhiteSpace(g.MacAddress) &&
                     string.Equals(g.MacAddress, gatewayMac, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrWhiteSpace(gatewayIp) && string.Equals(g.IpAddress, gatewayIp, StringComparison.OrdinalIgnoreCase)));

                if (gw != null)
                {
                    if (!string.Equals(gw.Name, ssid, StringComparison.OrdinalIgnoreCase) &&
                        !gw.StackedNames.Contains(ssid, StringComparer.OrdinalIgnoreCase))
                    {
                        gw.StackedNames.Add(ssid);
                        changed = true;
                    }
                    if (!string.IsNullOrWhiteSpace(gatewayIp) && !string.Equals(gw.IpAddress, gatewayIp, StringComparison.OrdinalIgnoreCase))
                    {
                        gw.IpAddress = gatewayIp;
                        changed = true;
                    }
                    if (!string.IsNullOrWhiteSpace(gatewayMac) && !string.Equals(gw.MacAddress, gatewayMac, StringComparison.OrdinalIgnoreCase))
                    {
                        gw.MacAddress = gatewayMac;
                        changed = true;
                    }
                }

                if (changed)
                {
                    Save(_currentConfig);
                }
                return changed;
            }
        }

        public bool AddOrUpdateTrustedGateway(TrustedGateway gateway)
        {
            if (string.IsNullOrWhiteSpace(gateway.IpAddress)) return false;

            lock (_lock)
            {
                var existing = _currentConfig.TrustedGateways.FirstOrDefault(g => 
                    (!string.IsNullOrWhiteSpace(gateway.MacAddress) && !string.IsNullOrWhiteSpace(g.MacAddress) &&
                     string.Equals(g.MacAddress, gateway.MacAddress, StringComparison.OrdinalIgnoreCase)) ||
                    (string.Equals(g.IpAddress, gateway.IpAddress, StringComparison.OrdinalIgnoreCase) && 
                     (string.IsNullOrWhiteSpace(gateway.MacAddress) || string.IsNullOrWhiteSpace(g.MacAddress) ||
                      string.Equals(g.MacAddress, gateway.MacAddress, StringComparison.OrdinalIgnoreCase))));
                if (existing != null)
                {
                    if (!string.IsNullOrWhiteSpace(gateway.Name))
                    {
                        if (string.IsNullOrWhiteSpace(existing.Name) || existing.Name.StartsWith("Gateway "))
                        {
                            existing.Name = gateway.Name;
                        }
                        else if (!string.Equals(existing.Name, gateway.Name, StringComparison.OrdinalIgnoreCase))
                        {
                            if (!existing.StackedNames.Contains(gateway.Name, StringComparer.OrdinalIgnoreCase))
                            {
                                existing.StackedNames.Add(gateway.Name);
                            }
                        }
                    }
                    
                    if (!string.IsNullOrWhiteSpace(gateway.MacAddress)) existing.MacAddress = gateway.MacAddress;
                    if (!string.IsNullOrWhiteSpace(gateway.IpAddress)) existing.IpAddress = gateway.IpAddress;
                    
                    foreach (var name in gateway.StackedNames)
                    {
                        if (!string.Equals(existing.Name, name, StringComparison.OrdinalIgnoreCase) &&
                            !existing.StackedNames.Contains(name, StringComparer.OrdinalIgnoreCase))
                        {
                            existing.StackedNames.Add(name);
                        }
                    }
                }
                else
                {
                    _currentConfig.TrustedGateways.Add(gateway);
                }

                Save(_currentConfig);
                return true;
            }
        }

        public bool UpdateGatewayMac(string ip, string oldMac, string newMac)
        {
            if (string.IsNullOrWhiteSpace(ip) || string.IsNullOrWhiteSpace(newMac)) return false;

            lock (_lock)
            {
                var existing = _currentConfig.TrustedGateways.FirstOrDefault(g => 
                    string.Equals(g.IpAddress, ip, StringComparison.OrdinalIgnoreCase) && 
                    string.Equals(g.MacAddress, oldMac, StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                {
                    existing.MacAddress = newMac.Trim();
                    Save(_currentConfig);
                    return true;
                }
                return false;
            }
        }

        public bool AddStackedNameToGateway(string ip, string mac, string networkName)
        {
            if (string.IsNullOrWhiteSpace(ip) || string.IsNullOrWhiteSpace(networkName)) return false;

            lock (_lock)
            {
                var existing = _currentConfig.TrustedGateways.FirstOrDefault(g => 
                    string.Equals(g.IpAddress, ip, StringComparison.OrdinalIgnoreCase) && 
                    string.Equals(g.MacAddress, mac, StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                {
                    if (!existing.StackedNames.Contains(networkName, StringComparer.OrdinalIgnoreCase))
                    {
                        existing.StackedNames.Add(networkName.Trim());
                        Save(_currentConfig);
                        return true;
                    }
                }
                return false;
            }
        }

        public bool RemoveStackedNameFromGateway(string ip, string mac, string networkName)
        {
            if (string.IsNullOrWhiteSpace(ip) || string.IsNullOrWhiteSpace(networkName)) return false;

            lock (_lock)
            {
                var existing = _currentConfig.TrustedGateways.FirstOrDefault(g => 
                    string.Equals(g.IpAddress, ip, StringComparison.OrdinalIgnoreCase) && 
                    string.Equals(g.MacAddress, mac, StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                {
                    int removed = existing.StackedNames.RemoveAll(s => string.Equals(s, networkName, StringComparison.OrdinalIgnoreCase));
                    if (removed > 0)
                    {
                        Save(_currentConfig);
                        return true;
                    }
                }
                return false;
            }
        }

        public bool RemoveTrustedGateway(string ip, string macAddress)
        {
            if (string.IsNullOrWhiteSpace(ip)) return false;

            lock (_lock)
            {
                int removed = _currentConfig.TrustedGateways.RemoveAll(g => 
                    string.Equals(g.IpAddress, ip, StringComparison.OrdinalIgnoreCase) && 
                    string.Equals(g.MacAddress, macAddress, StringComparison.OrdinalIgnoreCase));
                if (removed > 0)
                {
                    Save(_currentConfig);
                    return true;
                }
                return false;
            }
        }

        public void MarkNetworkAsKnownUntrusted(string ssid)
        {
            if (string.IsNullOrWhiteSpace(ssid)) return;

            lock (_lock)
            {
                if (!_currentConfig.KnownUntrustedNetworks.Contains(ssid, StringComparer.OrdinalIgnoreCase) &&
                    !_currentConfig.TrustedNetworks.Contains(ssid, StringComparer.OrdinalIgnoreCase))
                {
                    _currentConfig.KnownUntrustedNetworks.Add(ssid);
                    Save(_currentConfig);
                }
            }
        }
    }
}
