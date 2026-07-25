using Microsoft.AspNetCore.Authentication.Cookies;
using QuickShare.Shared.Infrastructure;

namespace QuickShare.Server.Extensions;

public static class ApiCookieAuthExtensions
{
    public static IServiceCollection AddApiCookieAuth(this IServiceCollection services)
    {
        services.AddSingleton<ITicketStore>(sp =>
        {
            return new SessionTicketStore(
                sp.GetRequiredService<IServiceScopeFactory>(),
                TimeSpan.FromDays(30));
        });

        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.Cookie.Name = AuthConstants.CookieName;
                options.ExpireTimeSpan = TimeSpan.FromDays(30);
                options.SlidingExpiration = true;
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

        services.AddOptions<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme)
            .Configure<ITicketStore>((options, store) =>
            {
                options.SessionStore = store;
            });

        services.AddAuthorization();
        return services;
    }
}
