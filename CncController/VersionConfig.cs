namespace CncController
{
    public static class VersionConfig
    {
        // 每次修改程式時，更新這個字串 
        // 格式: [Date]_SYNC_[Feature]
        public const string CurrentVersion = "2026.02.02_HARDWARE_VERIFY_01";

        // 隨機雜湊值 (標記本次建置)
        public const string RadomStr = "scan-v1-verify";
    }
}