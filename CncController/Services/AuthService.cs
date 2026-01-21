using System;
using CncController.Models;

namespace CncController.Services
{
    public class AuthService
    {
        // 單例模式
        public static AuthService Instance { get; } = new AuthService();

        private AuthService()
        {
            // 建構時不自動寫 Log，避免程式一啟動就寫一筆 Logout
            // 初始化為預設操作員
            _currentUser = new User { Username = "Operator", Role = UserRole.Operator };
        }

        // 當使用者改變時觸發事件 (讓 ViewModel 更新 UI)
        public event Action<User> CurrentUserChanged;

        private User _currentUser;
        public User CurrentUser
        {
            get => _currentUser;
            private set => _currentUser = value;
        }

        public bool Login(string password)
        {
            User newUser = null;

            // 簡單的密碼驗證邏輯
            switch (password)
            {
                case "1111": // 操作員
                    newUser = new User { Username = "Operator", Role = UserRole.Operator };
                    break;
                case "2222": // 工程師
                    newUser = new User { Username = "Engineer", Role = UserRole.Engineer };
                    break;
                case "8888": // 管理者 (最高權限)
                    newUser = new User { Username = "Admin", Role = UserRole.Admin };
                    break;
                case "dev999": // 開發者
                    newUser = new User { Username = "Developer", Role = UserRole.Developer };
                    break;
                default:
                    // 登入失敗也可以選擇紀錄，視需求而定
                    AlarmService.Instance.AddLog("LOGIN_FAIL", $"Failed login attempt with password: {password}");
                    return false;
            }

            CurrentUser = newUser;

            // ★★★ [修正] 這裡加入寫入登入履歷 ★★★
            AlarmService.Instance.AddLog("LOGIN", $"User '{CurrentUser.Username}' logged in (Role: {CurrentUser.Role})");

            CurrentUserChanged?.Invoke(CurrentUser);
            return true;
        }

        public void Logout()
        {
            // 紀錄登出 (在切換身份前)
            if (CurrentUser.Role != UserRole.Operator)
            {
                AlarmService.Instance.AddLog("LOGOUT", $"User '{CurrentUser.Username}' logged out");
            }

            // 登出後自動變回操作員
            CurrentUser = new User { Username = "Operator", Role = UserRole.Operator };
            CurrentUserChanged?.Invoke(CurrentUser);
        }
    }
}