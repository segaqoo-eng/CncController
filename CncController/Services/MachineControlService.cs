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

        // [安全] 各用途使用獨立 HttpClient，Timeout 互不影響
        private readonly HttpClient _pollingClient;  // 輪詢 + 一般指令（3s）
        private readonly HttpClient _estopClient;    // 急停專用（2s），最高優先
        // [Item 11] 伺服器 URL 從 AppSettings 讀取，不再硬寫；可於 appsettings.json 修改
        private string _serverUrl = AppSettings.Instance.ServerUrl;
        private readonly JsonSerializerOptions _jsonOptions;

        private CancellationTokenSource _jogCts;

        // [安全] 連續失敗計數器：連續 N 次失敗才判定為 Disconnected，避免短暫網路波動誤報
        private int _consecutiveFailCount = 0;
        private const int MaxConsecutiveFailsBeforeDisconnect = 3;

        // [核心] 用來記錄最後一次狀態，用於本地端的快速防呆判斷
        private MachineStatusData _lastCachedStatus;

        public string ServerVersion { get; private set; } = "Unknown";

        public enum ConnectionState
        {
            Disconnected,
            ServerOnly,
            Connected
        }

        // [Item 15] 機台動作類型（供狀態轉換驗證表使用）
        public enum MachineAction
        {
            EStop,
            Power,
            Jog,
            CycleStart,
            FeedHold,
            Stop,
            Mdi
        }

        /// <summary>
        /// [Item 15] 狀態轉換驗證表：依最後快取狀態判斷指定動作是否允許執行。
        /// 作為 Service 層的第二道防線（第一道在 ViewModel 的 CanExecuteMotion）。
        /// </summary>
        public (bool Allowed, string Reason) ValidateAction(MachineAction action)
        {
            // 未取得任何狀態時，僅允許急停（最保守策略）
            if (_lastCachedStatus == null)
                return action == MachineAction.EStop
                    ? (true, "")
                    : (false, "Machine status unknown; only E-Stop is allowed.");

            string taskState   = _lastCachedStatus.Task_State?.ToUpper() ?? "";
            string interpState = _lastCachedStatus.Interp_State?.ToUpper() ?? "";

            bool isEstop     = taskState.Contains("ESTOP");
            bool isPoweredOn = taskState == "ON";
            bool isRunning   = interpState == "RUNNING";
            bool isPaused    = interpState is "PAUSED" or "INTERP_PAUSED";

            return action switch
            {
                // 急停：永遠允許，不受任何狀態限制
                MachineAction.EStop => (true, ""),

                // 電源切換：急停未解除時禁止（需先 Reset）
                MachineAction.Power => isEstop
                    ? (false, "Cannot toggle Power while E-Stop is active. Reset first.")
                    : (true, ""),

                // JOG：需電源開啟、無急停、且目前非移動中
                MachineAction.Jog => (!isEstop && isPoweredOn && !isRunning && !isPaused)
                    ? (true, "")
                    : (false, $"JOG not allowed in state [{taskState}/{interpState}]"),

                // CycleStart：需電源開啟、無急停（暫停時允許 Resume）
                MachineAction.CycleStart => (!isEstop && isPoweredOn)
                    ? (true, "")
                    : (false, $"CycleStart not allowed in state [{taskState}/{interpState}]"),

                // FeedHold：僅在執行中有效
                MachineAction.FeedHold => isRunning
                    ? (true, "")
                    : (false, "FeedHold only valid when program is running."),

                // Stop：執行中或暫停時有效
                MachineAction.Stop => (isRunning || isPaused)
                    ? (true, "")
                    : (false, "Stop only valid when program is running or paused."),

                // MDI：需電源開啟、無急停、且非執行中
                MachineAction.Mdi => (!isEstop && isPoweredOn && !isRunning)
                    ? (true, "")
                    : (false, $"MDI not allowed in state [{taskState}/{interpState}]"),

                _ => (false, $"Unknown action: {action}")
            };
        }

        public MachineControlService()
        {
            _pollingClient = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            _estopClient   = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            _jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        }

        // --- 1. 狀態檢查 (同步後端狀態) ---
        public async Task<(ConnectionState State, MachineStatusData Data)> GetStatusAsync()
        {
            try
            {
                var response = await _pollingClient.GetAsync($"{_serverUrl}/v2/status");

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
                        _consecutiveFailCount = 0; // 成功，重置計數器
                        _lastCachedStatus = result.Data;
                        return (ConnectionState.Connected, result.Data);
                    }
                    else
                    {
                        _consecutiveFailCount = 0; // 伺服器可達，重置計數器
                        return (ConnectionState.ServerOnly, null);
                    }
                }
                else
                {
                    _consecutiveFailCount = 0; // HTTP 錯誤但伺服器可達
                    return (ConnectionState.ServerOnly, null);
                }
            }
            catch
            {
                // [安全] 連續失敗 N 次才判定 Disconnected，避免短暫波動誤報
                _consecutiveFailCount++;
                if (_consecutiveFailCount >= MaxConsecutiveFailsBeforeDisconnect)
                    return (ConnectionState.Disconnected, null);
                else
                    return (ConnectionState.ServerOnly, null);
            }
        }

        // --- 2. 錯誤查詢 ---
        public async Task<List<ErrorData>> GetErrorsAsync()
        {
            try
            {
                var response = await _pollingClient.GetAsync($"{_serverUrl}/v2/errors");
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
                var response = await _pollingClient.PostAsJsonAsync($"{_serverUrl}/v2/{endpoint}", payload ?? new { }, token);

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
                // 因為 _pollingClient 已經被狀態輪詢使用過，Timeout 屬性被鎖定不可修改。
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
            // [Item 15] 狀態轉換驗證表（Service 層第二防線）
            // 停止指令（speed==0）不受限制，確保 JOG 停止永遠可送出
            if (speed != 0)
            {
                var (allowed, reason) = ValidateAction(MachineAction.Jog);
                if (!allowed)
                {
                    AlarmService.Instance.AddLog("WARN", $"JOG Blocked: {reason}");
                    return;
                }
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
            // [Item 15] 狀態轉換驗證表（Service 層第二防線）
            var (cycleAllowed, cycleReason) = ValidateAction(MachineAction.CycleStart);
            if (!cycleAllowed)
            {
                AlarmService.Instance.AddLog("WARN", $"CycleStart Blocked: {cycleReason}");
                return;
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

        // [2026-02-23] 新增 HomeAsync：呼叫後端 /v2/machine/home 執行回原點（G28 MDI 不適用於 LinuxCNC）
        /// <summary>
        /// 全軸回原點（axis=-1）或單軸回原點（axis=0~5）
        /// 呼叫後端 /v2/machine/home，使用 cnc_cmd.home() 而非 G28 MDI
        /// </summary>
        public async Task<bool> HomeAsync(int axis = -1)
        {
            try
            {
                var response = await _pollingClient.PostAsJsonAsync(
                    $"{_serverUrl}/v2/machine/home", new { axis });
                if (!response.IsSuccessStatusCode) return false;
                var result = await response.Content.ReadFromJsonAsync<ApiResponse<object>>(_jsonOptions);
                if (result?.Status != "Success")
                {
                    AlarmService.Instance.AddLog("API", $"Home Fail: {result?.Message}");
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                AlarmService.Instance.AddLog("API", $"Home Exception: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> SendMdiCommandAsync(string command)
        {
            if (string.IsNullOrWhiteSpace(command)) return false;

            // [Item 15] Service 層第二道防線（與 JogAsync/CycleStartAsync 一致）
            var (allowed, reason) = ValidateAction(MachineAction.Mdi);
            if (!allowed)
            {
                AlarmService.Instance.AddLog("WARN", $"MDI Blocked: {reason}");
                return false;
            }

            try
            {
                var response = await _pollingClient.PostAsJsonAsync($"{_serverUrl}/v2/mdi", new { command });
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
                var response = await _pollingClient.GetAsync($"{_serverUrl}/api/machine/log");
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

            // [安全] 使用獨立 HttpClient，避免修改全域 _pollingClient.Timeout 而影響輪詢
            using var configClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var response = await configClient.PostAsJsonAsync(url, payload);

            if (!response.IsSuccessStatusCode)
            {
                string error = await response.Content.ReadAsStringAsync();
                throw new Exception($"Server Error: {error}");
            }
        }

        // [2026-02-24] 新增 SetFeedOverrideAsync：設定進給率覆蓋百分比（0~200%）
        public async Task<bool> SetFeedOverrideAsync(double percent)
        {
            try
            {
                var response = await _pollingClient.PostAsJsonAsync(
                    $"{_serverUrl}/v2/override/feed", new { value = percent });
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                AlarmService.Instance.AddLog("API", $"SetFeedOverride failed: {ex.Message}");
                return false;
            }
        }

        // [2026-02-24] 新增 SetSpindleOverrideAsync：設定主軸轉速覆蓋百分比（0~200%）
        public async Task<bool> SetSpindleOverrideAsync(double percent)
        {
            try
            {
                var response = await _pollingClient.PostAsJsonAsync(
                    $"{_serverUrl}/v2/override/spindle", new { value = percent });
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                AlarmService.Instance.AddLog("API", $"SetSpindleOverride failed: {ex.Message}");
                return false;
            }
        }

        // [2026-02-24] 新增 StepProgramAsync：單節執行（Single Block），每次僅執行一行 G-Code
        public async Task<bool> StepProgramAsync()
        {
            var (allowed, reason) = ValidateAction(MachineAction.CycleStart);
            if (!allowed)
            {
                AlarmService.Instance.AddLog("WARN", $"Step Blocked: {reason}");
                return false;
            }
            try
            {
                var response = await _pollingClient.PostAsJsonAsync(
                    $"{_serverUrl}/v2/program/step", new { });
                if (!response.IsSuccessStatusCode) return false;
                var result = await response.Content.ReadFromJsonAsync<ApiResponse<object>>(_jsonOptions);
                if (result?.Status != "Success")
                {
                    AlarmService.Instance.AddLog("API", $"Step Fail: {result?.Message}");
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                AlarmService.Instance.AddLog("API", $"Step Exception: {ex.Message}");
                return false;
            }
        }

        // [2026-02-23] 新增 GetOffsetsAsync：從後端 /v2/offsets 讀取 G54–G59 offset 值
        /// <summary>
        /// 取得 G54–G59 工件座標系偏移值（從後端 /v2/offsets）
        /// </summary>
        public async Task<Dictionary<string, Dictionary<string, double>>> GetOffsetsAsync()
        {
            try
            {
                var response = await _pollingClient.GetAsync($"{_serverUrl}/v2/offsets");
                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content
                        .ReadFromJsonAsync<ApiResponse<OffsetsData>>(_jsonOptions);
                    return result?.Data?.Offsets;
                }
            }
            catch (Exception ex)
            {
                AlarmService.Instance.AddLog("API", $"GetOffsets failed: {ex.Message}");
            }
            return null;
        }

        // 輕量 DTO（僅供 GetOffsetsAsync 使用）
        private class OffsetsData
        {
            public string Active { get; set; }
            public Dictionary<string, Dictionary<string, double>> Offsets { get; set; }
        }
    }
}