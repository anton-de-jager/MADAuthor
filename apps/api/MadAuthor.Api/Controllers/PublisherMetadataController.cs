using System.Text;
using System.Text.Json;
using MadAuthor.Application.Auth;
using MadAuthor.Application.Storage;
using MadAuthor.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MadAuthor.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/books/{projectId:guid}/publisher-metadata")]
public class PublisherMetadataController(
    MadAuthorDbContext db,
    IFileStorage storage,
    ICurrentUserService currentUser) : ControllerBase
{
    private const string MetadataFileName = "publisher-metadata.json";

    [HttpGet]
    public async Task<IActionResult> Get(Guid projectId, CancellationToken ct)
    {
        var (uid, cid) = Identify();
        var owns = await db.BookProjects
            .AnyAsync(p => p.Id == projectId && p.OwnerUserId == uid && p.CompanyId == cid, ct);
        if (!owns) return NotFound();

        var asset = await db.BookAssets
            .Where(a => a.BookProjectId == projectId && a.FileName == MetadataFileName)
            .OrderByDescending(a => a.CreatedDate)
            .FirstOrDefaultAsync(ct);
        if (asset is null) return NotFound(new { error = "Publisher metadata not found." });

        string raw;
        try
        {
            using var stream = storage.OpenRead(asset.BlobContainer, asset.BlobKey);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            raw = await reader.ReadToEndAsync(ct);
        }
        catch (FileNotFoundException)
        {
            return NotFound(new { error = "Publisher metadata file is missing from storage." });
        }
        catch (DirectoryNotFoundException)
        {
            return NotFound(new { error = "Publisher metadata file is missing from storage." });
        }

        // Validate the JSON parses; otherwise surface a clear error.
        try
        {
            using var doc = JsonDocument.Parse(raw);
            return Content(raw, "application/json", Encoding.UTF8);
        }
        catch (JsonException ex)
        {
            return StatusCode(500, new { error = "Stored publisher metadata is not valid JSON.", detail = ex.Message });
        }
    }

    [HttpPut]
    public async Task<IActionResult> Put(Guid projectId, [FromBody] JsonElement body, CancellationToken ct)
    {
        var (uid, cid) = Identify();
        var project = await db.BookProjects
            .FirstOrDefaultAsync(p => p.Id == projectId && p.OwnerUserId == uid && p.CompanyId == cid, ct);
        if (project is null) return NotFound();

        if (body.ValueKind != JsonValueKind.Object)
            return BadRequest(new { error = "Body must be a JSON object." });

        var asset = await db.BookAssets
            .Where(a => a.BookProjectId == projectId && a.FileName == MetadataFileName)
            .OrderByDescending(a => a.CreatedDate)
            .FirstOrDefaultAsync(ct);
        if (asset is null) return NotFound(new { error = "Publisher metadata not found." });

        // Re-serialize with stable indentation so the file is human-friendly on disk.
        string serialized;
        try
        {
            serialized = JsonSerializer.Serialize(body, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (JsonException ex)
        {
            return BadRequest(new { error = "Could not serialize JSON.", detail = ex.Message });
        }

        // Overwrite the same blob. SaveAsync is implemented as a put-by-key for local storage.
        var bytes = Encoding.UTF8.GetBytes(serialized);
        using (var ms = new MemoryStream(bytes))
        {
            await storage.SaveAsync(asset.BlobContainer, asset.BlobKey, ms, ct);
        }
        asset.FileSize = bytes.LongLength;
        asset.UpdatedDate = DateTime.UtcNow;

        // Sync selected fields back to the BookProject columns so the rest of the app
        // (and exports) see them without reading the JSON blob.
        if (TryGetString(body, "kdpDescription", out var kdp))
            project.Description = kdp;
        if (TryGetString(body, "refinedSubtitle", out var refined) && !string.IsNullOrWhiteSpace(refined))
            project.Subtitle = refined;
        if (TryGetString(body, "copyrightText", out var cprt))
            project.CopyrightText = cprt;
        if (TryGetString(body, "isbn", out var isbn) && !string.IsNullOrWhiteSpace(isbn))
            project.Isbn = isbn;

        project.UpdatedDate = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        return Content(serialized, "application/json", Encoding.UTF8);
    }

    private static bool TryGetString(JsonElement obj, string propertyName, out string value)
    {
        value = string.Empty;
        if (!obj.TryGetProperty(propertyName, out var prop)) return false;
        if (prop.ValueKind == JsonValueKind.Null) { value = string.Empty; return true; }
        if (prop.ValueKind != JsonValueKind.String) return false;
        value = prop.GetString() ?? string.Empty;
        return true;
    }

    private (Guid userId, Guid companyId) Identify()
    {
        if (currentUser.UserId is not { } uid || currentUser.CompanyId is not { } cid)
            throw new UnauthorizedAccessException();
        return (uid, cid);
    }
}
