using Microsoft.AspNetCore.Authentication.Cookies;

namespace QuickShare.Server.Extensions;

public static class ApiCookieAuthExtensions
{
    public static IServiceCollection AddApiCookieAuth(this IServiceCollection services)
    {
        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.Cookie.Name = AuthConstants.CookieName;
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
