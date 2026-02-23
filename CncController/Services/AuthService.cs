using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CncController.Models;

namespace CncController.Services
{
    // --- 密碼設定檔 Model ---

    public class UserCredential
    {
        public string Username { get; set; }
        public string Role { get; set; }
        public string PasswordHash { get; set; } // SHA256(Salt:Password) hex 字串
    }

    public class PasswordConfig
    {
        public string Salt { get; set; }
        public List<UserCredential> Users { get; set; } = new();
    }

    // --- AuthService ---

    public class AuthService
    {
        public static AuthService Instance { get; } = new AuthService();

        private const string PasswordFile = "passwords.json";
        private PasswordConfig _passwordConfig;
        private readonly JsonSerializerOptions _jsonOpts = new() { WriteIndented = true };

        private AuthService()
        {
            _currentUser = new User { Username = "Operator", Role = UserRole.Operator };
            LoadOrCreatePasswordConfig();
        }

        // ---------------------------------------------------------------
        // 密碼設定檔載入 / 建立
        // ---------------------------------------------------------------

        private void LoadOrCreatePasswordConfig()
        {
            try
            {
                if (File.Exists(PasswordFile))
                {
                    string json = File.ReadAllText(PasswordFile);
                    _passwordConfig = JsonSerializer.Deserialize<PasswordConfig>(json, _jsonOpts);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AuthService] Password config load error: {ex.Message}");
                _passwordConfig = null;
            }

            // 若設定檔不存在、讀取失敗或內容空白，建立預設設定並寫出
            if (_passwordConfig == null || _passwordConfig.Users == null || _passwordConfig.Users.Count == 0)
            {
                _passwordConfig = CreateDefaultConfig();
                SavePasswordConfig();
            }
        }

        private static PasswordConfig CreateDefaultConfig()
        {
            // 產生隨機 16 字元 Salt，每次建立時不同
            string salt = Guid.NewGuid().ToString("N")[..16];
            return new PasswordConfig
            {
                Salt = salt,
                Users = new List<UserCredential>
                {
                    new() { Username = "Operator",  Role = "Operator",  PasswordHash = HashPassword(salt, "1111") },
                    new() { Username = "Engineer",  Role = "Engineer",  PasswordHash = HashPassword(salt, "2222") },
                    new() { Username = "Admin",     Role = "Admin",     PasswordHash = HashPassword(salt, "8888") },
                    new() { Username = "Developer", Role = "Developer", PasswordHash = HashPassword(salt, "dev999") },
                }
            };
        }

        private void SavePasswordConfig()
        {
            try
            {
                string json = JsonSerializer.Serialize(_passwordConfig, _jsonOpts);
                File.WriteAllText(PasswordFile, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AuthService] Password config save error: {ex.Message}");
            }
        }

        // SHA256(Salt:Password) → 小寫 hex 字串
        internal static string HashPassword(string salt, string password)
        {
            using var sha = SHA256.Create();
            byte[] bytes = Encoding.UTF8.GetBytes($"{salt}:{password}");
            byte[] hash = sha.ComputeHash(bytes);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        // ---------------------------------------------------------------
        // 公開介面
        // ---------------------------------------------------------------

        public event Action<User> CurrentUserChanged;

        private User _currentUser;
        public User CurrentUser
        {
            get => _currentUser;
            private set => _currentUser = value;
        }

        public bool Login(string password)
        {
            if (string.IsNullOrEmpty(password)) return false;

            string inputHash = HashPassword(_passwordConfig.Salt, password);
            var credential = _passwordConfig.Users.FirstOrDefault(u =>
                string.Equals(u.PasswordHash, inputHash, StringComparison.OrdinalIgnoreCase));

            if (credential == null)
            {
                // [安全] 不記錄輸入的密碼，避免敏感資料進入日誌
                AlarmService.Instance.AddLog("LOGIN_FAIL", "Failed login attempt");
                return false;
            }

            if (!Enum.TryParse<UserRole>(credential.Role, out var role))
            {
                AlarmService.Instance.AddLog("LOGIN_FAIL", $"Invalid role in passwords.json: {credential.Role}");
                return false;
            }

            CurrentUser = new User { Username = credential.Username, Role = role };
            AlarmService.Instance.AddLog("LOGIN", $"User '{CurrentUser.Username}' logged in (Role: {CurrentUser.Role})");
            CurrentUserChanged?.Invoke(CurrentUser);
            return true;
        }

        public void Logout()
        {
            if (CurrentUser.Role != UserRole.Operator)
            {
                AlarmService.Instance.AddLog("LOGOUT", $"User '{CurrentUser.Username}' logged out");
            }
            CurrentUser = new User { Username = "Operator", Role = UserRole.Operator };
            CurrentUserChanged?.Invoke(CurrentUser);
        }
    }
}
