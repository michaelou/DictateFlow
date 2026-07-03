using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using DictateFlow.App.Interop;

namespace DictateFlow.App.Views;

/// <summary>
/// Borderless, topmost, click-through recording indicator shown bottom-center on the screen
/// containing the focused window (falling back to the primary screen). The code-behind only
/// handles window chrome concerns — extended styles that prevent focus stealing, positioning
/// and the fade in/out animations; all content is bound to
/// <see cref="ViewModels.OverlayViewModel"/>.
/// </summary>
public partial class OverlayWindow : Window
{
    /// <summary>Distance in pixels between the overlay and the top of the taskbar.</summary>
    private const double BottomMargin = 80;

    private static readonly Duration FadeDuration = new(TimeSpan.FromMilliseconds(140));

    /// <summary>Set while a fade-out is running so its completion knows whether to still hide.</summary>
    private bool _hiding;

    /// <summary>Initializes a new instance of the <see cref="OverlayWindow"/> class.</summary>
    public OverlayWindow()
    {
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
        SizeChanged += (_, _) => PositionWindow();
    }

    /// <summary>
    /// Shows the overlay with a subtle fade-in. Calling this while a fade-out is running
    /// cancels the hide, so quick state changes (listening → processing) never blink.
    /// </summary>
    public void FadeIn()
    {
        _hiding = false;
        if (!IsVisible)
        {
            Opacity = 0;
            Show();
            PositionWindow();
        }

        BeginAnimation(OpacityProperty, new DoubleAnimation(1.0, FadeDuration));
    }

    /// <summary>Fades the overlay out, then hides the window.</summary>
    public void FadeOut()
    {
        if (!IsVisible || _hiding)
        {
            return;
        }

        _hiding = true;
        var animation = new DoubleAnimation(0.0, FadeDuration);
        animation.Completed += (_, _) =>
        {
            if (_hiding)
            {
                _hiding = false;
                Hide();
            }
        };
        BeginAnimation(OpacityProperty, animation);
    }

    /// <summary>
    /// Marks the window as non-activatable, click-through and hidden from Alt+Tab so it can
    /// never steal focus or input from the application the user is dictating into.
    /// </summary>
    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        var exStyle = NativeMethods.GetWindowLongPtr(handle, NativeMethods.GwlExStyle).ToInt64();
        exStyle |= NativeMethods.WsExNoActivate | NativeMethods.WsExTransparent | NativeMethods.WsExToolWindow;
        NativeMethods.SetWindowLongPtr(handle, NativeMethods.GwlExStyle, new IntPtr(exStyle));
    }

    /// <summary>Places the overlay bottom-center on the target screen, above the taskbar.</summary>
    private void PositionWindow()
    {
        var area = GetTargetWorkArea();
        Left = area.Left + ((area.Width - ActualWidth) / 2);
        Top = area.Bottom - ActualHeight - BottomMargin;
    }

    /// <summary>
    /// The work area (in DIPs) of the monitor containing the focused window — the overlay
    /// never activates, so the foreground window is the one being dictated into. Falls back
    /// to the primary work area when there is no usable foreground window.
    /// </summary>
    private Rect GetTargetWorkArea()
    {
        try
        {
            var foreground = NativeMethods.GetForegroundWindow();
            if (foreground != IntPtr.Zero)
            {
                var monitor = NativeMethods.MonitorFromWindow(foreground, NativeMethods.MonitorDefaultToNearest);
                var info = NativeMethods.MonitorInfo.Create();
                if (monitor != IntPtr.Zero && NativeMethods.GetMonitorInfo(monitor, ref info))
                {
                    // MONITORINFO is in device pixels; the window's transform converts to DIPs.
                    var transform = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice;
                    if (transform.HasValue)
                    {
                        return new Rect(
                            transform.Value.Transform(new Point(info.Work.Left, info.Work.Top)),
                            transform.Value.Transform(new Point(info.Work.Right, info.Work.Bottom)));
                    }
                }
            }
        }
        catch (Exception)
        {
            // Positioning must never take a dictation down; the primary screen is always safe.
        }

        return SystemParameters.WorkArea;
    }
}
