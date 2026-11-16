using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using QuickShare.Infrastructure.Dev;
using Xunit;

namespace QuickShare.Server.Tests.E2E;

/// <summary>
/// The dev sign-in is an authentication shortcut. Its only real protection is the gate, so the
/// gate is what these tests pin: a production host must never map it, and neither must a
/// development host that is pointed at a real database.
/// </summary>
public sealed class DevSignInGateTests
{
    private static IConfiguration Config(bool inMemory) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["UseInMemoryDatabase"] = inMemory ? "true" : "false",
            })
            .Build();

    private static IHostEnvironment Env(string name) =>
        new StubEnvironment { EnvironmentName = name };

    [Fact]
    public void Development_WithInMemory_Enables()
    {
        Assert.True(DevSignIn.ShouldEnable(Config(true), Env(Environments.Development)));
    }

    [Fact]
    public void Production_IsNeverEnabled_EvenWithInMemory()
    {
        Assert.False(DevSignIn.ShouldEnable(Config(true), Env(Environments.Production)));
    }

    [Fact]
    public void Staging_IsNeverEnabled()
    {
        Assert.False(DevSignIn.ShouldEnable(Config(true), Env(Environments.Staging)));
    }

    [Fact]
    public void Development_WithARealDatabase_IsNeverEnabled()
    {
        Assert.False(DevSignIn.ShouldEnable(Config(false), Env(Environments.Development)));
    }

    [Fact]
    public void Token_IsRandomPerCall()
    {
        Assert.NotEqual(DevSignIn.NewToken(), DevSignIn.NewToken());
    }

    private sealed class StubEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "QuickShare";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
