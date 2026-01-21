using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using CncController.Models; // 使用正確的 Namespace

namespace CncController.Services
{
    public class HardwareScanService
    {
        private const string HOST = "192.168.0.137";
        private const int PORT = 55005; // Python Agent Port

        public async Task<List<DiscoveredSlave>> ScanAsync()
        {
            var list = new List<DiscoveredSlave>();
            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync(HOST, PORT);

                using var stream = client.GetStream();
                byte[] cmd = Encoding.UTF8.GetBytes("CMD_SCAN");
                await stream.WriteAsync(cmd, 0, cmd.Length);

                byte[] buffer = new byte[65536];
                int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                string csvData = Encoding.UTF8.GetString(buffer, 0, bytesRead);

                if (csvData.StartsWith("ERROR")) throw new Exception(csvData);

                // 解析 CSV
                var lines = csvData.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    if (line.StartsWith("Slave,")) continue;
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
            catch
            {
                // 模擬資料 (方便您在沒連線時測試 UI)
                list.Add(new DiscoveredSlave { Index = 0, Name = "Delta Drive (Sim)", VendorId = "0x1", ProductCode = "0x1" });
                list.Add(new DiscoveredSlave { Index = 1, Name = "IO Module (Sim)", VendorId = "0x2", ProductCode = "0x2" });
            }
            return list;
        }
    }
}