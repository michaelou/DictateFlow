using System.Windows;
using System.Windows.Threading;

namespace DictateFlow.App.Views;

/// <summary>
/// Small, topmost yes/no dialog that asks the user to approve one voice command before it runs.
/// Fail-closed: only an explicit <b>Yes</b> returns <see langword="true"/> — No, Escape, closing
/// the window, and the timeout all leave <see cref="Window.DialogResult"/> unset (denied). Unlike
/// the recording overlay, this dialog deliberately takes focus, because it requires a decision.
/// </summary>
public partial class CommandConfirmationWindow : Window
{
    private readonly DispatcherTimer _timeoutTimer;

    /// <summary>Initializes a new instance of the <see cref="CommandConfirmationWindow"/> class.</summary>
    /// <param name="commandName">The display name of the command awaiting approval.</param>
    /// <param name="timeout">How long to wait for an answer before denying automatically.</param>
    public CommandConfirmationWindow(string commandName, TimeSpan timeout)
    {
        CommandName = commandName;
        TimeoutNotice = $"Denied automatically after {(int)timeout.TotalSeconds} seconds if you don't respond.";
        InitializeComponent();
        DataContext = this;

        // Auto-deny on timeout: closing without setting DialogResult = true leaves it denied.
        _timeoutTimer = new DispatcherTimer { Interval = timeout };
        _timeoutTimer.Tick += (_, _) => Close();

        Loaded += (_, _) =>
        {
            _timeoutTimer.Start();
            Activate();
            YesButton.Focus();
        };
        Closed += (_, _) => _timeoutTimer.Stop();
    }

    /// <summary>Gets the display name of the command awaiting approval.</summary>
    public string CommandName { get; }

    /// <summary>Gets the line explaining the auto-deny timeout.</summary>
    public string TimeoutNotice { get; }

    private void OnYes(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
