using QuickShare.Data.Entities;
using Riok.Mapperly.Abstractions;

namespace QuickShare.Features.Users.Recipients;

[Mapper]
public static partial class RecipientMapper
{
    [MapperIgnoreSource(nameof(RecipientContact.UserId))]
    public static partial RecipientResponse ToResponse(this RecipientContact source);
}

public record RecipientResponse(Guid Id, string Email, string? Name, DateTime LastUsedAt, int UseCount);