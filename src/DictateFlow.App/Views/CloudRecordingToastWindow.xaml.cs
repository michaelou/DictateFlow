using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using DictateFlow.App.Interop;

namespace DictateFlow.App.Views;

/// <summary>
/// A persistent, borderless notification anchored to the bottom-right of the primary screen,
/// shown when a new cloud recording is detected. Unlike a tray balloon it never auto-dismisses:
/// it stays until the user clicks it (which raises <see cref="Clicked"/>) or dismisses it with
/// the close button (<see cref="Dismissed"/>). The window never steals focus from the app the
/// user is working in.
/// </summary>
public partial class CloudRecordingToastWindow : Window
{
    /// <summary>Gap in pixels between the toast and the screen edges (above the taskbar).</summary>
    private const double EdgeMargin = 16;

    /// <summary>Identifies the <see cref="Message"/> dependency property.</summary>
    public static readonly DependencyProperty MessageProperty = DependencyProperty.Register(
        nameof(Message), typeof(string), typeof(CloudRecordingToastWindow), new PropertyMetadata(""));

    /// <summary>Initializes a new instance of the <see cref="CloudRecordingToastWindow"/> class.</summary>
    public CloudRecordingToastWindow()
    {
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
        SizeChanged += (_, _) => PositionWindow();
    }

    /// <summary>Raised when the user clicks the notification body (to open the review window).</summary>
    public event EventHandler? Clicked;

    /// <summary>Raised when the user dismisses the notification with the close button.</summary>
    public event EventHandler? Dismissed;

    /// <summary>Gets or sets the notification body text.</summary>
    public string Message
    {
        get => (string)GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    /// <summary>Shows the toast at the bottom-right corner without activating it.</summary>
    public void ShowToast()
    {
        if (!IsVisible)
        {
            Show();
        }

        PositionWindow();
    }

    private void OnBodyClicked(object sender, MouseButtonEventArgs e) => Clicked?.Invoke(this, EventArgs.Empty);

    private void OnDismissClicked(object sender, RoutedEventArgs e)
    {
        // Don't let the click bubble to the body handler and open the window as well.
        e.Handled = true;
        Dismissed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Marks the window non-activatable and hidden from Alt+Tab so it never steals focus.</summary>
    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        var exStyle = NativeMethods.GetWindowLongPtr(handle, NativeMethods.GwlExStyle).ToInt64();
        // No WS_EX_TRANSPARENT here: the toast must remain clickable.
        exStyle |= NativeMethods.WsExNoActivate | NativeMethods.WsExToolWindow;
        NativeMethods.SetWindowLongPtr(handle, NativeMethods.GwlExStyle, new IntPtr(exStyle));
    }

    /// <summary>Places the toast in the bottom-right corner of the primary work area.</summary>
    private void PositionWindow()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Right - ActualWidth - EdgeMargin;
        Top = area.Bottom - ActualHeight - EdgeMargin;
    }
}
