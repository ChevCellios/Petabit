using System.Reflection;

namespace Petabit.Services;

public sealed record BuildVersion(string? CommitSha)
{
    public static BuildVersion FromAssembly(Assembly assembly)
    {
        var sha = assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == "CommitSha")?.Value;
        return new BuildVersion(NormalizeCommitSha(sha));
    }

    public static string? NormalizeCommitSha(string? value) =>
        value is { Length: 40 } && value.All(Uri.IsHexDigit) ? value.ToLowerInvariant() : null;
}
