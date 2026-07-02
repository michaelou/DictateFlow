using System.Runtime.InteropServices;
using DictateFlow.App.Interop;

namespace DictateFlow.App.Services.Output;

/// <summary>
/// Thin <c>SendInput</c> shell around <see cref="KeyboardEvent"/> sequences. All decision
/// logic lives in <see cref="KeyboardInputMapper"/>; this class only marshals the events
/// into <c>INPUT</c> structs. Verified manually — there is no unit-testable behavior here.
/// </summary>
internal static class KeyboardInjector
{
    /// <summary>The key-down/up events that produce a Ctrl+V paste chord.</summary>
    private static readonly KeyboardEvent[] CtrlV =
    [
        new(NativeMethods.VkControl, 0, IsKeyUp: false),
        new(NativeMethods.VkV, 0, IsKeyUp: false),
        new(NativeMethods.VkV, 0, IsKeyUp: true),
        new(NativeMethods.VkControl, 0, IsKeyUp: true),
    ];

    /// <summary>Sends <paramref name="events"/> in a single <c>SendInput</c> call.</summary>
    /// <param name="events">The events to inject.</param>
    /// <returns>The number of events the system accepted (equal to the count on success).</returns>
    public static uint Send(IReadOnlyList<KeyboardEvent> events)
    {
        if (events.Count == 0)
        {
            return 0;
        }

        var inputs = new NativeMethods.Input[events.Count];
        for (var i = 0; i < events.Count; i++)
        {
            var e = events[i];
            var flags = e.IsUnicode ? NativeMethods.KeyEventFUnicode : 0u;
            if (e.IsKeyUp)
            {
                flags |= NativeMethods.KeyEventFKeyUp;
            }

            inputs[i] = new NativeMethods.Input
            {
                Type = NativeMethods.InputKeyboard,
                Union = new NativeMethods.InputUnion
                {
                    Keyboard = new NativeMethods.KeybdInput
                    {
                        Vk = e.VirtualKey,
                        Scan = e.UnicodeCodeUnit,
                        Flags = flags,
                    },
                },
            };
        }

        return NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.Input>());
    }

    /// <summary>Sends the Ctrl+V paste chord to the foreground window.</summary>
    /// <returns>The number of events the system accepted (4 on success).</returns>
    public static uint SendCtrlV() => Send(CtrlV);
}
