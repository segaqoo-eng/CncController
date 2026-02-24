// [2026-02-24] BindingProxy：解決 DataGridColumn 不在 Visual Tree 無法綁定 DataContext 的問題
//              透過 Freezable 繼承，讓 DataGridColumn.Visibility 可綁定 ViewModel 屬性
using System.Windows;

namespace CncController.Helpers
{
    public class BindingProxy : Freezable
    {
        protected override Freezable CreateInstanceCore() => new BindingProxy();

        public object Data
        {
            get => GetValue(DataProperty);
            set => SetValue(DataProperty, value);
        }

        public static readonly DependencyProperty DataProperty =
            DependencyProperty.Register(nameof(Data), typeof(object), typeof(BindingProxy), new UIPropertyMetadata(null));
    }
}
