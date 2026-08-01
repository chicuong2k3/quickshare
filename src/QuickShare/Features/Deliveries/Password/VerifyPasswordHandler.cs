using LightResults;

namespace QuickShare.Features.Deliveries.Password;

public record VerifyPasswordCommand(string ShareCode, string Password);

public record VerifyPasswordResponse(bool Valid);

public sealed class VerifyPasswordHandler(IHttpContextAccessor httpContextAccessor)
{
    private const string SessionKeyPrefix = "pw_";
    private const string SessionAttemptsSuffix = "_attempts";

    public async Task<Result<VerifyPasswordResponse>> Handle(VerifyPasswordCommand cmd, AppDbContext db, CancellationToken ct)
    {
        var delivery = await db.Deliveries
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.ShareCode == cmd.ShareCode, ct);

        if (delivery is null || delivery.PasswordHash is null)
            return Result.Success(new VerifyPasswordResponse(true)); // No password set = always valid

        var httpContext = httpContextAccessor.HttpContext!;
        var sessionKey = $"{SessionKeyPrefix}{delivery.Id}";
        var attempts = httpContext.Session.GetInt32($"{sessionKey}{SessionAttemptsSuffix}") ?? 0;
        if (attempts >= 5)
            return Result.Failure<VerifyPasswordResponse>(new LightResults.Error("Too many attempts. Try again later."));

        httpContext.Session.SetInt32($"{sessionKey}{SessionAttemptsSuffix}", attempts + 1);
        return BCrypt.Net.BCrypt.Verify(cmd.Password, delivery.PasswordHash)
            ? Result.Success(new VerifyPasswordResponse(true))
            : Result.Failure<VerifyPasswordResponse>(new LightResults.Error("Wrong password."));
    }
}
