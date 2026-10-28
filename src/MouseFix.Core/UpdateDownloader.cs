using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace MouseFix.Core;

/// <summary>Downloads a release file and only keeps it if its SHA-256 matches the release's checksum file.</summary>
public static class UpdateDownloader
{
    /// <summary>
    /// Downloads <paramref name="fileUrl"/> to <paramref name="destination"/> and verifies it against the hash in
    /// <paramref name="checksumUrl"/> ("HASH  filename"). On a mismatch the file is deleted and an exception is thrown.
    /// </summary>
    public static async Task DownloadVerifiedAsync(string fileUrl, string checksumUrl, string destination,
        IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("MouseFix", "update"));

        string expected = ParseSha256(await http.GetStringAsync(checksumUrl, cancellationToken))
            ?? throw new InvalidDataException(L.T("O arquivo de verificação (SHA-256) da versão nova está vazio ou inválido.", "The new version's check file (SHA-256) is empty or invalid."));

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        using (var response = await http.GetAsync(fileUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
        {
            response.EnsureSuccessStatusCode();
            long? total = response.Content.Headers.ContentLength;
            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var target = File.Create(destination);
            var buffer = new byte[81920];
            long done = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                done += read;
                if (total > 0)
                    progress?.Report((double)done / total.Value);
            }
        }

        if (!HashMatches(destination, expected))
        {
            File.Delete(destination);
            throw new InvalidDataException(L.T("O arquivo baixado não confere com o SHA-256 da versão nova; ele foi apagado e nada foi instalado.", "The downloaded file doesn't match the new version's SHA-256; it was deleted and nothing was installed."));
        }
    }

    /// <summary>The 64-hex-digit hash at the start of a checksum file, or null.</summary>
    public static string? ParseSha256(string text)
    {
        var first = text.Trim().Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return first is { Length: 64 } && first.All(Uri.IsHexDigit) ? first.ToUpperInvariant() : null;
    }

    public static bool HashMatches(string file, string expectedSha256)
    {
        using var stream = File.OpenRead(file);
        return Convert.ToHexString(SHA256.HashData(stream)).Equals(expectedSha256, StringComparison.OrdinalIgnoreCase);
    }
}
