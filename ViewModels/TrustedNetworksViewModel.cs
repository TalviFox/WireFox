using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using WireFox.Models;
using WireFox.Services;

namespace WireFox.ViewModels
{
    public class TrustedNetworksViewModel : ViewModelBase
    {
        private string _newNetworkSsid = string.Empty;
        private string _newGatewayName = string.Empty;
        private string _newGatewayIp = string.Empty;
        private string _newGatewayMac = string.Empty;
        private string _newStackedName = string.Empty;
        private readonly System.Windows.Threading.Dispatcher _dispatcher;

        public ObservableCollection<string> TrustedNetworks { get; } = new();
        public ObservableCollection<TrustedGateway> TrustedGateways { get; } = new();

        public string NewNetworkSsid
        {
            get => _newNetworkSsid;
            set => SetProperty(ref _newNetworkSsid, value);
        }

        public string NewGatewayName
        {
            get => _newGatewayName;
            set => SetProperty(ref _newGatewayName, value);
        }

        public string NewGatewayIp
        {
            get => _newGatewayIp;
            set => SetProperty(ref _newGatewayIp, value);
        }

        public string NewGatewayMac
        {
            get => _newGatewayMac;
            set => SetProperty(ref _newGatewayMac, value);
        }

        public string NewStackedName
        {
            get => _newStackedName;
            set => SetProperty(ref _newStackedName, value);
        }

        public ICommand AddNetworkCommand { get; }
        public ICommand RemoveNetworkCommand { get; }
        public ICommand AddGatewayCommand { get; }
        public ICommand RemoveGatewayCommand { get; }
        public ICommand QuickFillDetectedCommand { get; }
        public ICommand UpdateGatewayMacFromActiveCommand { get; }
        public ICommand AddStackedNameToSelectedCommand { get; }
        public ICommand RemoveStackedNameCommand { get; }

        public TrustedNetworksViewModel()
        {
            _dispatcher = System.Windows.Application.Current?.Dispatcher ?? System.Windows.Threading.Dispatcher.CurrentDispatcher;

            AddNetworkCommand = new RelayCommand(OnAddNetwork);
            RemoveNetworkCommand = new RelayCommand(OnRemoveNetwork);
            AddGatewayCommand = new RelayCommand(OnAddGateway);
            RemoveGatewayCommand = new RelayCommand(OnRemoveGateway);
            QuickFillDetectedCommand = new RelayCommand(OnQuickFillDetected);
            UpdateGatewayMacFromActiveCommand = new RelayCommand(OnUpdateGatewayMacFromActive);
            AddStackedNameToSelectedCommand = new RelayCommand(OnAddStackedName);
            RemoveStackedNameCommand = new RelayCommand(OnRemoveStackedName);

            ConfigManager.Instance.ConfigChanged += OnConfigChanged;
            LoadData();
        }

        private void LoadData()
        {
            var config = ConfigManager.Instance.Config;
            TrustedNetworks.Clear();
            foreach (var ssid in config.TrustedNetworks)
            {
                TrustedNetworks.Add(ssid);
            }

            TrustedGateways.Clear();
            foreach (var gw in config.TrustedGateways)
            {
                TrustedGateways.Add(gw);
            }
        }

        private void OnConfigChanged(AppConfig config)
        {
            _dispatcher.Invoke(() =>
            {
                TrustedNetworks.Clear();
                foreach (var ssid in config.TrustedNetworks)
                {
                    TrustedNetworks.Add(ssid);
                }

                TrustedGateways.Clear();
                foreach (var gw in config.TrustedGateways)
                {
                    TrustedGateways.Add(gw);
                }
            });
        }

        private void OnAddNetwork()
        {
            if (string.IsNullOrWhiteSpace(NewNetworkSsid)) return;

            string ssidToAdd = NewNetworkSsid.Trim();
            if (ConfigManager.Instance.AddTrustedNetwork(ssidToAdd))
            {
                NewNetworkSsid = string.Empty;
            }
        }

        private void OnRemoveNetwork(object? parameter)
        {
            if (parameter is string ssid)
            {
                ConfigManager.Instance.RemoveTrustedNetwork(ssid);
            }
        }

        private void OnAddGateway()
        {
            if (string.IsNullOrWhiteSpace(NewGatewayIp)) return;

            var gw = new TrustedGateway
            {
                Name = string.IsNullOrWhiteSpace(NewGatewayName) ? $"Gateway {NewGatewayIp.Trim()}" : NewGatewayName.Trim(),
                IpAddress = NewGatewayIp.Trim(),
                MacAddress = NewGatewayMac?.Trim() ?? string.Empty
            };

            if (!string.IsNullOrWhiteSpace(NewStackedName))
            {
                gw.StackedNames.Add(NewStackedName.Trim());
            }

            if (ConfigManager.Instance.AddOrUpdateTrustedGateway(gw))
            {
                NewGatewayName = string.Empty;
                NewGatewayIp = string.Empty;
                NewGatewayMac = string.Empty;
                NewStackedName = string.Empty;
            }
        }

        private void OnRemoveGateway(object? parameter)
        {
            if (parameter is TrustedGateway gw)
            {
                ConfigManager.Instance.RemoveTrustedGateway(gw.IpAddress);
            }
            else if (parameter is string ip)
            {
                ConfigManager.Instance.RemoveTrustedGateway(ip);
            }
        }

        private void OnQuickFillDetected()
        {
            var netState = NetworkMonitorService.Instance.CurrentState;
            if (!string.IsNullOrEmpty(netState.GatewayIp))
            {
                NewGatewayIp = netState.GatewayIp;
                NewGatewayMac = netState.GatewayMac ?? string.Empty;
                NewGatewayName = netState.DnsServerName ?? netState.Ssid ?? $"Gateway {netState.GatewayIp}";
                if (netState.DetectedNames.Count > 0)
                {
                    NewStackedName = netState.DetectedNames.First();
                }
            }
        }

        private void OnUpdateGatewayMacFromActive(object? parameter)
        {
            if (parameter is TrustedGateway gw)
            {
                var netState = NetworkMonitorService.Instance.CurrentState;
                if (!string.IsNullOrEmpty(netState.GatewayMac) && string.Equals(netState.GatewayIp, gw.IpAddress, StringComparison.OrdinalIgnoreCase))
                {
                    ConfigManager.Instance.UpdateGatewayMac(gw.IpAddress, netState.GatewayMac);
                }
            }
        }

        private void OnAddStackedName(object? parameter)
        {
            if (parameter is TrustedGateway gw && !string.IsNullOrWhiteSpace(NewStackedName))
            {
                ConfigManager.Instance.AddStackedNameToGateway(gw.IpAddress, NewStackedName.Trim());
                NewStackedName = string.Empty;
            }
        }

        private void OnRemoveStackedName(object? parameter)
        {
            if (parameter is (TrustedGateway gw, string name))
            {
                ConfigManager.Instance.RemoveStackedNameFromGateway(gw.IpAddress, name);
            }
        }
    }
}
