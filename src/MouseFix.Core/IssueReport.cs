using System.Text;

namespace MouseFix.Core;

public sealed record IssueReportInfo(
    string AppVersion,
    string WindowsVersion,
    int DisplayScalePercent,
    string MouseState,
    IReadOnlyList<string> MouseSoftware,
    string? Emulator,
    string LogTail);

/// <summary>Builds a pre-filled "new issue" link. Nothing is sent: the user reviews the form on GitHub first.</summary>
public static class IssueReport
{
    public const string NewIssueUrl = "https://github.com/juancarlos-QW/MouseFix/issues/new";

    // Browsers and GitHub reject very long URLs; the log is trimmed to stay under this.
    private const int MaxUrlLength = 7000;

    public static string BuildUrl(IssueReportInfo info)
    {
        string url = Compose(info, info.LogTail);
        // Drop the oldest log lines until the link is short enough.
        var lines = info.LogTail.Split('\n').ToList();
        while (url.Length > MaxUrlLength && lines.Count > 0)
        {
            lines.RemoveAt(0);
            url = Compose(info, string.Join('\n', lines));
        }
        return url;
    }

    private static string Compose(IssueReportInfo info, string log)
    {
        var body = new StringBuilder()
            .AppendLine(L.T("**O que aconteceu?**", "**What happened?**"))
            .AppendLine()
            .AppendLine()
            .AppendLine(L.T("**O que você esperava?**", "**What did you expect?**"))
            .AppendLine()
            .AppendLine()
            .AppendLine("---")
            .AppendLine($"- MouseFix: {info.AppVersion}")
            .AppendLine($"- Windows: {info.WindowsVersion}")
            .AppendLine(L.T($"- Escala da tela: {info.DisplayScalePercent}%", $"- Display scale: {info.DisplayScalePercent}%"))
            .AppendLine(L.T($"- Mouse no Windows: {info.MouseState}", $"- Mouse in Windows: {info.MouseState}"))
            .AppendLine(L.T($"- Software de mouse aberto: {(info.MouseSoftware.Count == 0 ? "nenhum" : string.Join(", ", info.MouseSoftware))}", $"- Mouse software open: {(info.MouseSoftware.Count == 0 ? "none" : string.Join(", ", info.MouseSoftware))}"))
            .AppendLine(L.T($"- Emulador: {info.Emulator ?? "nenhum encontrado"}", $"- Emulator: {info.Emulator ?? "none found"}"));

        if (!string.IsNullOrWhiteSpace(log))
            body.AppendLine().AppendLine("<details><summary>Log</summary>").AppendLine().AppendLine("```").AppendLine(log).AppendLine("```").AppendLine("</details>");

        return $"{NewIssueUrl}?title={Uri.EscapeDataString(L.T("Problema: ", "Problem: "))}&body={Uri.EscapeDataString(body.ToString())}";
    }

    /// <summary>Last lines of the log with the user's profile path replaced, so the Windows user name isn't shared.</summary>
    public static string ReadLogTail(string path, int lines, string userProfile)
    {
        if (!File.Exists(path))
            return "";
        try
        {
            var tail = File.ReadLines(path).TakeLast(lines);
            return Anonymize(string.Join('\n', tail), userProfile);
        }
        catch (IOException)
        {
            return "";
        }
    }

    public static string Anonymize(string text, string userProfile) =>
        string.IsNullOrEmpty(userProfile) ? text : text.Replace(userProfile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
}
