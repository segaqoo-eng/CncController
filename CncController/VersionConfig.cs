namespace CncController
{
    public static class VersionConfig
    {
        // 每次修改程式時，更新這個字串 
        // 格式: [Date]_SYNC_[Feature]
        public const string CurrentVersion = "2026.01.29_HARDWARE_VERIFY_02";

        // 隨機雜湊值 (標記本次建置)
        public const string RadomStr = "scan-v4-verify";
    }
}