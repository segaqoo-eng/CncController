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

        // ★★★ 修正：補上 LoadConfigAsync ★★★
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

        public async Task SaveConfigAsync(MachineConfig config)
        {
            // 1. 存本地 JSON (保留設定)
            string json = JsonSerializer.Serialize(config, _jsonOptions);
            await File.WriteAllTextAsync(ConfigFileName, json);

            // 2. 生成 INI
            var ini = new StringBuilder();
            ini.AppendLine("[EMC]");
            ini.AppendLine("MACHINE = CNC_CONTROLLER_GEN");
            ini.AppendLine("[DISPLAY]");
            ini.AppendLine("DISPLAY = probe_basic");

            foreach (var axis in config.Axes)
            {
                ini.AppendLine($"\n[AXIS_{axis.AxisID}]");
                double pitch = axis.Pitch == 0 ? 1 : axis.Pitch;
                ini.AppendLine($"SCALE = {axis.PulsePerRev / pitch}");
                ini.AppendLine($"MIN_LIMIT = {axis.SoftLimitNeg}");
                ini.AppendLine($"MAX_LIMIT = {axis.SoftLimitPos}");
                ini.AppendLine($"HOME_SEARCH_VEL = {axis.HomeSpeed}");
            }

            // 3. 上傳
            var payload = new { IniContent = ini.ToString(), HalContent = "loadrt lcec", XmlContent = "" };
            try
            {
                await _http.PostAsJsonAsync("/api/config/update", payload);
            }
            catch
            {
                // 忽略上傳錯誤，避免卡住 UI
            }
        }
    }
}