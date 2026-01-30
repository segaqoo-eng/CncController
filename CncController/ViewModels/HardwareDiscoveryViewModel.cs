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
        // ★★★ 修正：改用 Singleton Instance ★★★
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
            System.Diagnostics.Debug.WriteLine("Scan StartScan_1");
            var results = await _scanService.ScanAsync();
            System.Diagnostics.Debug.WriteLine("Scan StartScan_2");
            foreach (var item in results)
            {
                // 1. 印出詳細資料到輸出視窗
                System.Diagnostics.Debug.WriteLine($"[Add] Index: {item.Index}, Name: {item.Name}, VID: {item.VendorId}");
                Slaves.Add(item);
            }
            StatusMessage = $"Scan complete. Found {Slaves.Count} devices.";
            IsScanning = false;
        }
    }
}