using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CncController.Models;

namespace CncController.Services
{
    public class ApiResponse<T>
    {
        public string Status { get; set; }
        public string Message { get; set; }
        public string Version { get; set; }
        public T Data { get; set; }
    }

    public class ErrorData { public string Kind { get; set; } public string Text { get; set; } }
    public class LogResponse { public string Log { get; set; } }

    public class MachineControlService
    {
        private static MachineControlService _instance;
        public static MachineControlService Instance => _instance ??= new MachineControlService();

        private readonly HttpClient _httpClient;
        private string _serverUrl = "http://192.168.0.137:5000"; // 請確認您的 IP
        private readonly JsonSerializerOptions _jsonOptions;

        private CancellationTokenSource _jogCts;
        private MachineStatusData _lastCachedStatus;
       // private MachineStatusData _lastCachedStatus; // 需透過 GetStatusAsync 更新此變數
        public string ServerVersion { get; private set; } = "Unknown";

        public enum ConnectionState
        {
            Disconnected,
            ServerOnly,
            Connected
        }

        public MachineControlService()
        {
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            _jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        }

        // --- 狀態檢查 ---
        public async Task<(ConnectionState State, MachineStatusData Data)> GetStatusAsync()
        {
            await PollErrorsAsync();
            try
            {
                var response = await _httpClient.GetAsync($"{_serverUrl}/v2/status");

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
                catch { }

                if (response.IsSuccessStatusCode) // HTTP 200 OK
                {
                    // 情況 A: 完美連線
                    if (result?.Status == "Success")
                    {
                        return (ConnectionState.Connected, result.Data);
                    }
                    // 情況 B: Server 活著 (HTTP 200)，但內容回傳 Error (例如 NML 斷線)
                    else
                    {
                        // [關鍵修正] 這裡原本漏掉了，導致狀態沒變
                        // 我們將其視為 "ServerOnly" (橘燈)
                        return (ConnectionState.ServerOnly, null);
                    }
                }
                else // HTTP 404, 500, 503...
                {
                    // 情況 C: Server 活著但報錯 (例如我們在 Python 改回傳 503)
                    return (ConnectionState.ServerOnly, null);
                }
            }
            catch
            {
                return (ConnectionState.Disconnected, null);
            }

            return (ConnectionState.Disconnected, null);
        }

        // ★★★ [新增] 相容性修正：讓 WaitForLinuxCNC 可以呼叫 ★★★
        public async Task<bool> CheckConnectionAsync()
        {
            var result = await GetStatusAsync();
            return result.State == ConnectionState.Connected;
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

        // --- 指令發送 ---
        private async Task<T> SendV2CommandAsync<T>(string endpoint, object payload, CancellationToken token = default)
        {
            AlarmService.Instance.AddLog("API", $"REQ: {endpoint}");
            try
            {
                var response = await _httpClient.PostAsJsonAsync($"{_serverUrl}/v2/{endpoint}", payload ?? new { }, token);

                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadFromJsonAsync<ApiResponse<T>>(_jsonOptions, token);
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
            catch (TaskCanceledException)
            {
                AlarmService.Instance.AddLog("API", $"RES: {endpoint} [CANCELED]");
            }
            catch (Exception ex)
            {
                AlarmService.Instance.AddLog("API", $"EX: {ex.Message}");
            }
            return default;
        }

        private async Task SendV2CommandAsync(string endpoint, object payload = null, CancellationToken token = default)
            => await SendV2CommandAsync<object>(endpoint, payload, token);

        // --- 控制方法 ---
        public async Task ResetMachineAsync() => await SendV2CommandAsync("machine/reset");
        public async Task TriggerEstopAsync() => await SendV2CommandAsync("machine/estop");

        //private readonly HashSet<int> _activeJogAxes = new();
        public async Task JogAsync(int axis, double speed, double distance = 0)
        {
            if (speed != 0 && _lastCachedStatus != null && _lastCachedStatus.Is_Moving)
            {
                AlarmService.Instance.AddLog("WARN", "JOG blocked: Machine is moving.");
                return;
            }

            if (_jogCts != null)
            {
                _jogCts.Cancel();
                _jogCts.Dispose();
            }
            _jogCts = new CancellationTokenSource();

            await SendV2CommandAsync("motion/jog", new { axis, speed, dist = distance }, _jogCts.Token);
        }

        public async Task JogStopAsync(int axis)
        {
            if (_jogCts != null)
            {
                _jogCts.Cancel();
                _jogCts.Dispose();
                _jogCts = null;
            }
            await SendV2CommandAsync("motion/jog", new { axis, speed = 0, dist = 0 });
        }

        public async Task CycleStartAsync(string file = null)
        {
            if (_lastCachedStatus != null && _lastCachedStatus.Is_Moving)
            {
                AlarmService.Instance.AddLog("WARN", "Run blocked: Machine is moving.");
                return;
            }
            await SendV2CommandAsync("program/run", new { file_name = file });
        }

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