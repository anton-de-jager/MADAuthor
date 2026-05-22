using MadAuthor.Contracts.ClaudeTasks;
using MadAuthor.Domain.Entities;
using MadAuthor.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MadAuthor.Api.Controllers;

/// <summary>
/// Reusable prompt templates the operator picks when creating a new <see cref="ClaudeTask"/>.
/// </summary>
[ApiController]
[Authorize(Roles = "Admin,Owner")]
[Route("api/claude-prompt-templates")]
public class ClaudePromptTemplatesController(MadAuthorDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ClaudePromptTemplateDto>>> List(CancellationToken ct = default)
    {
        var rows = await db.ClaudePromptTemplates
            .OrderByDescending(t => t.UpdatedDate ?? t.CreatedDate)
            .ToListAsync(ct);
        return rows.Select(ToDto).ToList();
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ClaudePromptTemplateDto>> Get(int id, CancellationToken ct = default)
    {
        var t = await db.ClaudePromptTemplates.FindAsync(new object[] { id }, ct);
        return t is null ? NotFound() : ToDto(t);
    }

    [HttpPost]
    public async Task<ActionResult<ClaudePromptTemplateDto>> Create(
        [FromBody] CreateClaudePromptTemplateRequest req,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req.Name))
            return BadRequest(new { error = "Name is required." });
        if (req.Name.Length > 200)
            return BadRequest(new { error = "Name must be 200 characters or fewer." });
        if (string.IsNullOrWhiteSpace(req.Content))
            return BadRequest(new { error = "Content is required." });

        // Name is unique (DB-enforced) -- pre-check for a friendlier error.
        var name = req.Name.Trim();
        if (await db.ClaudePromptTemplates.AnyAsync(t => t.Name == name, ct))
            return Conflict(new { error = $"A template named '{name}' already exists." });

        var template = new ClaudePromptTemplate
        {
            Name = name,
            Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description,
            Content = req.Content,
            CreatedDate = DateTime.UtcNow,
        };
        db.ClaudePromptTemplates.Add(template);
        await db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(Get), new { id = template.Id }, ToDto(template));
    }

    [HttpPatch("{id:int}")]
    public async Task<ActionResult<ClaudePromptTemplateDto>> Update(
        int id,
        [FromBody] UpdateClaudePromptTemplateRequest req,
        CancellationToken ct = default)
    {
        var t = await db.ClaudePromptTemplates.FindAsync(new object[] { id }, ct);
        if (t is null) return NotFound();

        if (req.Name is not null)
        {
            var name = req.Name.Trim();
            if (name.Length is 0 or > 200)
                return BadRequest(new { error = "Name must be 1-200 characters." });
            if (name != t.Name && await db.ClaudePromptTemplates.AnyAsync(x => x.Name == name && x.Id != id, ct))
                return Conflict(new { error = $"A template named '{name}' already exists." });
            t.Name = name;
        }
        if (req.Description is not null)
            t.Description = string.IsNullOrEmpty(req.Description) ? null : req.Description;
        if (req.Content is not null)
        {
            if (string.IsNullOrWhiteSpace(req.Content))
                return BadRequest(new { error = "Content cannot be blank." });
            t.Content = req.Content;
        }

        await db.SaveChangesAsync(ct);
        return ToDto(t);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct = default)
    {
        var t = await db.ClaudePromptTemplates.FindAsync(new object[] { id }, ct);
        if (t is null) return NotFound();
        db.ClaudePromptTemplates.Remove(t);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private static ClaudePromptTemplateDto ToDto(ClaudePromptTemplate t) =>
        new(t.Id, t.Name, t.Description, t.Content, t.CreatedDate, t.UpdatedDate);
}
