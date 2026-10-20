namespace MouseFix.Core;

/// <summary>Reads and writes the system mouse settings.</summary>
public interface IMouseSettingsStore
{
    MouseSettings Read();

    void Write(MouseSettings settings);

    /// <summary>Display scaling of the primary monitor, in percent (100, 125, 150...).</summary>
    int GetDisplayScalePercent();
}
