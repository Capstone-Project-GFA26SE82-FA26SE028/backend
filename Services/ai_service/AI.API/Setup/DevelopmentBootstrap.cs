using System.Diagnostics;

namespace AI.API.Setup;

public static class DevelopmentBootstrap
{
    public static bool ShouldRun(string environment, bool enabled, string? completed)
    {
        return environment.Equals("Development", StringComparison.OrdinalIgnoreCase)
            && enabled && completed != "1";
    }

    public static string? FindScript(string contentRoot)
    {
        var directory = new DirectoryInfo(contentRoot);
        while (directory is not null)
        {
            var path = Path.Combine(directory.FullName, "scripts", "Start-AI.ps1");
            if (File.Exists(path) && File.Exists(Path.Combine(directory.FullName, "Services", "ai_service", "AI.API", "AI.API.csproj")))
            {
                return path;
            }

            directory = directory.Parent;
        }

        return null;
    }

    public static async Task<bool> Run(IConfiguration configuration, string contentRoot)
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("Automatic native AI setup supports Windows only. Use Docker or prepare Ollama manually; AI will still start and report dependency errors normally.");
            return false;
        }

        var script = FindScript(contentRoot);
        if (script is null)
        {
            Console.Error.WriteLine("AI setup script was not found in this source checkout. Run scripts/Start-AI.ps1 manually or configure Ollama before starting AI.");
            return false;
        }

        Console.WriteLine("Preparing the local development AI runtime. The first model download can take several minutes.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(35));
        try
        {
            var start = new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"),
                WorkingDirectory = Path.GetDirectoryName(Path.GetDirectoryName(script))!,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            start.ArgumentList.Add("-NoProfile");
            start.ArgumentList.Add("-ExecutionPolicy");
            start.ArgumentList.Add("Bypass");
            start.ArgumentList.Add("-File");
            start.ArgumentList.Add(script);
            start.ArgumentList.Add("-Mode");
            start.ArgumentList.Add("Local");
            start.ArgumentList.Add("-SetupOnly");
            start.ArgumentList.Add("-RuntimeOnly");
            start.Environment["Ollama__Endpoint"] = configuration["Ollama:Endpoint"];
            using var process = Process.Start(start)
                ?? throw new InvalidOperationException("Could not start the development runtime check.");
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }

                throw;
            }

            if (process.ExitCode != 0)
            {
                Console.Error.WriteLine("Local AI runtime setup failed. Follow the setup output above. AI will start with controlled dependency errors until Ollama is ready.");
                return false;
            }

            return await ModelSetup.Run(configuration) == 0;
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or IOException or InvalidOperationException or OperationCanceledException)
        {
            Console.Error.WriteLine($"Local AI setup did not complete: {error.Message}. Run scripts/Start-AI.ps1 -Mode Local -SetupOnly to diagnose. No other backend service was stopped.");
            return false;
        }
    }
}
