using Microsoft.AspNetCore.Authentication.Cookies;

namespace TeleCloud.Server.Infrastructure;

public static class ApiCookieAuthExtensions
{
    public static IServiceCollection AddApiCookieAuth(this IServiceCollection services)
    {
        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.Cookie.Name = ".AspNetCore.TeleCloud";
                options.Events.OnRedirectToLogin = ctx =>
                {
                    ctx.Response.StatusCode = 401;
                    return Task.CompletedTask;
                };
                options.Events.OnRedirectToAccessDenied = ctx =>
                {
                    ctx.Response.StatusCode = 403;
                    return Task.CompletedTask;
                };
            });

        services.AddAuthorization();
        return services;
    }
}
