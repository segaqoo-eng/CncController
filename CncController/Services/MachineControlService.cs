using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;

namespace CncController.Services
{
    public class MachineControlService
    {
        private static MachineControlService _instance;
        public static MachineControlService Instance => _instance ??= new MachineControlService();

        private readonly HttpClient _httpClient;
        private string _serverUrl = "http://192.168.0.137:5000"; // 請確認 IP

        public MachineControlService()
        {
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        }

        public void SetServerIp(string ip)
        {
            _serverUrl = $"http://{ip}:5000";
        }

        // === 核心狀態檢查 ===
        public async Task<bool> CheckConnectionAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync($"{_serverUrl}/api/status");
                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadFromJsonAsync<MachineStatusResponse>();
                    return result?.Connected ?? false;
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        // === 取得啟動日誌 (新增) ===
        public async Task<string> GetStartupLogAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync($"{_serverUrl}/api/machine/log");
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    // 簡單解析
                    using var doc = System.Text.Json.JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("log", out var logElement))
                    {
                        return logElement.GetString();
                    }
                }
                return "Unable to fetch log.";
            }
            catch (Exception ex)
            {
                return $"Error fetching log: {ex.Message}";
            }
        }

        // === 通用控制發送 ===
        private async Task SendCommandAsync(string action, object value = null, int? axis = null)
        {
            try
            {
                var payload = new { action, value, axis };
                await _httpClient.PostAsJsonAsync($"{_serverUrl}/api/control", payload);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Cmd Error: {ex.Message}");
            }
        }

        // === 控制指令封裝 ===
        public async Task SetEstopAsync(bool active) => await SendCommandAsync("estop", active);
        public async Task SetPowerAsync(bool active) => await SendCommandAsync("power", active);
        public async Task CycleStartAsync() => await SendCommandAsync("cycle_start");
        public async Task StopAsync() => await SendCommandAsync("abort");
        public async Task FeedHoldAsync(bool pause) => await SendCommandAsync("feed_hold", pause);
        public async Task ReloadProgramAsync() => await SendCommandAsync("reload");
        public async Task SetCoolantAsync(string type, bool on) => await SendCommandAsync(type, on);
        public async Task HomeAxisAsync(int axisIndex) => await SendCommandAsync("home", axisIndex);
        public async Task JogAsync(int axis, double speed) => await SendCommandAsync("jog", speed, axis);
        public async Task JogStopAsync(int axis) => await SendCommandAsync("jog_stop", null, axis);
    }

    // 輔助類別
    public class MachineStatusResponse
    {
        public bool Connected { get; set; }
        public object State { get; set; }
    }
}