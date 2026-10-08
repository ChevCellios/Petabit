using System.Net;
using System.Reflection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Petabit.Tests;

public sealed class BuildVersionTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0503a110")]
    [InlineData("not-a-commit-or-a-secret-to-expose")]
    [InlineData("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz")]
    public void InvalidBuildMetadataIsNotExposed(string? value) =>
        Assert.Null(Petabit.Services.BuildVersion.NormalizeCommitSha(value));

    [Fact]
    public void AssemblyMetadataIsReadAndNormalized()
    {
        var assembly = System.Reflection.Emit.AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName("VersionTest"), System.Reflection.Emit.AssemblyBuilderAccess.Run);
        assembly.SetCustomAttribute(new System.Reflection.Emit.CustomAttributeBuilder(
            typeof(AssemblyMetadataAttribute).GetConstructor([typeof(string), typeof(string)])!,
            ["CommitSha", "0503A11029EDFBAC3E0E6394563B4CA84764F250"]));
        Assert.Equal("0503a11029edfbac3e0e6394563b4ca84764f250",
            Petabit.Services.BuildVersion.FromAssembly(assembly).CommitSha);
    }

    [Theory]
    [InlineData("0503a11029edfbac3e0e6394563b4ca84764f250", HttpStatusCode.OK)]
    [InlineData(null, HttpStatusCode.ServiceUnavailable)]
    public async Task VersionReturnsOnlyBuildShaWithoutCaching(string? sha, HttpStatusCode status)
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.Configure<Petabit.Services.StationSyncOptions>(options => options.Enabled = false);
                services.RemoveAll<Petabit.Services.BuildVersion>();
                services.AddSingleton(new Petabit.Services.BuildVersion(sha));
            }));
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        using var response = await client.GetAsync("/version");
        Assert.Equal(status, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var json = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Single(json.RootElement.EnumerateObject());
        Assert.Equal(sha, json.RootElement.GetProperty("commitSha").GetString());
    }
}
