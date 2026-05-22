using MadAuthor.Domain.Enums;

namespace MadAuthor.Application.Security;

/// <summary>
/// Antivirus / malware scanning abstraction for uploaded files. Phase 4 implementation:
/// pluggable backends — currently <see cref="ScanStatus"/> defaults to <c>Skipped</c> when no
/// daemon is configured (the no-op scanner) and is computed via the ClamAV INSTREAM protocol
/// when <c>CLAMAV_HOST</c> + <c>CLAMAV_PORT</c> env vars (or the matching
/// <c>FileScanner:ClamAv:Host/Port</c> config keys) are present.
/// </summary>
public interface IFileScanner
{
    /// <summary>True if the scanner has a real backend wired up. When false, every scan returns
    /// <see cref="ScanStatus.Skipped"/> without consuming the stream.</summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Scan a file stream end-to-end. The stream is read from current position to end; if you
    /// need to read it again afterwards, rewind before calling. Returns <see cref="ScanStatus.Clean"/>
    /// on a clean verdict, <see cref="ScanStatus.Infected"/> if malware was detected, or
    /// <see cref="ScanStatus.Skipped"/> if the scanner is disabled or the call errored
    /// (errors should be logged; we never fail the upload over scanner outages).
    /// </summary>
    /// <param name="content">The file bytes. The scanner does not close the stream.</param>
    /// <param name="fileName">Hint for logging only; not sent to the backend.</param>
    Task<FileScanResult> ScanAsync(Stream content, string? fileName = null, CancellationToken ct = default);
}

/// <summary>
/// Outcome of <see cref="IFileScanner.ScanAsync"/>. <see cref="Threat"/> carries the daemon's
/// own threat name when <see cref="Status"/> is <see cref="ScanStatus.Infected"/> (e.g.
/// <c>Win.Test.EICAR_HDB-1</c>).
/// </summary>
public sealed record FileScanResult(ScanStatus Status, string? Threat = null);
