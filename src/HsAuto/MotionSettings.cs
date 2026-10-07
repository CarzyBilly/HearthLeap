using System.Windows;
namespace HsAuto;
public static class MotionSettings
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached("Enabled", typeof(bool), typeof(MotionSettings), new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.Inherits));
    public static bool GetEnabled(DependencyObject o) => (bool)o.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject o, bool value) => o.SetValue(EnabledProperty, value);
}
