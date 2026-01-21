using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Threading.Tasks;
using CncController.Models;

namespace CncController.Services
{
    public class ConfigurationService
    {
        public static ConfigurationService Instance { get; } = new ConfigurationService();
        private readonly HttpClient _http;

        public ConfigurationService()
        {
            _http = new HttpClient { BaseAddress = new System.Uri("http://127.0.0.1:5000") };
        }

        public async Task SaveConfigAsync(MachineConfig config)
        {
            // 1. 生成 INI (這裡放入生成邏輯)
            var ini = new StringBuilder();
            ini.AppendLine("[EMC]");
            ini.AppendLine("MACHINE = CNC_CONTROLLER_GEN");
            ini.AppendLine("[DISPLAY]");
            ini.AppendLine("DISPLAY = probe_basic"); // ★ 指定 Probe Basic

            foreach (var axis in config.Axes)
            {
                ini.AppendLine($"\n[AXIS_{axis.AxisID}]");
                double scale = axis.PulsePerRev / axis.Pitch; // 計算電子齒輪比
                ini.AppendLine($"SCALE = {scale}");
                ini.AppendLine($"MIN_LIMIT = {axis.SoftLimitNeg}");
                ini.AppendLine($"MAX_LIMIT = {axis.SoftLimitPos}");
                ini.AppendLine($"HOME_SEARCH_VEL = {axis.HomeSpeed}");
            }

            // 2. 生成 HAL (簡化範例)
            var hal = new StringBuilder();
            hal.AppendLine("loadusr -W lcec_conf ethercat-conf.xml");
            hal.AppendLine("loadrt lcec");

            // 3. 上傳
            var payload = new
            {
                IniContent = ini.ToString(),
                HalContent = hal.ToString(),
                XmlContent = ""
            };

            await _http.PostAsJsonAsync("/api/config/update", payload);
        }
    }
}