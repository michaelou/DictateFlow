namespace DictateFlow.App.Services.Output;

/// <summary>
/// One simulated keyboard event, independent of the Win32 <c>INPUT</c> layout so the
/// text-to-events mapping stays pure and unit-testable.
/// </summary>
/// <param name="VirtualKey">Virtual-key code for key events (e.g. <c>VK_RETURN</c>); <c>0</c> for Unicode events.</param>
/// <param name="UnicodeCodeUnit">The UTF-16 code unit for Unicode events; <c>0</c> for key events.</param>
/// <param name="IsKeyUp">Whether this is a key release (each key/code unit is sent as a down+up pair).</param>
public readonly record struct KeyboardEvent(ushort VirtualKey, ushort UnicodeCodeUnit, bool IsKeyUp)
{
    /// <summary>Gets a value indicating whether this event carries a UTF-16 code unit instead of a virtual key.</summary>
    public bool IsUnicode => VirtualKey == 0;
}
