using System.Diagnostics;

namespace SaasPOS.Tests.Integration;

/// <summary>
/// Skips the test if Docker is not available on the machine.
/// Integration tests with Testcontainers require a running Docker daemon.
/// </summary>
public sealed class DockerAvailableFactAttribute : FactAttribute
{
    public DockerAvailableFactAttribute()
    {
        if (!IsDockerAvailable())
        {
            Skip = "Docker is not available. Testcontainers integration tests require Docker.";
        }
    }

    private static bool IsDockerAvailable()
    {
        try
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = "docker",
                Arguments = "info",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            process.Start();
            process.WaitForExit(5000);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
