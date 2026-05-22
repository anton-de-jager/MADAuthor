using MadAuthor.Application.Security;
using MadAuthor.Domain.Enums;

namespace MadAuthor.Infrastructure.Security;

/// <summary>
/// Fallback scanner used when no antivirus daemon is configured. Returns
/// <see cref="ScanStatus.Skipped"/> for every input without touching the stream.
/// </summary>
public sealed class NoOpFileScanner : IFileScanner
{
    public bool IsEnabled => false;

    public Task<FileScanResult> ScanAsync(Stream content, string? fileName = null, CancellationToken ct = default)
        => Task.FromResult(new FileScanResult(ScanStatus.Skipped));
}
