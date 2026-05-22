using System.Net.Sockets;
using System.Text;
using MadAuthor.Application.Security;
using MadAuthor.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace MadAuthor.Infrastructure.Security;

public class ClamAvOptions
{
    public string? Host { get; set; }
    public int Port { get; set; } = 3310;
    public int ConnectTimeoutMs { get; set; } = 5_000;
    public int ScanTimeoutMs { get; set; } = 60_000;
    /// <summary>Max bytes accepted per scan. Must be ≤ clamd's <c>StreamMaxLength</c>.</summary>
    public int MaxBytes { get; set; } = 25 * 1024 * 1024; // 25 MB matches our upload cap minus headroom
}

/// <summary>
/// ClamAV TCP scanner using the INSTREAM protocol. Connects to <c>clamd</c>, streams the file in
/// chunks framed by 4-byte big-endian length prefixes, then terminates the stream with a zero
/// length. clamd responds with one of:
/// <list type="bullet">
///   <item><c>stream: OK</c> — clean</item>
///   <item><c>stream: &lt;ThreatName&gt; FOUND</c> — infected</item>
///   <item><c>... ERROR</c> — scanner error; treated as Skipped + logged so uploads still succeed</item>
/// </list>
/// All errors fall back to <see cref="ScanStatus.Skipped"/> — virus scanning is a defense-in-depth
/// layer, not a blocker for legit users, and we don't want a clamd outage to take down uploads.
/// </summary>
public sealed class ClamAvFileScanner(ClamAvOptions options, ILogger<ClamAvFileScanner> log) : IFileScanner
{
    private const int ChunkSize = 16 * 1024; // 16 KiB chunks; sized to fit comfortably under clamd's StreamMaxLength

    public bool IsEnabled => !string.IsNullOrWhiteSpace(options.Host);

    public async Task<FileScanResult> ScanAsync(Stream content, string? fileName = null, CancellationToken ct = default)
    {
        if (!IsEnabled)
            return new FileScanResult(ScanStatus.Skipped);

        try
        {
            using var tcp = new TcpClient();
            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            connectCts.CancelAfter(options.ConnectTimeoutMs);
            await tcp.ConnectAsync(options.Host!, options.Port, connectCts.Token);

            using var net = tcp.GetStream();
            net.ReadTimeout = options.ScanTimeoutMs;
            net.WriteTimeout = options.ScanTimeoutMs;

            // 1. Send "zINSTREAM\0" — the 'z' prefix asks clamd to terminate replies with NUL.
            var hello = Encoding.ASCII.GetBytes("zINSTREAM\0");
            await net.WriteAsync(hello, ct);

            // 2. Stream the file in chunks: [4-byte BE length][bytes].
            var buffer = new byte[ChunkSize];
            var totalSent = 0;
            while (true)
            {
                var n = await content.ReadAsync(buffer.AsMemory(0, ChunkSize), ct);
                if (n == 0) break;

                totalSent += n;
                if (totalSent > options.MaxBytes)
                {
                    log.LogWarning("ClamAV scan aborted: file '{File}' exceeds MaxBytes ({Max} B).", fileName, options.MaxBytes);
                    // Close the connection abruptly; result will be Skipped.
                    return new FileScanResult(ScanStatus.Skipped);
                }

                var lenBe = new byte[4];
                lenBe[0] = (byte)((n >> 24) & 0xFF);
                lenBe[1] = (byte)((n >> 16) & 0xFF);
                lenBe[2] = (byte)((n >> 8) & 0xFF);
                lenBe[3] = (byte)(n & 0xFF);
                await net.WriteAsync(lenBe, ct);
                await net.WriteAsync(buffer.AsMemory(0, n), ct);
            }

            // 3. Terminate with a 0-length chunk and read the verdict.
            await net.WriteAsync(new byte[] { 0, 0, 0, 0 }, ct);

            var reply = new MemoryStream();
            var readBuf = new byte[256];
            while (true)
            {
                var got = await net.ReadAsync(readBuf, ct);
                if (got <= 0) break;
                reply.Write(readBuf, 0, got);
                if (readBuf[..got].Contains((byte)0)) break; // NUL terminator
            }
            var verdict = Encoding.ASCII.GetString(reply.ToArray()).TrimEnd('\0', '\n', '\r', ' ');

            // Examples:
            //   "stream: OK"
            //   "stream: Win.Test.EICAR_HDB-1 FOUND"
            //   "stream: <something> ERROR"
            if (verdict.EndsWith(": OK", StringComparison.Ordinal))
                return new FileScanResult(ScanStatus.Clean);

            if (verdict.EndsWith(" FOUND", StringComparison.Ordinal))
            {
                // Threat name lives between "stream: " and " FOUND".
                const string prefix = "stream: ";
                var threat = verdict.StartsWith(prefix, StringComparison.Ordinal)
                    ? verdict[prefix.Length..^" FOUND".Length]
                    : verdict;
                log.LogWarning("ClamAV detected malware in '{File}': {Threat}", fileName, threat);
                return new FileScanResult(ScanStatus.Infected, threat);
            }

            log.LogWarning("ClamAV returned unrecognised verdict for '{File}': {Verdict}", fileName, verdict);
            return new FileScanResult(ScanStatus.Skipped);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "ClamAV scan failed for '{File}'; treating as Skipped.", fileName);
            return new FileScanResult(ScanStatus.Skipped);
        }
    }
}
