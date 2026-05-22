namespace MadAuthor.Contracts.Books;

public record BookCharacterDto(
    Guid Id,
    string Name,
    string? Description,
    string? Personality,
    string? Background,
    string? Goals,
    string? Conflicts,
    DateTime CreatedDate);

public record CreateBookCharacterRequest(
    string Name,
    string? Description,
    string? Personality,
    string? Background,
    string? Goals,
    string? Conflicts);

public record UpdateBookCharacterRequest(
    string? Name,
    string? Description,
    string? Personality,
    string? Background,
    string? Goals,
    string? Conflicts);
