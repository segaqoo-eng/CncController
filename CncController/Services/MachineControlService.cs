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
        // [2026-03-10] ATC 專用（30s）：歸零/換刀/旋轉等長時間操作，避免阻塞輪詢通道
        private readonly HttpClient _atcClient;
        // [Item 11] 伺服器 URL 從 AppSettings 讀取，不再硬寫；可於 appsettings.json 修改
        private string _serverUrl = AppSettings.Instance.ServerUrl;
        private readonly JsonSerializerOptions _jsonOptions;

        private CancellationTokenSource _jogCts;

        // [安全] 連續失敗計數器：連續 N 次失敗才判定為 Disconnected，避免短暫網路波動誤報
        private int _consecutiveFailCount = 0;
        private const int MaxConsecutiveFailsBeforeDisconnect = 3;

        // [2026-03-04] 重置連線失敗計數（供 RETRY 按鈕呼叫）
        public void ResetFailCount() => _consecutiveFailCount = 0;

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
            // [2026-03-10] ATC 專用 HttpClient：歸零/換刀可能耗時 30 秒以上
            _atcClient     = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
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

        // [2026-03-04] 新增 SetBlockDeleteAsync：切換 Block Delete 開關
        public async Task<bool> SetBlockDeleteAsync(bool value)
        {
            try
            {
                var response = await _pollingClient.PostAsJsonAsync(
                    $"{_serverUrl}/v2/program/block_delete", new { value });
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                AlarmService.Instance.AddLog("API", $"SetBlockDelete failed: {ex.Message}");
                return false;
            }
        }

        // [2026-03-04] 新增 SetOptionalStopAsync：切換 Optional Stop (M01) 開關
        public async Task<bool> SetOptionalStopAsync(bool value)
        {
            try
            {
                var response = await _pollingClient.PostAsJsonAsync(
                    $"{_serverUrl}/v2/program/optional_stop", new { value });
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                AlarmService.Instance.AddLog("API", $"SetOptionalStop failed: {ex.Message}");
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

        // [2026-03-05] 通用 HAL Signal 設定：呼叫 /v2/hal/setp 設定 HAL 信號值
        public async Task<bool> HalSetSignalAsync(string signal, double value)
        {
            try
            {
                var response = await _pollingClient.PostAsJsonAsync(
                    $"{_serverUrl}/v2/hal/setp",
                    new { signal, value });
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                AlarmService.Instance.AddLog("API", $"HAL setp Exception: {ex.Message}");
                return false;
            }
        }

        // [2026-02-24] 新增 SetTaskModeAsync：切換任務模式（MANUAL/AUTO/MDI）
        public async Task<bool> SetTaskModeAsync(string mode)
        {
            try
            {
                var response = await _pollingClient.PostAsJsonAsync(
                    $"{_serverUrl}/v2/machine/mode", new { mode });
                if (!response.IsSuccessStatusCode) return false;
                var result = await response.Content.ReadFromJsonAsync<ApiResponse<object>>(_jsonOptions);
                if (result?.Status != "Success")
                {
                    AlarmService.Instance.AddLog("API", $"SetMode Fail: {result?.Message}");
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                AlarmService.Instance.AddLog("API", $"SetMode Exception: {ex.Message}");
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

        // [2026-03-03] 新增 GetToolTableAsync：從後端 /v2/tool/table 讀取刀具表
        public async Task<List<ToolEntry>> GetToolTableAsync()
        {
            try
            {
                var response = await _pollingClient.GetAsync($"{_serverUrl}/v2/tool/table");
                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content
                        .ReadFromJsonAsync<ApiResponse<List<ToolEntry>>>(_jsonOptions);
                    if (result?.Status == "Success")
                        return result.Data;
                }
            }
            catch (Exception ex)
            {
                AlarmService.Instance.AddLog("API", $"GetToolTable failed: {ex.Message}");
            }
            return null;
        }

        // [2026-03-03] 新增 SaveToolTableAsync：將刀具表寫入後端 /v2/tool/save
        public async Task<bool> SaveToolTableAsync(List<ToolEntry> tools)
        {
            try
            {
                var response = await _pollingClient.PostAsJsonAsync(
                    $"{_serverUrl}/v2/tool/save", new { tools });
                if (!response.IsSuccessStatusCode) return false;
                var result = await response.Content.ReadFromJsonAsync<ApiResponse<object>>(_jsonOptions);
                return result?.Status == "Success";
            }
            catch (Exception ex)
            {
                AlarmService.Instance.AddLog("API", $"SaveToolTable failed: {ex.Message}");
                return false;
            }
        }

        // [2026-03-04] 新增 RunProbeAsync：執行探測循環（POST /v2/probe/run）
        // 獨立使用 30s timeout HttpClient，因探測涉及多段移動
        // [2026-03-06] 新增 wcs/probePositionOnly 參數：後端直接寫入 WCS，避免時序衝突
        public async Task<ProbeResult> RunProbeAsync(
            string probeType, string direction, ProbeParameters parameters,
            string wcs = "", bool probePositionOnly = false)
        {
            try
            {
                // [2026-03-05] timeout 30s→120s：探測涉及多段慢速移動，30s 不夠
                using var probeClient = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
                var payload = new
                {
                    probe_type = probeType,
                    direction = direction,
                    probe_tool = parameters.ProbeToolNumber,  // [2026-03-06] 探針刀號（自動 G43 + 半徑補正）
                    traverse_speed = parameters.TraverseSpeed,
                    search_speed = parameters.SearchSpeed,
                    max_xy_distance = parameters.MaxXYDistance,
                    max_z_distance = parameters.MaxZDistance,
                    xy_clearance = parameters.XYClearance,
                    z_clearance = parameters.ZClearance,
                    extra_depth = parameters.ExtraDepth,
                    diameter = parameters.Diameter,   // [2026-03-04] Boss/Pocket 近似直徑
                    offset_x = parameters.OffsetX,    // [2026-03-04] 特徵中心近似偏移
                    offset_y = parameters.OffsetY,
                    edge_width = parameters.EdgeWidth,  // [2026-03-04] Edge Angle 邊緣寬度
                    wcs = wcs,                          // [2026-03-06] 目標座標系（G54~G59.3）
                    probe_position_only = probePositionOnly  // [2026-03-06] 僅顯示結果
                };
                var response = await probeClient.PostAsJsonAsync(
                    $"{_serverUrl}/v2/probe/run", payload);
                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content
                        .ReadFromJsonAsync<ApiResponse<ProbeResult>>(_jsonOptions);
                    if (result?.Status == "Success")
                        return result.Data;
                    return new ProbeResult { Error = result?.Message ?? "Unknown error" };
                }
                var errorBody = await response.Content.ReadAsStringAsync();
                return new ProbeResult { Error = $"HTTP {(int)response.StatusCode}: {errorBody}" };
            }
            catch (TaskCanceledException)
            {
                AlarmService.Instance.AddLog("ERROR", "Probe timeout (30s)");
                return new ProbeResult { Error = "探測超時（30 秒）" };
            }
            catch (Exception ex)
            {
                AlarmService.Instance.AddLog("ERROR", $"RunProbe failed: {ex.Message}");
                return new ProbeResult { Error = ex.Message };
            }
        }

        // =====================================================================
        // [2026-03-09] ATC 刀庫控制方法
        // =====================================================================

        // [2026-03-09] 讀取 ATC 即時狀態（IO + 刀位表 + 目前刀位）
        public async Task<AtcStatus> GetAtcStatusAsync()
        {
            try
            {
                var response = await _pollingClient.GetAsync($"{_serverUrl}/v2/atc/status");
                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadFromJsonAsync<ApiResponse<AtcStatus>>(_jsonOptions);
                    if (result?.Status == "Success") return result.Data;
                }
            }
            catch (Exception ex) { AlarmService.Instance.AddLog("API", $"GetAtcStatus failed: {ex.Message}"); }
            return null;
        }

        // [2026-03-09] ATC 通用 POST 命令（回傳 bool）
        // [2026-03-10] ATC 命令走專用 _atcClient（60s timeout），不阻塞輪詢通道
        private async Task<bool> SendAtcCommandAsync(string endpoint)
        {
            try
            {
                var response = await _atcClient.PostAsJsonAsync($"{_serverUrl}/v2/atc/{endpoint}", new { });
                if (!response.IsSuccessStatusCode) return false;
                var result = await response.Content.ReadFromJsonAsync<ApiResponse<object>>(_jsonOptions);
                return result?.Status == "Success";
            }
            catch (Exception ex) { AlarmService.Instance.AddLog("API", $"ATC {endpoint} failed: {ex.Message}"); return false; }
        }

        // [2026-03-10] ATC POST 命令帶 payload（專用 _atcClient）
        private async Task<bool> SendAtcCommandAsync(string endpoint, object payload)
        {
            try
            {
                var response = await _atcClient.PostAsJsonAsync($"{_serverUrl}/v2/atc/{endpoint}", payload);
                if (!response.IsSuccessStatusCode) return false;
                var result = await response.Content.ReadFromJsonAsync<ApiResponse<object>>(_jsonOptions);
                return result?.Status == "Success";
            }
            catch (Exception ex) { AlarmService.Instance.AddLog("API", $"ATC {endpoint} failed: {ex.Message}"); return false; }
        }

        // [2026-03-09] 旋轉刀盤到指定刀位
        public Task<bool> AtcRotateAsync(int pocket) => SendAtcCommandAsync("rotate", new { pocket });
        // [2026-03-09] 刀盤正轉一格
        public Task<bool> AtcFwdAsync() => SendAtcCommandAsync("fwd");
        // [2026-03-09] 刀盤反轉一格
        public Task<bool> AtcRevAsync() => SendAtcCommandAsync("rev");
        // [2026-03-09] 夾刀
        public Task<bool> AtcClampAsync() => SendAtcCommandAsync("clamp");
        // [2026-03-09] 鬆刀
        public Task<bool> AtcUnclampAsync() => SendAtcCommandAsync("unclamp");
        // [2026-03-09] 伸出刀盤
        public Task<bool> AtcExtendAsync() => SendAtcCommandAsync("extend");
        // [2026-03-09] 收回刀盤
        public Task<bool> AtcRetractAsync() => SendAtcCommandAsync("retract");
        // [2026-03-09] 刀庫歸零
        public Task<bool> AtcRefAsync() => SendAtcCommandAsync("ref");
        // [2026-03-09] Z 至淨空高度
        public Task<bool> AtcHeadUpAsync() => SendAtcCommandAsync("head_up");
        // [2026-03-09] Z 至換刀高度
        public Task<bool> AtcHeadDownAsync() => SendAtcCommandAsync("head_down");
        // [2026-03-09] 主軸定向
        public Task<bool> AtcOrientAsync() => SendAtcCommandAsync("orient");
        // [2026-03-09] 設定刀位對應
        public Task<bool> AtcSetSlotAsync(int slot, int toolNumber) => SendAtcCommandAsync("slot", new { slot, tool_number = toolNumber });

        // 輕量 DTO（僅供 GetOffsetsAsync 使用）
        private class OffsetsData
        {
            public string Active { get; set; }
            public Dictionary<string, Dictionary<string, double>> Offsets { get; set; }
        }
    }
}