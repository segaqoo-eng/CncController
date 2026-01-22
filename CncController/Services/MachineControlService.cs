using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;

namespace CncController.Services
{
    // 用於解析 /api/status 回傳的狀態
    public class MachineStatusResult
    {
        public bool Connected { get; set; }
        public string Msg { get; set; }
    }

    // 用於解析 /api/machine/log 回傳的日誌
    public class LogResult
    {
        public string Status { get; set; }
        public string Log { get; set; }
    }

    public class MachineControlService
    {
        // Singleton 實例
        public static MachineControlService Instance { get; } = new MachineControlService();

        private readonly HttpClient _http;

        private MachineControlService()
        {
            // ★★★ 設定目標 LinuxCNC 的 IP 位址 ★★★
            _http = new HttpClient { BaseAddress = new Uri("http://192.168.0.137:5000") };

            // 設定超時時間 (避免網路斷線時卡住介面太久)
            _http.Timeout = TimeSpan.FromSeconds(5);
        }

        /// <summary>
        /// 通用指令發送方法
        /// </summary>
        private async Task SendCommandAsync(string action, object value = null, object axis = null)
        {
            try
            {
                var payload = new { action, value, axis };
                var response = await _http.PostAsJsonAsync("/api/control", payload);
                response.EnsureSuccessStatusCode();
            }
            catch (Exception ex)
            {
                // 這裡可以改為記錄到 Log 檔或彈出 Toast 通知
                Console.WriteLine($"[Control Service] Error sending '{action}': {ex.Message}");
            }
        }

        // ==========================================
        // 1. 基本控制指令
        // ==========================================

        /// <summary>
        /// 設定緊急停止狀態
        /// </summary>
        /// <param name="active">true = 按下急停 (觸發 E-Stop), false = 解除急停 (Reset)</param>
        public async Task SetEstopAsync(bool active)
        {
            await SendCommandAsync("estop", active);
        }

        /// <summary>
        /// 設定機台電源
        /// </summary>
        /// <param name="on">true = 開啟電源 (Machine On), false = 關閉電源</param>
        public async Task SetPowerAsync(bool on)
        {
            await SendCommandAsync("power", on);
        }

        /// <summary>
        /// 執行軸回原點
        /// </summary>
        /// <param name="axisIndex">軸索引 (0=X, 1=Y...), 傳入 -1 代表全部軸回原點</param>
        public async Task HomeAxisAsync(int axisIndex)
        {
            await SendCommandAsync("home", axisIndex);
        }

        /// <summary>
        /// 開始 Jog 移動 (持續移動)
        /// </summary>
        /// <param name="axisIndex">軸索引</param>
        /// <param name="velocity">速度 (正值=正向, 負值=負向)</param>
        public async Task JogStartAsync(int axisIndex, double velocity)
        {
            await SendCommandAsync("jog", velocity, axisIndex);
        }

        /// <summary>
        /// 停止 Jog 移動
        /// </summary>
        /// <param name="axisIndex">軸索引</param>
        public async Task JogStopAsync(int axisIndex)
        {
            await SendCommandAsync("jog_stop", null, axisIndex);
        }

        /// <summary>
        /// 執行 MDI G-Code 指令 (例如 "M3 S1000")
        /// </summary>
        public async Task ExecuteMdiAsync(string gcode)
        {
            if (!string.IsNullOrWhiteSpace(gcode))
            {
                await SendCommandAsync("mdi", gcode);
            }
        }

        // ==========================================
        // 2. 狀態監控與診斷
        // ==========================================

        /// <summary>
        /// 檢查是否連線到 LinuxCNC (用於啟動時的輪詢等待)
        /// </summary>
        /// <returns>True 代表 LinuxCNC 已啟動並準備就緒</returns>
        public async Task<bool> CheckConnectionAsync()
        {
            try
            {
                // 呼叫 /api/status 檢查連線
                var response = await _http.GetAsync("/api/status");
                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadFromJsonAsync<MachineStatusResult>();
                    // 必須 connected 為 true 且沒有錯誤訊息
                    return result != null && result.Connected;
                }
            }
            catch
            {
                // 忽略連線錯誤 (視為未連線)
            }
            return false;
        }

        /// <summary>
        /// 獲取遠端 LinuxCNC 的啟動日誌 (startup.log)
        /// 用於診斷為什麼啟動失敗
        /// </summary>
        public async Task<string> GetStartupLogAsync()
        {
            try
            {
                var response = await _http.GetAsync("/api/machine/log");
                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadFromJsonAsync<LogResult>();
                    return result?.Log ?? "Log is empty.";
                }
                else
                {
                    return $"Server returned error: {response.StatusCode}";
                }
            }
            catch (Exception ex)
            {
                return $"Failed to retrieve log: {ex.Message}";
            }
        }
    }
}