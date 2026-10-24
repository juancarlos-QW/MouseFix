using MouseFix.Core;

namespace MouseFix.Core.Tests;

public sealed class BlueStacksProfilesTests : IDisposable
{
    // Trimmed copy of a real MSI App Player mapping file (same layout and spacing).
    private const string SampleCfg = """
        {
            "MetaData" : {
                "ParserVersion" : "17"
            },
            "ControlSchemes" : [
                {
                    "Name" : "Smart",
                    "BuiltIn" : true,
                    "Selected" : false,
                    "GameControls" : [
                        {
                            "$type" : "Pan, Bluestacks",
                            "Type" : "Pan",
                            "Sensitivity" : 1.0,
                            "SensitivityRatioY" : 0.4,
                            "MouseAcceleration" : true
                        },
                        {
                            "$type" : "Tap, Bluestacks",
                            "Type" : "Tap"
                        }
                    ]
                },
                {
                    "Name" : "Standard",
                    "BuiltIn" : false,
                    "Selected" : true,
                    "GameControls" : [
                        {
                            "$type" : "Pan, Bluestacks",
                            "Type" : "Pan",
                            "Sensitivity" : 1.71,
                            "SensitivityRatioY" : 0.4736842105,
                            "MouseAcceleration" : false
                        }
                    ]
                }
            ],
            "Strings" : {
                "MouseAcceleration" : "Mouse acceleration"
            }
        }
        """;

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"mousefix-bs-{Guid.NewGuid()}");
    private readonly string _file;

    public BlueStacksProfilesTests()
    {
        var dir = Path.Combine(_root, "BlueStacks_msi5", "Engine", "UserData", "InputMapper", "UserFiles");
        Directory.CreateDirectory(dir);
        _file = Path.Combine(dir, "com.dts.freefireth.cfg");
        File.WriteAllText(_file, SampleCfg);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Finds_free_fire_profile_and_reads_aim_controls()
    {
        var profile = Assert.Single(BlueStacksProfiles.Find(_root));

        Assert.Equal("MSI App Player", profile.EmulatorName);
        Assert.Equal("Free Fire", profile.GameName);
        Assert.Equal(2, profile.Aims.Count);
        Assert.True(profile.AnyAcceleration);
        Assert.Equal(new EmulatorAim("Standard", true, 1.71, 0.4736842105, false), profile.SelectedAim);
    }

    [Fact]
    public void Disabling_acceleration_changes_only_the_boolean_values_and_keeps_a_backup()
    {
        int changed = BlueStacksProfiles.DisableMouseAcceleration(_file);

        Assert.Equal(1, changed);
        Assert.Equal(SampleCfg.Replace("\"MouseAcceleration\" : true", "\"MouseAcceleration\" : false"), File.ReadAllText(_file));
        Assert.Equal(SampleCfg, File.ReadAllText(_file + BlueStacksProfiles.BackupSuffix));
        Assert.False(BlueStacksProfiles.Load("x", "y", _file).AnyAcceleration);
    }

    [Fact]
    public void Built_in_mapping_is_used_when_the_user_never_customised_controls()
    {
        File.Delete(_file);
        var builtIn = Path.Combine(_root, "BlueStacks_msi5", "Engine", "UserData", "InputMapper", "com.dts.freefireth.cfg");
        File.WriteAllText(builtIn, SampleCfg);

        var profile = Assert.Single(BlueStacksProfiles.Find(_root));

        Assert.Equal(builtIn, profile.FilePath);
        Assert.True(profile.AnyAcceleration);
    }

    [Fact]
    public void Customised_mapping_wins_over_the_built_in_one()
    {
        var builtIn = Path.Combine(_root, "BlueStacks_msi5", "Engine", "UserData", "InputMapper", "com.dts.freefireth.cfg");
        File.WriteAllText(builtIn, SampleCfg);

        Assert.Equal(_file, Assert.Single(BlueStacksProfiles.Find(_root)).FilePath);
    }

    [Fact]
    public void Setting_sensitivity_changes_only_the_selected_scheme()
    {
        int changed = BlueStacksProfiles.SetSelectedSensitivity(_file, 0.855);

        Assert.Equal(1, changed);
        var profile = BlueStacksProfiles.Load("x", "y", _file);
        Assert.Equal(0.86, profile.SelectedAim!.Sensitivity);
        Assert.Equal(1.0, profile.Aims.Single(a => a.SchemeName == "Smart").Sensitivity);
        Assert.Equal(0.4736842105, profile.SelectedAim.SensitivityRatioY);
        Assert.Equal(SampleCfg, File.ReadAllText(_file + BlueStacksProfiles.BackupSuffix));
        // "$type" must stay the first property: the emulator's JSON reader uses it to pick the control class.
        Assert.Matches(@"\{\s*""\$type"": ""Pan, Bluestacks""", File.ReadAllText(_file));
    }

    [Fact]
    public void Nothing_is_written_when_acceleration_is_already_off()
    {
        BlueStacksProfiles.DisableMouseAcceleration(_file);
        File.Delete(_file + BlueStacksProfiles.BackupSuffix);

        Assert.Equal(0, BlueStacksProfiles.DisableMouseAcceleration(_file));
        Assert.False(File.Exists(_file + BlueStacksProfiles.BackupSuffix));
    }
}
