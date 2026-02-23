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
        private readonly HttpClient _estopClient; // [安全] 急停專用通道，不受其他請求阻塞
        private string _serverUrl = "http://192.168.0.137:5000"; // 請確認您的 IP
        private readonly JsonSerializerOptions _jsonOptions;

        private CancellationTokenSource _jogCts;

        // [核心] 用來記錄最後一次狀態，用於本地端的快速防呆判斷
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
            // 全域 Client 只設定短 Timeout (適合高頻率 Polling)
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            // [安全] 急停專用 HttpClient：Timeout 較短（2s），且獨立於一般通訊通道
            _estopClient = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            _jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        }

        // --- 1. 狀態檢查 (同步後端狀態) ---
        public async Task<(ConnectionState State, MachineStatusData Data)> GetStatusAsync()
        {
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
                catch (Exception ex)
                {
                    AlarmService.Instance.AddLog("API", $"Status JSON parse error: {ex.GetType().Name}: {ex.Message}");
                }

                if (response.IsSuccessStatusCode)
                {
                    if (result?.Status == "Success")
                    {
                        // [關鍵] 更新本地緩存狀態，供 CycleStart/Jog 判斷使用
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

        // --- 2. 錯誤查詢 ---
        public async Task<List<ErrorData>> GetErrorsAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync($"{_serverUrl}/v2/errors");
                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadFromJsonAsync<ApiResponse<List<ErrorData>>>(_jsonOptions);
                    if (result != null && result.Status == "Success")
                    {
                        return result.Data;
                    }
                }
            }
            catch (Exception ex)
            {
                AlarmService.Instance.AddLog("API", $"GetErrors failed: {ex.GetType().Name}: {ex.Message}");
            }
            return null;
        }

        // --- 3. 底層通訊輔助 ---
        public async Task<bool> CheckConnectionAsync()
        {
            var result = await GetStatusAsync();
            return result.State == ConnectionState.Connected;
        }

        private async Task<T> SendV2CommandAsync<T>(string endpoint, object payload, CancellationToken token = default)
        {
            try
            {
                var response = await _httpClient.PostAsJsonAsync($"{_serverUrl}/v2/{endpoint}", payload ?? new { }, token);

                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadFromJsonAsync<ApiResponse<T>>(_jsonOptions, token);

                    // [規範] 只有 Status == Success 才算成功，否則視為邏輯錯誤
                    if (result?.Status == "Success")
                    {
                        return result.Data;
                    }

                    // 記錄後端回傳的具體錯誤訊息 (如 ERR_NOT_HOMED)
                    AlarmService.Instance.AddLog("API", $"CMD Fail: {endpoint} -> {result?.Message}");
                }
                else
                {
                    AlarmService.Instance.AddLog("API", $"HTTP Error: {response.StatusCode} on {endpoint}");
                }
            }
            catch (TaskCanceledException) { }
            catch (Exception ex)
            {
                AlarmService.Instance.AddLog("API", $"Exception: {ex.Message}");
            }
            return default;
        }

        private async Task SendV2CommandAsync(string endpoint, object payload = null, CancellationToken token = default)
            => await SendV2CommandAsync<object>(endpoint, payload, token);


        // --- [修正] 檔案上傳 (解決 Timeout 修改報錯問題) ---
        public async Task<bool> UploadGCodeAsync(string fileName, string gcodeContent)
        {
            var payload = new { name = fileName, content = gcodeContent };

            try
            {
                // ★★★ 修正點：建立一個全新的臨時 HttpClient ★★★
                // 因為 _httpClient 已經被狀態輪詢使用過，Timeout 屬性被鎖定不可修改。
                // 且上傳需要較長的 Timeout (例如 10秒)，不能用全域的 3秒。
                using (var uploadClient = new HttpClient())
                {
                    uploadClient.Timeout = TimeSpan.FromSeconds(20); // 設定充裕的時間

                    var response = await uploadClient.PostAsJsonAsync($"{_serverUrl}/api/files/upload", payload);
                    return response.IsSuccessStatusCode;
                }
            }
            catch (Exception ex)
            {
                AlarmService.Instance.AddLog("API", $"Upload Fail: {ex.Message}");
                return false;
            }
        }


        // --- 4. 控制指令 (對應新架構) ---

        public async Task ResetMachineAsync() => await SendV2CommandAsync("machine/reset");

        // [安全] 急停使用獨立 _estopClient，不受一般指令 HTTP 佇列阻塞
        // 同時立即取消所有進行中的 JOG 操作
        public async Task TriggerEstopAsync()
        {
            // 1. 先取消進行中的 JOG CancellationToken
            _jogCts?.Cancel();

            try
            {
                // 2. 用急停專用通道發送指令
                var response = await _estopClient.PostAsJsonAsync($"{_serverUrl}/v2/machine/estop", new { });
                if (!response.IsSuccessStatusCode)
                {
                    AlarmService.Instance.AddLog("API", $"E-Stop HTTP Error: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                AlarmService.Instance.AddLog("API", $"E-Stop failed: {ex.GetType().Name}: {ex.Message}");
            }
        }

        public async Task JogAsync(int axis, double speed, double distance = 0)
        {
            // [防呆] 若機台非 IDLE 且非停止指令，禁止 JOG
            if (speed != 0 && _lastCachedStatus != null && _lastCachedStatus.Is_Moving)
            {
                AlarmService.Instance.AddLog("WARN", "JOG Ignored: Machine is moving.");
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

        /// <summary>
        /// [智慧啟動] 自動判斷是用 Run 還是 Resume
        /// 符合新架構規範：Idle -> Run, Paused -> Resume
        /// [更新] 支援 fileName 參數，用於指定執行檔
        /// </summary>
        public async Task CycleStartAsync(string file = null)
        {
            // 1. 本地狀態防呆
            if (_lastCachedStatus != null && _lastCachedStatus.Is_Moving)
            {
                // 如果正在移動中 (RUNNING 且非 PAUSED)，則不允許再次 Start
                // (注意：如果 Interp_State 是 RUNNING 但 Task_State 沒有 PAUSED 關鍵字，視為執行中)
                if (_lastCachedStatus.Interp_State == "RUNNING" && !_lastCachedStatus.Task_State.ToUpper().Contains("PAUSED"))
                {
                    AlarmService.Instance.AddLog("WARN", "Machine is already running.");
                    return;
                }
            }

            string endpoint = "program/run";
            object payload = new { file_name = file };

            // 2. 判斷是否為暫停狀態 -> 改發 Resume 指令
            if (_lastCachedStatus != null &&
               (_lastCachedStatus.Interp_State == "PAUSED" || _lastCachedStatus.Interp_State == "INTERP_PAUSED"))
            {
                endpoint = "program/resume";
                payload = new { }; // Resume 通常不需要參數
                AlarmService.Instance.AddLog("INFO", "Resuming Program...");
            }
            else
            {
                AlarmService.Instance.AddLog("INFO", $"Starting Program: {file ?? "Current"}...");
            }

            await SendV2CommandAsync(endpoint, payload);
        }

        public async Task StopAsync() => await SendV2CommandAsync("program/stop");
        public async Task FeedHoldAsync() => await SendV2CommandAsync("program/pause");

        public async Task<bool> SendMdiCommandAsync(string command)
        {
            if (string.IsNullOrWhiteSpace(command)) return false;
            try
            {
                var response = await _httpClient.PostAsJsonAsync($"{_serverUrl}/v2/mdi", new { command });
                if (!response.IsSuccessStatusCode) return false;
                var result = await response.Content.ReadFromJsonAsync<ApiResponse<object>>(_jsonOptions);
                if (result?.Status != "Success")
                {
                    AlarmService.Instance.AddLog("API", $"MDI Fail: {result?.Message}");
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                AlarmService.Instance.AddLog("API", $"MDI Exception: {ex.Message}");
                return false;
            }
        }

        public async Task<string> GetStartupLogAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync($"{_serverUrl}/api/machine/log");
                var result = await response.Content.ReadFromJsonAsync<LogResponse>(_jsonOptions);
                return result?.Log ?? "No Log";
            }
            catch (Exception ex)
            {
                AlarmService.Instance.AddLog("API", $"Startup log fetch failed: {ex.Message}");
                return "Log Unavailable";
            }
        }

        public async Task SaveConfigAndRestartAsync(List<AxisSetting> axes)
        {
            var payload = new { axes = axes };
            string url = $"{_serverUrl}/api/machine/save_config";

            // [安全] 使用獨立 HttpClient，避免修改全域 _httpClient.Timeout 而影響輪詢
            using var configClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var response = await configClient.PostAsJsonAsync(url, payload);

            if (!response.IsSuccessStatusCode)
            {
                string error = await response.Content.ReadAsStringAsync();
                throw new Exception($"Server Error: {error}");
            }
        }
    }
}