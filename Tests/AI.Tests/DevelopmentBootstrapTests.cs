using AI.API.Setup;

namespace AI.Tests;

public sealed class DevelopmentBootstrapTests
{
    [Theory]
    [InlineData("Development", true, null, true)]
    [InlineData("Development", true, "1", false)]
    [InlineData("Development", false, null, false)]
    [InlineData("Docker", true, null, false)]
    [InlineData("Production", true, null, false)]
    [InlineData("Staging", true, null, false)]
    public void OnlyExplicitDevelopmentStartupRunsSetup(string environment, bool enabled, string? completed, bool expected)
    {
        Assert.Equal(expected, DevelopmentBootstrap.ShouldRun(environment, enabled, completed));
    }

    [Fact]
    public void ScriptDiscoveryDoesNotDependOnDeveloperDrivePaths()
    {
        var root = Path.Combine(Path.GetTempPath(), "ai-bootstrap-test-" + Guid.NewGuid().ToString("N"));
        var scripts = Path.Combine(root, "scripts");
        var project = Path.Combine(root, "Services", "ai_service", "AI.API");
        Directory.CreateDirectory(scripts);
        Directory.CreateDirectory(project);
        var script = Path.Combine(scripts, "Start-AI.ps1");
        var marker = Path.Combine(project, "AI.API.csproj");
        try
        {
            File.WriteAllText(script, "# Test fixture; never executed.");
            File.WriteAllText(marker, "<Project />");
            Assert.Equal(script, DevelopmentBootstrap.FindScript(project));
        }
        finally
        {
            File.Delete(script);
            File.Delete(marker);
            Directory.Delete(project);
            Directory.Delete(Path.GetDirectoryName(project)!);
            Directory.Delete(Path.Combine(root, "Services"));
            Directory.Delete(scripts);
            Directory.Delete(root);
        }
    }
}
