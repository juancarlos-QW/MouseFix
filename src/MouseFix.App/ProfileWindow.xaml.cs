using System.Diagnostics;
using System.Windows;
using MouseFix.Core;
using Wpf.Ui.Controls;

namespace MouseFix.App;

/// <summary>Picks a program and a speed for a new <see cref="SpeedProfile"/>.</summary>
public partial class ProfileWindow : FluentWindow
{
    private sealed record ProgramChoice(string ProcessName, string DisplayName)
    {
        public string Label => DisplayName == ProcessName ? ProcessName : $"{DisplayName} ({ProcessName})";

        // Shown in the editable box after picking an item.
        public override string ToString() => ProcessName;
    }

    public ProfileWindow(int initialSpeed)
    {
        InitializeComponent();
        ProgramBox.ItemsSource = ListPrograms();
        SpeedSlider.Value = initialSpeed;
        UpdateSpeedLabel();
    }

    /// <summary>The profile to add, set when the user clicks "Adicionar".</summary>
    public SpeedProfile? Result { get; private set; }

    // Known programs (e.g. the emulator) first, even if closed, then programs that have a window open.
    private static List<ProgramChoice> ListPrograms()
    {
        var choices = SpeedProfiles.KnownPrograms
            .GroupBy(k => k.Value)
            .Select(g => new ProgramChoice(g.First().Key, g.Key))
            .ToList();

        foreach (var p in Process.GetProcesses())
        {
            using (p)
            {
                if (p.MainWindowHandle == 0 || p.Id == Environment.ProcessId)
                    continue;
                if (choices.Any(c => c.ProcessName.Equals(p.ProcessName, StringComparison.OrdinalIgnoreCase)))
                    continue;
                choices.Add(new ProgramChoice(p.ProcessName, FriendlyName(p)));
            }
        }
        return choices;
    }

    private static string FriendlyName(Process p)
    {
        try
        {
            var description = p.MainModule?.FileVersionInfo.FileDescription;
            if (!string.IsNullOrWhiteSpace(description))
                return description.Trim();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // Programs running as administrator can't be inspected; the process name is enough.
        }
        return p.ProcessName;
    }

    private void SpeedSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (SpeedLabel is not null)
            UpdateSpeedLabel();
    }

    private void UpdateSpeedLabel()
    {
        int speed = (int)SpeedSlider.Value;
        SpeedLabel.Text = speed == MouseSettings.OneToOneSpeed
            ? $"{speed}/20 (1:1)"
            : $"{speed}/20 ({MouseSettings.SpeedMultiplier(speed):0.###}x)";
    }

    private void AddButton_Click(object sender, RoutedEventArgs e)
    {
        var picked = ProgramBox.SelectedItem as ProgramChoice;
        string name = SpeedProfiles.NormalizeProcessName(picked?.ProcessName ?? ProgramBox.Text ?? "");
        if (name.Length == 0)
        {
            System.Windows.MessageBox.Show(this, L.T("Escolha ou digite o programa.", "Pick or type the program."), L.T("Novo perfil", "New profile"),
                System.Windows.MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        string display = picked?.ProcessName.Equals(name, StringComparison.OrdinalIgnoreCase) == true
            ? picked.DisplayName
            : SpeedProfiles.KnownPrograms.GetValueOrDefault(name, name);
        Result = new SpeedProfile(name, display, (int)SpeedSlider.Value);
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
