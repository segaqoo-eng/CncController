using System.Collections.Generic;
using System.ComponentModel;

namespace CncController.Services
{
    public class LocalizationService : INotifyPropertyChanged
    {
        public static LocalizationService Instance { get; } = new LocalizationService();
        public event PropertyChangedEventHandler? PropertyChanged;

        // 索引器
        public string this[string key] => key; // 暫時直接回傳 Key，實際應查表
    }
}