using MouseFix.Core;

namespace MouseFix.Core.Tests;

public class IssueReportTests
{
    private static IssueReportInfo Info(string log) => new(
        "1.2.0", "Windows 10.0.26200", 125, "aceleração desligada, velocidade 10/20",
        ["Razer Synapse"], "MSI App Player · Free Fire", log);

    [Fact]
    public void Link_opens_a_prefilled_issue_with_the_system_details()
    {
        var url = IssueReport.BuildUrl(Info("2026-10-21 [INFO] tudo certo"));
        var body = Uri.UnescapeDataString(url[(url.IndexOf("body=") + 5)..]);

        Assert.StartsWith(IssueReport.NewIssueUrl + "?title=", url);
        Assert.Contains("MouseFix: 1.2.0", body);
        Assert.Contains("Escala da tela: 125%", body);
        Assert.Contains("Razer Synapse", body);
        Assert.Contains("tudo certo", body);
    }

    [Fact]
    public void Very_long_logs_are_trimmed_keeping_the_newest_lines()
    {
        var log = string.Join('\n', Enumerable.Range(1, 2000).Select(i => $"linha {i} com algum texto de log"));
        var url = IssueReport.BuildUrl(Info(log));
        var body = Uri.UnescapeDataString(url[(url.IndexOf("body=") + 5)..]);

        Assert.True(url.Length <= 7000);
        Assert.Contains("linha 2000 ", body);
        Assert.DoesNotContain("linha 1 ", body);
    }

    [Fact]
    public void The_windows_user_folder_is_hidden()
    {
        var text = IssueReport.Anonymize(@"Sensibilidade ajustada em C:\Users\carlo\AppData\x.cfg", @"C:\Users\carlo");

        Assert.Equal(@"Sensibilidade ajustada em %USERPROFILE%\AppData\x.cfg", text);
    }
}
