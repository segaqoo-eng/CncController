namespace CncController.Models
{
    public enum UserRole
    {
        Operator,   // 操作員 (最低權限)
        Engineer,   // 工程師 (可修參數)
        Admin,      // 管理者 (最高權限：可進設定頁)
        Developer   // 開發者 (Debug用)
    }

    public class User
    {
        public string Username { get; set; }
        public UserRole Role { get; set; }

        // 用來判斷是否能進入設定頁面 (Admin 或 Developer)
        public bool CanAccessSettings => Role == UserRole.Admin || Role == UserRole.Developer;

        // 用來判斷是否能修改加工參數 (Engineer 以上)
        public bool CanEditParams => Role >= UserRole.Engineer;
    }
}