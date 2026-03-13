// [2026-03-04] MonitorView code-behind：自動捲動 + 3D 視角控制
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Media3D;
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
        // [2026-03-13] 修正時序：MachineStatus 可能晚於 DataContext 設定，需額外監聽 VM.PropertyChanged
        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            // 解除舊訂閱
            if (_vm != null)
            {
                _vm.PropertyChanged -= OnVmPropertyChanged;
                if (_vm.MachineStatus != null)
                    _vm.MachineStatus.PropertyChanged -= OnStatusPropertyChanged;
            }

            _vm = DataContext as MonitorViewModel;
            if (_vm != null)
            {
                // 監聽 VM 屬性變化（等 MachineStatus 被設定時重新訂閱）
                _vm.PropertyChanged += OnVmPropertyChanged;
                if (_vm.MachineStatus != null)
                    _vm.MachineStatus.PropertyChanged += OnStatusPropertyChanged;
            }
        }

        // [2026-03-13] 當 MachineStatus 屬性被設定時，訂閱其 PropertyChanged
        private void OnVmPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(MonitorViewModel.MachineStatus)) return;
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

        // =========================================================
        // [2026-03-13] 3D 視角控制按鈕（code-behind 操作 HelixViewport3D Camera）
        // =========================================================

        // 置中充滿（ZoomExtents）
        private void OnFitClick(object sender, RoutedEventArgs e)
        {
            Viewport3D.ZoomExtents(500);
        }

        // ISO 等角視圖（45° 俯視）
        private void OnIsoClick(object sender, RoutedEventArgs e)
        {
            Viewport3D.Camera.Position = new Point3D(200, 200, 200);
            Viewport3D.Camera.LookDirection = new Vector3D(-200, -200, -200);
            Viewport3D.Camera.UpDirection = new Vector3D(0, 0, 1);
            Viewport3D.ZoomExtents(500);
        }

        // TOP 俯視圖（Z 軸正方向往下看）
        private void OnTopClick(object sender, RoutedEventArgs e)
        {
            Viewport3D.Camera.Position = new Point3D(0, 0, 300);
            Viewport3D.Camera.LookDirection = new Vector3D(0, 0, -300);
            Viewport3D.Camera.UpDirection = new Vector3D(0, 1, 0);
            Viewport3D.ZoomExtents(500);
        }

        // FRONT 正視圖（Y 軸正方向往後看）
        private void OnFrontClick(object sender, RoutedEventArgs e)
        {
            Viewport3D.Camera.Position = new Point3D(0, -300, 0);
            Viewport3D.Camera.LookDirection = new Vector3D(0, 300, 0);
            Viewport3D.Camera.UpDirection = new Vector3D(0, 0, 1);
            Viewport3D.ZoomExtents(500);
        }
    }
}
