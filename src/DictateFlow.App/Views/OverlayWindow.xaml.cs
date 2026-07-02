using System.Windows;
using System.Windows.Interop;
using DictateFlow.App.Interop;

namespace DictateFlow.App.Views;

/// <summary>
/// Borderless, topmost, click-through recording indicator shown at the bottom-center of the
/// primary screen. The code-behind only handles window chrome concerns (extended styles that
/// prevent focus stealing, and positioning); all content is bound to
/// <see cref="ViewModels.OverlayViewModel"/>.
/// </summary>
public partial class OverlayWindow : Window
{
    /// <summary>Distance in pixels between the overlay and the top of the taskbar.</summary>
    private const double BottomMargin = 80;

    /// <summary>Initializes a new instance of the <see cref="OverlayWindow"/> class.</summary>
    public OverlayWindow()
    {
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
        SizeChanged += (_, _) => PositionWindow();
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

    /// <summary>Places the overlay bottom-center on the primary screen, above the taskbar.</summary>
    private void PositionWindow()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Left + ((area.Width - ActualWidth) / 2);
        Top = area.Bottom - ActualHeight - BottomMargin;
    }
}
