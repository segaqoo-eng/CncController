using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CncController.Models;
using CncController.Services;

namespace CncController.ViewModels
{
    public partial class HardwareDiscoveryViewModel : ObservableObject
    {
        // ★★★ 修正重點：改用 Singleton Instance，不能用 new() ★★★
        private readonly HardwareScanService _scanService = HardwareScanService.Instance;

        [ObservableProperty]
        private string _statusMessage = "Ready to scan.";

        [ObservableProperty]
        private bool _isScanning;

        public ObservableCollection<DiscoveredSlave> Slaves { get; } = new();

        [RelayCommand]
        private async Task StartScan()
        {
            if (IsScanning) return;
            IsScanning = true;
            StatusMessage = "Scanning EtherCAT bus...";
            Slaves.Clear();

            // 這裡呼叫 Service 的掃描方法
            var results = await _scanService.ScanAsync();

            foreach (var item in results)
            {
                Slaves.Add(item);
            }

            StatusMessage = $"Scan complete. Found {Slaves.Count} devices.";
            IsScanning = false;
        }
    }
}