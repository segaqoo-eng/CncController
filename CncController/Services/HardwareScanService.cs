using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using CncController.Models;

namespace CncController.Services
{
    public class HardwareScanService
    {
        // ★★★ 修正這裡：補上 Instance 宣告 (這是之前缺少的) ★★★
        public static HardwareScanService Instance { get; } = new HardwareScanService();

        // Python Agent 的 IP 與 Port
        private const string HOST = "192.168.0.137";
        private const int PORT = 55005;

        // 私有建構子 (防止外部直接 new)
        private HardwareScanService() { }

        /// <summary>
        /// 掃描 EtherCAT 匯流排
        /// </summary>
        public async Task<List<DiscoveredSlave>> ScanAsync()
        {
            var list = new List<DiscoveredSlave>();
            try
            {
                using var client = new TcpClient();

                // 設定連線逾時 2秒，避免介面卡死
                var connectTask = client.ConnectAsync(HOST, PORT);
                if (await Task.WhenAny(connectTask, Task.Delay(2000)) != connectTask)
                {
                    throw new TimeoutException("Connection timed out (Check Python Agent)");
                }

                using var stream = client.GetStream();
                byte[] cmd = Encoding.UTF8.GetBytes("CMD_SCAN");
                await stream.WriteAsync(cmd, 0, cmd.Length);

                byte[] buffer = new byte[65536];
                client.ReceiveTimeout = 3000; // 讀取逾時設定

                int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                string csvData = Encoding.UTF8.GetString(buffer, 0, bytesRead);

                if (csvData.StartsWith("ERROR")) throw new Exception(csvData);

                // 解析 CSV 回傳資料
                var lines = csvData.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    if (line.StartsWith("Slave,")) continue; // 跳過標題列

                    var parts = line.Split(',');
                    if (parts.Length >= 5)
                    {
                        list.Add(new DiscoveredSlave
                        {
                            Index = int.Parse(parts[0]),
                            VendorId = parts[1],
                            ProductCode = parts[2],
                            Source = parts[3],
                            Name = parts[4].Trim('"')
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                // 當連線失敗時，回傳模擬資料 (方便您測試 UI)
                // 正式版建議改為記錄 Log
                System.Diagnostics.Debug.WriteLine($"Scan Error: {ex.Message}");

                list.Add(new DiscoveredSlave { Index = 0, Name = "Delta Drive (Sim)", VendorId = "0x001", ProductCode = "0x101" });
                list.Add(new DiscoveredSlave { Index = 1, Name = "IO Module (Sim)", VendorId = "0x002", ProductCode = "0x202" });
            }
            return list;
        }
    }
}