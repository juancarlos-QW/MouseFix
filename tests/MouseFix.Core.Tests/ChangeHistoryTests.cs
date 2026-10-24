using MouseFix.Core;

namespace MouseFix.Core.Tests;

public sealed class ChangeHistoryTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"mousefix-history-{Guid.NewGuid()}.json");
    private static readonly DateTime T0 = new(2026, 10, 24, 14, 0, 0);

    public void Dispose() => File.Delete(_path);

    [Fact]
    public void Entries_are_kept_newest_first_and_survive_a_restart()
    {
        var history = new ChangeHistory(_path);
        history.Add(new ChangeEntry(T0, ChangeSource.User, "Aceleração removida", true));
        history.Add(new ChangeEntry(T0.AddMinutes(5), ChangeSource.OtherProgram, "aceleração ligada", true, Program: "HD-Player"));
        history.Flush();

        var reloaded = new ChangeHistory(_path);

        Assert.Equal(2, reloaded.Entries.Count);
        Assert.Equal(ChangeSource.OtherProgram, reloaded.Entries[0].Source);
        Assert.Equal("HD-Player", reloaded.Entries[0].Program);
    }

    [Fact]
    public void The_same_change_repeated_within_a_minute_is_grouped()
    {
        var history = new ChangeHistory(_path);
        for (int i = 0; i < 3; i++)
            history.Add(new ChangeEntry(T0.AddSeconds(i * 10), ChangeSource.OtherProgram, "aceleração ligada", true, Program: "Jogo"));

        var entry = Assert.Single(history.Entries);
        Assert.Equal(3, entry.Count);
        Assert.Equal(T0.AddSeconds(20), entry.Time);
    }

    [Fact]
    public void Only_the_most_recent_entries_are_kept()
    {
        var history = new ChangeHistory(_path);
        for (int i = 0; i < ChangeHistory.MaxEntries + 20; i++)
            history.Add(new ChangeEntry(T0.AddMinutes(i * 2), ChangeSource.User, $"mudança {i}", true));

        Assert.Equal(ChangeHistory.MaxEntries, history.Entries.Count);
        Assert.Equal($"mudança {ChangeHistory.MaxEntries + 19}", history.Entries[0].Description);
    }

    [Fact]
    public void Rapid_changes_are_written_at_most_every_couple_of_seconds_and_flushed_on_demand()
    {
        var history = new ChangeHistory(_path);
        history.Add(new ChangeEntry(T0, ChangeSource.User, "primeira", true));
        history.Add(new ChangeEntry(T0.AddMinutes(2), ChangeSource.User, "segunda", true));

        Assert.Single(new ChangeHistory(_path).Entries);

        history.Flush();
        Assert.Equal(2, new ChangeHistory(_path).Entries.Count);
    }

    [Fact]
    public void A_corrupt_file_starts_an_empty_history()
    {
        File.WriteAllText(_path, "{ not json");

        Assert.Empty(new ChangeHistory(_path).Entries);
    }
}
