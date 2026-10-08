using System.Reflection;
using System.Text.RegularExpressions;

namespace MeshMuster.Tests;

/// <summary>
/// Greps the source; a secret must not reach a log line or the device list.
/// </summary>
public class SensitiveLoggingTests
{
    [Test]
    public void No_log_call_interpolates_a_secret_property()
    {
        var offenders = new List<string>();
        var logCall = new Regex(@"Log(Trace|Debug|Information|Warning|Error|Critical)\s*\(", RegexOptions.Compiled);

        foreach (var file in SourceFiles())
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                if (!logCall.IsMatch(lines[i])) continue;

                // Log calls wrap, so check the statement rather than one line.
                var statement = string.Join(' ', lines.Skip(i).Take(4));
                foreach (var secret in SecretProperties)
                    if (statement.Contains(secret, StringComparison.Ordinal))
                        offenders.Add($"{Path.GetFileName(file)}:{i + 1}");
            }
        }

        Assert.That(offenders, Is.Empty,
            $"Log statements reference a secret property: {string.Join(", ", offenders)}");
    }

    [Test]
    public void The_device_list_partial_never_renders_a_secret()
    {
        var root = FindProjectRoot();
        var listViews = new[]
        {
            Path.Combine(root, "MeshMuster", "Pages", "Index.cshtml"),
            Path.Combine(root, "MeshMuster", "Pages", "Shared", "_DeviceTable.cshtml"),
        };

        foreach (var view in listViews)
        {
            var markup = File.ReadAllText(view);
            foreach (var secret in SecretProperties)
                Assert.That(markup, Does.Not.Contain(secret),
                    $"{Path.GetFileName(view)} renders {secret}; secrets belong on the detail page only.");
        }
    }

    /// <summary>
    /// The properties that must never be logged or listed.
    /// </summary>
    private static readonly string[] SecretProperties = ["PrivateKey", "AdminPassword"];

    /// <summary>
    /// Every C# file in the web project.
    /// </summary>
    private static IEnumerable<string> SourceFiles()
    {
        var root = FindProjectRoot();
        return Directory.EnumerateFiles(
            Path.Combine(root, "MeshMuster"), "*.cs", SearchOption.AllDirectories);
    }

    /// <summary>
    /// Walk up from the test assembly until the solution file turns up.
    /// </summary>
    private static string FindProjectRoot()
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MeshMuster.slnx")))
            dir = dir.Parent;

        Assert.That(dir, Is.Not.Null, "Could not locate the solution root.");
        return dir!.FullName;
    }
}
