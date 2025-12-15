using System.Diagnostics;

var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));

Console.WriteLine("Todo Refactor Dataset (.NET Core)");
Console.WriteLine("---------------------------------");
Console.WriteLine("This launcher helps you explore the BEFORE and AFTER console versions of the todo app.");
Console.WriteLine("Type 'before' or 'after' to run a specific version, or 'exit' to quit.");

while (true)
{
    Console.Write("\nCommand (before/after/help/exit): ");
    var command = Console.ReadLine()?.Trim().ToLowerInvariant();

    switch (command)
    {
        case "before":
            RunProject("repository_before");
            break;
        case "after":
            RunProject("repository_after");
            break;
        case "help":
            PrintHelp();
            break;
        case "exit":
            Console.WriteLine("Bye!");
            return;
        default:
            Console.WriteLine("Unknown command. Type 'help' for instructions.");
            break;
    }
}

void RunProject(string folder)
{
    var projectPath = Path.Combine(repoRoot, folder, $"{folder}.csproj");
    if (!File.Exists(projectPath))
    {
        Console.WriteLine($"Project not found at {projectPath}. Make sure it exists.");
        return;
    }

    var psi = new ProcessStartInfo("dotnet", $"run --project \"{projectPath}\"")
    {
        UseShellExecute = false,
        RedirectStandardInput = false,
        RedirectStandardOutput = false,
        RedirectStandardError = false,
        WorkingDirectory = repoRoot
    };

    Console.WriteLine($"\nLaunching '{folder}' (Ctrl+C to stop the child process)...");
    using var process = Process.Start(psi);
    process?.WaitForExit();
    Console.WriteLine($"\n'{folder}' finished with exit code {process?.ExitCode ?? -1}.");
}

void PrintHelp()
{
    Console.WriteLine(@"
before - Runs the legacy console implementation located in repository_before/.
after  - Runs the refactored console implementation in repository_after/.
exit   - Close the launcher.

You can also run each project independently with:
    dotnet run --project repository_before/repository_before.csproj
    dotnet run --project repository_after/repository_after.csproj
");
}
