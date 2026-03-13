using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging.Messages;

namespace CncController.Models
{
    // [2026-03-12] 從 MachineModels.cs 拆分：程式檔案 & 巨集變數

    // [2026-03-13] Messenger 訊息：FILE 頁載入遠端程式 → MAIN 頁顯示
    public class ProgramLoadedMessage : ValueChangedMessage<string>
    {
        public string FileName { get; }
        public string Content { get; }
        public bool IsRemote { get; }

        public ProgramLoadedMessage(string fileName, string content, bool isRemote)
            : base(content)
        {
            FileName = fileName;
            Content = content;
            IsRemote = isRemote;
        }
    }

    // [2026-03-13] Messenger 訊息：自動導航回 MAIN 頁
    public class NavigateToMainMessage : ValueChangedMessage<bool>
    {
        public NavigateToMainMessage() : base(true) { }
    }

    public class ProgramFileInfo
    {
        public string Name { get; set; } = "";
        public long Size { get; set; }
        public double Modified { get; set; }
    }

    public class ProgramReadResult
    {
        public string Name { get; set; } = "";
        public string Content { get; set; } = "";
    }

    public partial class MacroVariable : ObservableObject
    {
        [ObservableProperty] private int _id;
        [ObservableProperty] private double _value;
        [ObservableProperty] private string _name = "";
        [ObservableProperty] private bool _isModified;
    }
}
