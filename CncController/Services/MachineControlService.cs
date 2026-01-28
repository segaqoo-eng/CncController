using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using CncController.Models;

namespace CncController.Services
{
    // ==============================================================================
    // API 資料協定定義 (修改：加入 Version)
    // ==============================================================================
    public class ApiResponse<T>
    {
        public string Status { get; set; }
        public string Message { get; set; }
        public string Version { get; set; } // [新增] 支援後端版本回傳
        public T Data { get; set; }
    }

    public class MachineStatusData
    {
        public bool Connected { get; set; }
        public string Task_State { get; set; }
        public Dictionary<string, double> Position { get; set; }
        public double Feedrate { get; set; }
        public double Spindle_Speed { get; set; }
        public string File { get; set; }
    }

    public class ErrorData { public string Kind { get; set; } public string Text { get; set; } }
    public class LogResponse { public string Log { get; set; } }

    // ==============================================================================
    // 核心服務實作
    // ==============================================================================
    public class MachineControlService
    {
        private static MachineControlService _instance;
        public static MachineControlService Instance => _instance ??= new MachineControlService();

        private readonly HttpClient _httpClient;
        private string _serverUrl = "http://192.168.0.137:5000"; // 請確認您的 IP
        private readonly JsonSerializerOptions _jsonOptions;

        // [新增] 伺服器版本屬性
        public string ServerVersion { get; private set; } = "Unknown";

        // [新增] 連線狀態列舉
        public enum ConnectionState
        {
            Disconnected,   // 完全斷線 (HTTP 失敗)
            ServerOnly,     // Server 在，但 LinuxCNC 沒開 (HTTP 500)
            Connected       // 正常運作 (HTTP 200)
        }

        public MachineControlService()
        {
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            _jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        }

        // --- 狀態檢查 (大幅修改：回傳 Tuple) ---

        // 為了相容性保留舊方法 (如果有的話)，但建議 ViewModel 都改用 GetStatusAsync
        public async Task<bool> CheckConnectionAsync()
        {
            var (state, _) = await GetStatusAsync();
            return state == ConnectionState.Connected;
        }

        // [核心修改] 回傳 (狀態, 資料) 的 Tuple
        public async Task<(ConnectionState State, MachineStatusData Data)> GetStatusAsync()
        {
            await PollErrorsAsync();
            try
            {
                var response = await _httpClient.GetAsync($"{_serverUrl}/v2/status");

                // 1. 嘗試讀取內容與版本號
                ApiResponse<MachineStatusData> result = null;
                try
                {
                    var jsonString = await response.Content.ReadAsStringAsync();
                    if (!string.IsNullOrEmpty(jsonString))
                    {
                        result = JsonSerializer.Deserialize<ApiResponse<MachineStatusData>>(jsonString, _jsonOptions);
                        if (result != null && !string.IsNullOrEmpty(result.Version))
                        {
                            ServerVersion = result.Version;
                        }
                    }
                }
                catch { /* JSON 解析失敗忽略 */ }

                // 2. 判斷狀態
                if (response.IsSuccessStatusCode)
                {
                    // HTTP 200: 連線成功且 LinuxCNC 正常
                    if (result?.Status == "Success")
                        return (ConnectionState.Connected, result.Data);
                }
                else
                {
                    // HTTP 500: Server 活著 (有回傳 JSON)，但 LinuxCNC 報錯
                    // 這裡我們視為 ServerOnly 模式
                    return (ConnectionState.ServerOnly, null);
                }
            }
            catch
            {
                // 網路錯誤 (Timeout, Connection Refused)
                return (ConnectionState.Disconnected, null);
            }

            return (ConnectionState.Disconnected, null);
        }

        private async Task PollErrorsAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync($"{_serverUrl}/v2/errors");
                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadFromJsonAsync<ApiResponse<List<ErrorData>>>(_jsonOptions);
                    if (result?.Status == "Success" && result.Data != null)
                    {
                        foreach (var err in result.Data)
                            AlarmService.Instance.AddLog("ERROR", $"[{err.Kind}] {err.Text}");
                    }
                }
            }
            catch { }
        }

        // --- 通用 V2 指令發送 ---
        private async Task<T> SendV2CommandAsync<T>(string endpoint, object payload = null)
        {
            AlarmService.Instance.AddLog("API", $"REQ: {endpoint}");
            try
            {
                var response = await _httpClient.PostAsJsonAsync($"{_serverUrl}/v2/{endpoint}", payload ?? new { });
                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadFromJsonAsync<ApiResponse<T>>(_jsonOptions);
                    if (result?.Status == "Success")
                    {
                        AlarmService.Instance.AddLog("API", $"RES: {endpoint} [OK]");
                        return result.Data;
                    }
                    AlarmService.Instance.AddLog("API", $"RES: {endpoint} [Err: {result?.Message}]");
                }
                else
                {
                    AlarmService.Instance.AddLog("API", $"HTTP: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                AlarmService.Instance.AddLog("API", $"EX: {ex.Message}");
            }
            return default;
        }

        private async Task SendV2CommandAsync(string endpoint, object payload = null)
            => await SendV2CommandAsync<object>(endpoint, payload);

        // --- 公開控制方法 ---

        public async Task ResetMachineAsync() => await SendV2CommandAsync("machine/reset");
        public async Task ShutdownMachineAsync() => await SendV2CommandAsync("machine/shutdown");
        public async Task TriggerEstopAsync() => await SendV2CommandAsync("machine/estop");

        public async Task JogAsync(int axis, double speed, double distance = 0)
        {
            await SendV2CommandAsync("motion/jog", new { axis, speed, dist = distance });
        }

        public async Task JogStopAsync(int axis)
            => await SendV2CommandAsync("motion/jog", new { axis, speed = 0 });

        public async Task CycleStartAsync(string file = null)
            => await SendV2CommandAsync("program/run", new { file_name = file });

        public async Task StopAsync() => await SendV2CommandAsync("program/stop");

        public async Task FeedHoldAsync() => await SendV2CommandAsync("program/pause");

        public async Task<string> GetStartupLogAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync($"{_serverUrl}/api/machine/log");
                var result = await response.Content.ReadFromJsonAsync<LogResponse>(_jsonOptions);
                return result?.Log ?? "No Log";
            }
            catch { return "Log Unavailable"; }
        }
    }
}