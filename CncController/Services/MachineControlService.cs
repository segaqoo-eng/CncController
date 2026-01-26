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
    // API 資料協定定義
    // ==============================================================================
    public class ApiResponse<T>
    {
        public string Status { get; set; }
        public string Message { get; set; }
        public T Data { get; set; }
    }

    public class MachineStatusData
    {
        public bool Connected { get; set; }
        public string Task_State { get; set; }
        public string Interp_State { get; set; }
        public Dictionary<string, double> Position { get; set; }
        public double Feedrate { get; set; }
        public double Spindle_Speed { get; set; }
        public string File { get; set; }
    }

    public class ErrorData { public string Kind { get; set; } public string Text { get; set; } }
    public class LogResponse { public string Log { get; set; } public string Error { get; set; } }

    // ==============================================================================
    // 核心服務實作
    // ==============================================================================
    public class MachineControlService
    {
        private static MachineControlService _instance;
        public static MachineControlService Instance => _instance ??= new MachineControlService();

        private readonly HttpClient _httpClient;
        private string _serverUrl = "http://192.168.0.137:5000"; // 請確認 IP
        private readonly JsonSerializerOptions _jsonOptions;

        public MachineControlService()
        {
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            _jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        }

        // --- 狀態與錯誤輪詢 ---
        public async Task<MachineStatusData> GetStatusAsync()
        {
            await PollErrorsAsync(); // 每次獲取狀態前先獲取 LinuxCNC 錯誤
            try
            {
                var response = await _httpClient.GetAsync($"{_serverUrl}/v2/status");
                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadFromJsonAsync<ApiResponse<MachineStatusData>>(_jsonOptions);
                    if (result?.Status == "Success") return result.Data;
                }
            }
            catch { }
            return null;
        }

        // ★★★ 補回缺失的方法 ★★★
        public async Task<bool> CheckConnectionAsync()
        {
            var status = await GetStatusAsync();
            return status != null && status.Connected;
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
                            AlarmService.Instance.AddLog("ERROR", $"[CNC] {err.Text}");
                    }
                }
            }
            catch { }
        }

        // --- 通用 V2 指令發送 (含自動日誌寫入) ---
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
                        AlarmService.Instance.AddLog("API", $"RES: {endpoint} [Success]");
                        return result.Data;
                    }
                    AlarmService.Instance.AddLog("API", $"RES: {endpoint} [Error: {result?.Message}]");
                }
                else
                {
                    AlarmService.Instance.AddLog("API", $"HTTP ERROR: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                AlarmService.Instance.AddLog("API", $"EXCEPTION: {ex.Message}");
            }
            return default;
        }

        private async Task SendV2CommandAsync(string endpoint, object payload = null)
            => await SendV2CommandAsync<object>(endpoint, payload);

        // --- 公開控制方法 ---
        public async Task ResetMachineAsync() => await SendV2CommandAsync("machine/reset");
        public async Task ShutdownMachineAsync() => await SendV2CommandAsync("machine/shutdown");
        public async Task TriggerEstopAsync() => await SendV2CommandAsync("machine/estop");
        public async Task JogAsync(int axis, double speed) => await SendV2CommandAsync("motion/jog", new { axis, speed });
        public async Task JogStopAsync(int axis) => await SendV2CommandAsync("motion/jog", new { axis, speed = 0 });
        public async Task CycleStartAsync(string file = null) => await SendV2CommandAsync("program/run", new { file_name = file });
        public async Task StopAsync() => await SendV2CommandAsync("program/stop");
        public async Task FeedHoldAsync() => await SendV2CommandAsync("program/pause"); // 補上 FeedHold
        public async Task<string> GetStartupLogAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync($"{_serverUrl}/api/machine/log");
                var result = await response.Content.ReadFromJsonAsync<LogResponse>(_jsonOptions);
                return result?.Log ?? "No Log.";
            }
            catch { return "Log unavailable"; }
        }
    }
}