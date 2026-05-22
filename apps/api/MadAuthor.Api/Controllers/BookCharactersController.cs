using MadAuthor.Application.Auth;
using MadAuthor.Contracts.Books;
using MadAuthor.Domain.Entities;
using MadAuthor.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MadAuthor.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/books/{bookId:guid}/characters")]
public class BookCharactersController(
    MadAuthorDbContext db,
    ICurrentUserService currentUser) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<BookCharacterDto>>> List(Guid bookId, CancellationToken ct)
    {
        var (userId, companyId) = Identify();
        var owns = await db.BookProjects.AnyAsync(
            p => p.Id == bookId && p.CompanyId == companyId && p.OwnerUserId == userId, ct);
        if (!owns) return NotFound();

        var characters = await db.BookCharacters
            .Where(c => c.BookProjectId == bookId)
            .OrderBy(c => c.Name)
            .Select(c => new BookCharacterDto(
                c.Id, c.Name, c.Description, c.Personality,
                c.Background, c.Goals, c.Conflicts, c.CreatedDate))
            .ToListAsync(ct);
        return characters;
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<BookCharacterDto>> Get(Guid bookId, Guid id, CancellationToken ct)
    {
        var (userId, companyId) = Identify();
        var character = await db.BookCharacters
            .Where(c => c.Id == id && c.BookProjectId == bookId)
            .Join(db.BookProjects, c => c.BookProjectId, p => p.Id, (c, p) => new { c, p })
            .Where(x => x.p.CompanyId == companyId && x.p.OwnerUserId == userId)
            .Select(x => new BookCharacterDto(
                x.c.Id, x.c.Name, x.c.Description, x.c.Personality,
                x.c.Background, x.c.Goals, x.c.Conflicts, x.c.CreatedDate))
            .FirstOrDefaultAsync(ct);
        if (character is null) return NotFound();
        return character;
    }

    [HttpPost]
    public async Task<ActionResult<BookCharacterDto>> Create(
        Guid bookId,
        [FromBody] CreateBookCharacterRequest req,
        CancellationToken ct)
    {
        var (userId, companyId) = Identify();
        var owns = await db.BookProjects.AnyAsync(
            p => p.Id == bookId && p.CompanyId == companyId && p.OwnerUserId == userId, ct);
        if (!owns) return NotFound();

        if (string.IsNullOrWhiteSpace(req.Name))
            return BadRequest(new { error = "Name is required." });
        if (req.Name.Length > 200)
            return BadRequest(new { error = "Name must be 200 characters or fewer." });

        var character = new BookCharacter
        {
            Id = Guid.NewGuid(),
            BookProjectId = bookId,
            Name = req.Name.Trim(),
            Description = req.Description?.Trim(),
            Personality = req.Personality?.Trim(),
            Background = req.Background?.Trim(),
            Goals = req.Goals?.Trim(),
            Conflicts = req.Conflicts?.Trim(),
            CreatedDate = DateTime.UtcNow,
        };
        db.BookCharacters.Add(character);
        await db.SaveChangesAsync(ct);

        var dto = new BookCharacterDto(
            character.Id, character.Name, character.Description, character.Personality,
            character.Background, character.Goals, character.Conflicts, character.CreatedDate);
        return CreatedAtAction(nameof(Get), new { bookId, id = character.Id }, dto);
    }

    [HttpPatch("{id:guid}")]
    public async Task<ActionResult<BookCharacterDto>> Update(
        Guid bookId, Guid id,
        [FromBody] UpdateBookCharacterRequest req,
        CancellationToken ct)
    {
        var (userId, companyId) = Identify();
        var character = await db.BookCharacters
            .Where(c => c.Id == id && c.BookProjectId == bookId)
            .Join(db.BookProjects, c => c.BookProjectId, p => p.Id, (c, p) => new { c, p })
            .Where(x => x.p.CompanyId == companyId && x.p.OwnerUserId == userId)
            .Select(x => x.c)
            .FirstOrDefaultAsync(ct);
        if (character is null) return NotFound();

        if (req.Name is not null)
        {
            if (string.IsNullOrWhiteSpace(req.Name) || req.Name.Length > 200)
                return BadRequest(new { error = "Name must be 1-200 characters." });
            character.Name = req.Name.Trim();
        }
        if (req.Description is not null)
            character.Description = string.IsNullOrEmpty(req.Description) ? null : req.Description.Trim();
        if (req.Personality is not null)
            character.Personality = string.IsNullOrEmpty(req.Personality) ? null : req.Personality.Trim();
        if (req.Background is not null)
            character.Background = string.IsNullOrEmpty(req.Background) ? null : req.Background.Trim();
        if (req.Goals is not null)
            character.Goals = string.IsNullOrEmpty(req.Goals) ? null : req.Goals.Trim();
        if (req.Conflicts is not null)
            character.Conflicts = string.IsNullOrEmpty(req.Conflicts) ? null : req.Conflicts.Trim();

        character.UpdatedDate = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        return new BookCharacterDto(
            character.Id, character.Name, character.Description, character.Personality,
            character.Background, character.Goals, character.Conflicts, character.CreatedDate);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid bookId, Guid id, CancellationToken ct)
    {
        var (userId, companyId) = Identify();
        var character = await db.BookCharacters
            .Where(c => c.Id == id && c.BookProjectId == bookId)
            .Join(db.BookProjects, c => c.BookProjectId, p => p.Id, (c, p) => new { c, p })
            .Where(x => x.p.CompanyId == companyId && x.p.OwnerUserId == userId)
            .Select(x => x.c)
            .FirstOrDefaultAsync(ct);
        if (character is null) return NotFound();

        db.BookCharacters.Remove(character);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private (Guid userId, Guid companyId) Identify()
    {
        if (currentUser.UserId is not { } uid)
            throw new UnauthorizedAccessException("No user id on the request principal.");
        if (currentUser.CompanyId is { } cid) return (uid, cid);
        var fallback = db.CompanyMembers
            .Where(m => m.UserId == uid)
            .OrderBy(m => m.CreatedDate)
            .Select(m => (Guid?)m.CompanyId)
            .FirstOrDefault();
        if (fallback is null)
            throw new UnauthorizedAccessException($"User {uid} has no company membership.");
        return (uid, fallback.Value);
    }
}
