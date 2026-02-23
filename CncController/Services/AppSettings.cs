using System;
using System.IO;
using System.Text.Json;

namespace CncController.Services
{
    /// <summary>
    /// [Item 11] 統一應用程式設定：從 appsettings.json 讀取，缺少時自動建立預設檔。
    /// 解決伺服器 IP 硬寫於三個 Service 中的問題。
    /// </summary>
    public class AppSettings
    {
        public static AppSettings Instance { get; } = new AppSettings();

        private const string FileName = "appsettings.json";

        /// <summary>後端伺服器 URL（含 port）</summary>
        public string ServerUrl { get; private set; } = "http://192.168.0.137:5000";

        private readonly JsonSerializerOptions _jsonOpts = new() { WriteIndented = true };

        private AppSettings()
        {
            Load();
        }

        private void Load()
        {
            try
            {
                if (File.Exists(FileName))
                {
                    string json = File.ReadAllText(FileName);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("ServerUrl", out var urlProp))
                    {
                        string? url = urlProp.GetString();
                        if (!string.IsNullOrWhiteSpace(url))
                            ServerUrl = url.TrimEnd('/');
                    }
                }
                else
                {
                    // 首次執行：建立預設設定檔，方便使用者直接修改 IP
                    Save();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AppSettings] Load failed: {ex.Message}, using default URL.");
            }
        }

        /// <summary>將目前設定寫回 appsettings.json</summary>
        public void Save()
        {
            try
            {
                var data = new { ServerUrl };
                string json = JsonSerializer.Serialize(data, _jsonOpts);
                File.WriteAllText(FileName, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AppSettings] Save failed: {ex.Message}");
            }
        }

        /// <summary>更新 ServerUrl 並持久化（供設定介面使用）</summary>
        public void UpdateServerUrl(string newUrl)
        {
            if (!string.IsNullOrWhiteSpace(newUrl))
            {
                ServerUrl = newUrl.TrimEnd('/');
                Save();
            }
        }
    }
}
