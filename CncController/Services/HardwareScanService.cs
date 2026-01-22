using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using CncController.Models;

namespace CncController.Services
{
    public class HardwareScanService
    {
        public static HardwareScanService Instance { get; } = new HardwareScanService();

        private readonly HttpClient _http;

        // ★★★ 修正模擬資料：完全對應 CSV 欄位 ★★★
        private readonly List<DiscoveredSlave> _simulatedSlaves = new()
        {
            new DiscoveredSlave {
                Index = 0,
                VendorId = "0x00100000",
                ProductCode = "0x000c010d",
                Name = "SV660_1Axis_00916",
                VendorGroup = "InoServo",
                ProductModel = "InoSV660N",
                Category = "Servo",
                Source = "XML (SV660.xml)",
                Pdos = "6040:00, 607a:00..."
            },
            new DiscoveredSlave {
                Index = 1,
                VendorId = "0x000001dd",
                ProductCode = "0x00005500",
                Name = "R1-EC5500",
                VendorGroup = "SystemBk",
                ProductModel = "R1-EC5500",
                Category = "Coupler",
                Source = "Fallback",
                Pdos = "None"
            },
            new DiscoveredSlave {
                Index = 2,
                VendorId = "0x000001dd",
                ProductCode = "0x00005621",
                Name = "R1-EC5621",
                VendorGroup = "Axis",
                ProductModel = "R1-EC5621",
                Category = "Servo",
                Source = "XML (Delta.xml)",
                Pdos = "6040:00, 607a:00..."
            }
        };

        private HardwareScanService()
        {
            _http = new HttpClient { BaseAddress = new Uri("http://192.168.0.137:5000") };
            _http.Timeout = TimeSpan.FromSeconds(15);
        }

        public async Task<List<DiscoveredSlave>> ScanAsync()
        {
            try
            {
                var response = await _http.PostAsync("/api/ethercat/scan", null);

                if (response.IsSuccessStatusCode)
                {
                    var slaves = await response.Content.ReadFromJsonAsync<List<DiscoveredSlave>>();
                    return slaves ?? new List<DiscoveredSlave>();
                }
                else
                {
                    Console.WriteLine($"Scan failed: {response.StatusCode}");
                    return _simulatedSlaves;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Scan exception: {ex.Message}");
                return _simulatedSlaves;
            }
        }
    }
}