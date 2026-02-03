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

    public class ErrorData
    {
        public string Kind { get; set; }
        public string Text { get; set; }
    }

    public class LogResponse { public string Log { get; set; } }

    public class MachineControlService
    {
        private static MachineControlService _instance;
        public static MachineControlService Instance => _instance ??= new MachineControlService();

        private readonly HttpClient _httpClient;
        private string _serverUrl = "http://192.168.0.137:5000"; // 請確認您的 IP
        private readonly JsonSerializerOptions _jsonOptions;

        private CancellationTokenSource _jogCts;

        // [修正] 用來記錄最後一次狀態 (供 Jog 防呆使用)
        private MachineStatusData _lastCachedStatus;

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

        // --- 1. 狀態檢查 ---
        public async Task<(ConnectionState State, MachineStatusData Data)> GetStatusAsync()
        {
            // [修正] 移除內部的 PollErrorsAsync，改由 MainViewModel 主動呼叫 GetErrorsAsync
            // await PollErrorsAsync(); 

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

                if (response.IsSuccessStatusCode)
                {
                    if (result?.Status == "Success")
                    {
                        // [關鍵修正] 更新緩存，讓 JogAsync 的防呆邏輯生效
                        _lastCachedStatus = result.Data;
                        return (ConnectionState.Connected, result.Data);
                    }
                    else
                    {
                        return (ConnectionState.ServerOnly, null);
                    }
                }
                else
                {
                    return (ConnectionState.ServerOnly, null);
                }
            }
            catch
            {
                return (ConnectionState.Disconnected, null);
            }
        }

        // --- 2. [新增] 公開的錯誤查詢方法 (供 MainViewModel 呼叫) ---
        public async Task<List<ErrorData>> GetErrorsAsync()
        {
            try
            {
                // 呼叫後端 API，這會取得並清空後端的錯誤佇列
                var response = await _httpClient.GetAsync($"{_serverUrl}/v2/errors");

                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadFromJsonAsync<ApiResponse<List<ErrorData>>>(_jsonOptions);

                    if (result != null && result.Status == "Success")
                    {
                        return result.Data; // 回傳 List<ErrorData> 給 ViewModel 處理
                    }
                }
            }
            catch
            {
                // 錯誤查詢失敗通常是因為斷線，GetStatusAsync 那邊會處理斷線狀態，這邊靜默即可
            }
            return null;
        }

        // --- 3. 輔助與指令 ---

        public async Task<bool> CheckConnectionAsync()
        {
            var result = await GetStatusAsync();
            return result.State == ConnectionState.Connected;
        }

        private async Task<T> SendV2CommandAsync<T>(string endpoint, object payload, CancellationToken token = default)
        {
            // AlarmService.Instance.AddLog("API", $"REQ: {endpoint}"); // 視需求開啟 debug log
            try
            {
                var response = await _httpClient.PostAsJsonAsync($"{_serverUrl}/v2/{endpoint}", payload ?? new { }, token);

                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadFromJsonAsync<ApiResponse<T>>(_jsonOptions, token);
                    if (result?.Status == "Success")
                    {
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
                // Ignore cancel
            }
            catch (Exception ex)
            {
                AlarmService.Instance.AddLog("API", $"EX: {ex.Message}");
            }
            return default;
        }

        private async Task SendV2CommandAsync(string endpoint, object payload = null, CancellationToken token = default)
            => await SendV2CommandAsync<object>(endpoint, payload, token);

        // --- 4. 控制方法 ---
        public async Task ResetMachineAsync() => await SendV2CommandAsync("machine/reset");
        public async Task TriggerEstopAsync() => await SendV2CommandAsync("machine/estop");

        public async Task JogAsync(int axis, double speed, double distance = 0)
        {
            // 防呆：如果機器正在移動，禁止 Jog (除非是停止指令 speed=0)
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

        // 在 MachineControlService 類別中新增此方法

        public async Task SaveConfigAndRestartAsync(List<AxisSetting> axes)
        {
            // 1. 準備 Payload
            var payload = new
            {
                axes = axes
            };

            // 2. 序列化
            string json = System.Text.Json.JsonSerializer.Serialize(payload);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            // 3. 發送請求
            // 注意：這裡假設您的 Service 內部已經維護了 _httpClient 或 BaseUrl
            // 如果沒有，請使用與您現有方法相同的 URL 組合方式
            string url = $"{_serverUrl}/api/machine/save_config";

            try
            {
                // 設定較長的 Timeout，因為重啟需要時間
                _httpClient.Timeout = TimeSpan.FromSeconds(10);

                var response = await _httpClient.PostAsync(url, content);

                if (!response.IsSuccessStatusCode)
                {
                    string error = await response.Content.ReadAsStringAsync();
                    throw new Exception($"Server Error: {error}");
                }

                // 成功後，通常不需要回傳內容，因為接下來就是要等待重啟
            }
            catch (Exception)
            {
                // 恢復 Timeout (如果是全域 Client)
                _httpClient.Timeout = TimeSpan.FromSeconds(5);
                throw; // 將錯誤拋回給 ViewModel 顯示
            }
        }
    }
}