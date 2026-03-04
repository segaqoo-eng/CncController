// [2026-03-04] 新增 GCodeLineItem：G-Code 逐行模型（供 MonitorView 行號高亮使用）
using CommunityToolkit.Mvvm.ComponentModel;

namespace CncController.Models
{
    public partial class GCodeLineItem : ObservableObject
    {
        public int LineNumber { get; set; }
        public string Text { get; set; } = "";

        // [2026-03-04] 當前執行行標記（True 時 UI 黃底高亮）
        [ObservableProperty] private bool _isCurrentLine;
    }
}
