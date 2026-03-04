// [2026-03-04] MonitorView code-behind：自動捲動至 G-Code 執行中行
using System.Windows.Controls;
using CncController.ViewModels;
using CncController.Models;

namespace CncController.Views.Pages
{
    public partial class MonitorView : UserControl
    {
        public MonitorView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
        }

        private MonitorViewModel _vm;

        // [2026-03-04] DataContext 變更時訂閱 MachineStatus.PropertyChanged
        private void OnDataContextChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e)
        {
            if (_vm != null && _vm.MachineStatus != null)
                _vm.MachineStatus.PropertyChanged -= OnStatusPropertyChanged;

            _vm = DataContext as MonitorViewModel;
            if (_vm?.MachineStatus != null)
                _vm.MachineStatus.PropertyChanged += OnStatusPropertyChanged;
        }

        // [2026-03-04] CurrentLine 變化時自動捲動 ListBox 至對應行
        private void OnStatusPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(MachineStatus.CurrentLine)) return;
            if (_vm == null) return;

            int lineIdx = _vm.MachineStatus.CurrentLine - 1;
            if (lineIdx >= 0 && lineIdx < _vm.GCodeLines.Count)
            {
                Dispatcher.InvokeAsync(() =>
                {
                    GCodeListBox.ScrollIntoView(_vm.GCodeLines[lineIdx]);
                });
            }
        }
    }
}
