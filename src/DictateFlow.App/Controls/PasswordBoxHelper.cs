using System.Windows;
using System.Windows.Controls;

namespace DictateFlow.App.Controls;

/// <summary>
/// Attached property that makes <see cref="PasswordBox.Password"/> bindable
/// (<see cref="PasswordBox"/> deliberately has no dependency property for it).
/// Usage: <c>controls:PasswordBoxHelper.BoundPassword="{Binding SpeechApiKey, Mode=TwoWay}"</c>.
/// </summary>
public static class PasswordBoxHelper
{
    /// <summary>Identifies the <c>BoundPassword</c> attached property.</summary>
    public static readonly DependencyProperty BoundPasswordProperty = DependencyProperty.RegisterAttached(
        "BoundPassword",
        typeof(string),
        typeof(PasswordBoxHelper),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnBoundPasswordChanged));

    /// <summary>Guards against re-entrancy while the box itself pushes a change to the binding.</summary>
    private static readonly DependencyProperty IsUpdatingProperty = DependencyProperty.RegisterAttached(
        "IsUpdating", typeof(bool), typeof(PasswordBoxHelper), new PropertyMetadata(false));

    /// <summary>Gets the bound password value.</summary>
    /// <param name="element">The password box the property is attached to.</param>
    public static string GetBoundPassword(DependencyObject element) => (string)element.GetValue(BoundPasswordProperty);

    /// <summary>Sets the bound password value.</summary>
    /// <param name="element">The password box the property is attached to.</param>
    /// <param name="value">The new password value.</param>
    public static void SetBoundPassword(DependencyObject element, string value) => element.SetValue(BoundPasswordProperty, value);

    private static void OnBoundPasswordChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not PasswordBox box)
        {
            return;
        }

        box.PasswordChanged -= OnPasswordChanged;
        if (!(bool)box.GetValue(IsUpdatingProperty))
        {
            box.Password = (string?)e.NewValue ?? string.Empty;
        }

        box.PasswordChanged += OnPasswordChanged;
    }

    private static void OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        var box = (PasswordBox)sender;
        box.SetValue(IsUpdatingProperty, true);
        SetBoundPassword(box, box.Password);
        box.SetValue(IsUpdatingProperty, false);
    }
}
