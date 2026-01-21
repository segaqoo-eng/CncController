using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using CncController.Models;

namespace CncController.Services
{
    public class ConfigurationService
    {
        public static ConfigurationService Instance { get; } = new ConfigurationService();

        private readonly HttpClient _http;
        private const string ConfigFileName = "MachineConfig.json";
        private readonly JsonSerializerOptions _jsonOptions;

        private ConfigurationService()
        {
            _http = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:5000") };
            _jsonOptions = new JsonSerializerOptions { WriteIndented = true, PropertyNameCaseInsensitive = true };
        }

        // 讀取本地設定
        public async Task<MachineConfig> LoadConfigAsync()
        {
            try
            {
                if (!File.Exists(ConfigFileName)) return new MachineConfig();
                string json = await File.ReadAllTextAsync(ConfigFileName);
                return JsonSerializer.Deserialize<MachineConfig>(json, _jsonOptions) ?? new MachineConfig();
            }
            catch
            {
                return new MachineConfig();
            }
        }

        // 儲存設定 (本地 + 遠端)
        public async Task SaveConfigAsync(MachineConfig config)
        {
            // 1. 存本地
            string json = JsonSerializer.Serialize(config, _jsonOptions);
            await File.WriteAllTextAsync(ConfigFileName, json);

            // 2. 生成 LinuxCNC 設定 (INI)
            var ini = new StringBuilder();
            ini.AppendLine("[EMC]");
            ini.AppendLine("MACHINE = CNC_CONTROLLER_GEN");
            foreach (var axis in config.Axes)
            {
                ini.AppendLine($"\n[AXIS_{axis.AxisID}]");
                ini.AppendLine($"SCALE = {axis.PulsePerRev / (axis.Pitch == 0 ? 1 : axis.Pitch)}");
            }

            // 3. 上傳
            var payload = new { IniContent = ini.ToString(), HalContent = "", XmlContent = "" };
            try
            {
                await _http.PostAsJsonAsync("/api/config/update", payload);
            }
            catch
            {
                // 上傳失敗不影響本地存檔，這裡選擇忽略或記錄 Log
            }
        }
    }
}