using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using System.Linq; // [新增] 用於 LINQ 查詢
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
            // 請確認這個 IP 是您的後端位址
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
                    // Log: 顯示抓到的數量
                    System.Diagnostics.Debug.WriteLine($"ScanAsync Success: Found {slaves?.Count ?? 0} slaves.");

                    // Log: 列出細節
                    if (slaves != null)
                    {
                        foreach (var slave in slaves)
                        {
                            System.Diagnostics.Debug.WriteLine($" -> [Slave] Index: {slave.Index}, Name: {slave.Name}, VID: {slave.VendorId}, PID: {slave.ProductCode}");
                        }
                    }
                    return slaves ?? new List<DiscoveredSlave>();
                }
                else
                {
                    Console.WriteLine($"Scan failed: {response.StatusCode}");
                    System.Diagnostics.Debug.WriteLine("Scan failed");

                    return _simulatedSlaves;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Scan exception: {ex.Message}");
                System.Diagnostics.Debug.WriteLine("Scan exception");
                return _simulatedSlaves;
            }
        }

        // =============================================================
        // [新增] Level 3 硬體驗證邏輯
        // 比對標準：站號 (Index) + 廠商 (VendorId) + 產品 (ProductCode)
        // =============================================================
        public (bool IsValid, string Message) ValidateTopology(List<DiscoveredSlave> currentSlaves, MachineConfig savedConfig)
        {
            if (savedConfig == null || savedConfig.Mappings == null || savedConfig.Mappings.Count == 0)
                return (false, "No saved configuration found.");

            foreach (var map in savedConfig.Mappings)
            {
                // 1. 找硬體：根據 PhysicalIndex (站號) 尋找
                var slave = currentSlaves.FirstOrDefault(s => s.Index == map.PhysicalIndex);

                if (slave == null)
                {
                    return (false, $"Missing Device: Axis {map.LogicalName} expects Slave {map.PhysicalIndex} (Not Found).");
                }

                // 2. 比對 VendorID (忽略大小寫與 0x 前綴)
                if (!CompareHex(map.ExpectedVendorId, slave.VendorId))
                {
                    return (false, $"Mismatch: Axis {map.LogicalName} (Slave {slave.Index}) Vendor Changed! Expected: {map.ExpectedVendorId}, Found: {slave.VendorId}");
                }

                // 3. 比對 ProductCode
                if (!CompareHex(map.ExpectedProductCode, slave.ProductCode))
                {
                    return (false, $"Mismatch: Axis {map.LogicalName} (Slave {slave.Index}) Product Changed! Expected: {map.ExpectedProductCode}, Found: {slave.ProductCode}");
                }
            }

            return (true, "Hardware Verification Passed.");
        }

        // 輔助：比較 Hex 字串
        private bool CompareHex(string hex1, string hex2)
        {
            if (string.IsNullOrEmpty(hex1) || string.IsNullOrEmpty(hex2)) return false;
            try
            {
                long v1 = Convert.ToInt64(hex1.ToLower().Replace("0x", ""), 16);
                long v2 = Convert.ToInt64(hex2.ToLower().Replace("0x", ""), 16);
                return v1 == v2;
            }
            catch
            {
                // 如果解析失敗，直接比對字串
                return hex1.Equals(hex2, StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}