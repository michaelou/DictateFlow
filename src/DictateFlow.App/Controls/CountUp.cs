using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace DictateFlow.App.Controls;

/// <summary>
/// Attached behaviour that animates a <see cref="TextBlock"/> from zero up to a target number,
/// formatting each frame. Set <c>controls:CountUp.To</c> (and optionally <c>controls:CountUp.Format</c>,
/// a standard numeric format string, default <c>"N0"</c>) on a TextBlock. The animation runs when
/// the value is first applied and again whenever it changes — so the cost cards re-count on refresh.
/// </summary>
public static class CountUp
{
    /// <summary>The target value to count up to. Bind this to the numeric metric.</summary>
    public static readonly DependencyProperty ToProperty = DependencyProperty.RegisterAttached(
        "To", typeof(double), typeof(CountUp), new PropertyMetadata(double.NaN, OnToChanged));

    /// <summary>Standard numeric format string applied to each frame (default <c>"N0"</c>).</summary>
    public static readonly DependencyProperty FormatProperty = DependencyProperty.RegisterAttached(
        "Format", typeof(string), typeof(CountUp), new PropertyMetadata("N0"));

    // Internal animated value; its change callback writes the formatted text.
    private static readonly DependencyProperty CurrentProperty = DependencyProperty.RegisterAttached(
        "Current", typeof(double), typeof(CountUp), new PropertyMetadata(0.0, OnCurrentChanged));

    /// <summary>Sets the count-up target on a TextBlock.</summary>
    public static void SetTo(DependencyObject element, double value) => element.SetValue(ToProperty, value);

    /// <summary>Gets the count-up target from a TextBlock.</summary>
    public static double GetTo(DependencyObject element) => (double)element.GetValue(ToProperty);

    /// <summary>Sets the numeric format string on a TextBlock.</summary>
    public static void SetFormat(DependencyObject element, string value) => element.SetValue(FormatProperty, value);

    /// <summary>Gets the numeric format string from a TextBlock.</summary>
    public static string GetFormat(DependencyObject element) => (string)element.GetValue(FormatProperty);

    private static void OnToChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock textBlock || e.NewValue is not double target || double.IsNaN(target))
        {
            return;
        }

        void Start()
        {
            var animation = new DoubleAnimation(0, target, new Duration(TimeSpan.FromMilliseconds(800)))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            };
            textBlock.BeginAnimation(CurrentProperty, animation);
        }

        if (textBlock.IsLoaded)
        {
            Start();
        }
        else
        {
            // Defer until the element is in the tree so the animation is visible.
            void Handler(object s, RoutedEventArgs args)
            {
                textBlock.Loaded -= Handler;
                Start();
            }

            textBlock.Loaded += Handler;
        }
    }

    private static void OnCurrentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TextBlock textBlock)
        {
            textBlock.Text = ((double)e.NewValue).ToString(GetFormat(textBlock), CultureInfo.CurrentCulture);
        }
    }
}
