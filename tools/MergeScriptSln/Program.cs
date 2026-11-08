using Microsoft.VisualStudio.SolutionPersistence;
using Microsoft.VisualStudio.SolutionPersistence.Model;
using Microsoft.VisualStudio.SolutionPersistence.Serializer;

namespace MergeScriptSln;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            var input = args.Length == 0 ? FindSolution(Directory.GetCurrentDirectory()) : Path.GetFullPath(args[0]);
            if (args.Length > 1 || input is null)
            {
                Console.Error.WriteLine("Usage: MergeScriptSln [UEProject.sln | UEProject.slnx]");
                return 1;
            }

            await MergeAsync(input);
            return 0;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or SolutionException or ArgumentException)
        {
            Console.Error.WriteLine(error.Message);
            return 1;
        }
    }

    private static async Task MergeAsync(string input)
    {
        var projectDirectory = Path.GetDirectoryName(input)!;
        var projectName = Path.GetFileNameWithoutExtension(input);
        var extension = Path.GetExtension(input);
        ISolutionSerializer serializer = extension.ToLowerInvariant() switch
        {
            ".sln" => SolutionSerializers.SlnFileV12,
            ".slnx" => SolutionSerializers.SlnXml,
            _ => throw new ArgumentException("Expected an Unreal .sln or .slnx file.")
        };
        var output = Path.Combine(projectDirectory, $"{projectName}.mixed{extension}");
        var scriptDirectory = Path.Combine(projectDirectory, "Script");
        var projects = FindManagedProjects(scriptDirectory).ToArray();
        var solution = await serializer.OpenAsync(input, CancellationToken.None);
        var existingPaths = solution.SolutionProjects
            .Select(project => Path.GetFullPath(project.FilePath, projectDirectory))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var existingIds = solution.SolutionItems.Select(item => item.Id).ToHashSet();
        var managedIds = await ReadManagedIdsAsync(scriptDirectory, projectName);

        foreach (var path in projects)
        {
            if (!existingPaths.Add(path))
                continue;

            var relativePath = Path.GetRelativePath(projectDirectory, path);
            var folder = solution.FindFolder("/Script/") ?? solution.AddFolder("/Script/");
            var project = solution.AddProject(relativePath, folder: folder);
            if (managedIds.TryGetValue(path, out var id) && existingIds.Add(id))
                project.Id = id;

            ConfigureProject(project, solution);
            Console.WriteLine($"  + {relativePath}");
        }

        await SaveAsync(serializer, solution, output);
        Console.WriteLine($"Done: {projects.Length} Script projects in {output}");
    }

    private static IEnumerable<string> FindManagedProjects(string scriptDirectory)
    {
        if (!Directory.Exists(scriptDirectory))
            throw new DirectoryNotFoundException($"Script directory not found: {scriptDirectory}");

        return EnumerateProjects(scriptDirectory).OrderBy(path => path, StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> EnumerateProjects(string directory)
    {
        foreach (var project in Directory.EnumerateFiles(directory, "*.csproj"))
            yield return project;

        foreach (var child in Directory.EnumerateDirectories(directory))
        {
            var name = Path.GetFileName(child);
            if (name.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("obj", StringComparison.OrdinalIgnoreCase) ||
                File.GetAttributes(child).HasFlag(FileAttributes.ReparsePoint))
                continue;

            foreach (var project in EnumerateProjects(child))
                yield return project;
        }
    }

    private static async Task<Dictionary<string, Guid>> ReadManagedIdsAsync(string scriptDirectory, string projectName)
    {
        var ids = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        var managedSolutionPath = Path.Combine(scriptDirectory, $"Managed{projectName}.sln");
        if (!File.Exists(managedSolutionPath))
            return ids;

        var managedSolution = await SolutionSerializers.SlnFileV12.OpenAsync(managedSolutionPath, CancellationToken.None);
        foreach (var project in managedSolution.SolutionProjects)
            ids.TryAdd(Path.GetFullPath(project.FilePath, scriptDirectory), project.Id);

        return ids;
    }

    private static void ConfigureProject(SolutionProjectModel project, SolutionModel solution)
    {
        foreach (var configuration in solution.BuildTypes)
        {
            var target = configuration.Split(' ', 2)[0];
            var managedConfiguration = target is "Debug" or "DebugGame" ? "Debug" : "Release";
            project.AddProjectConfigurationRule(new(BuildDimension.BuildType, configuration, string.Empty, managedConfiguration));
        }

        project.AddProjectConfigurationRule(new(BuildDimension.Platform, string.Empty, string.Empty, "Any CPU"));
        // UnrealSharp publishes the managed assemblies after the native editor starts.
        project.AddProjectConfigurationRule(new(BuildDimension.Build, string.Empty, string.Empty, "false"));
        project.AddProjectConfigurationRule(new(BuildDimension.Deploy, string.Empty, string.Empty, "false"));
    }

    private static async Task SaveAsync(ISolutionSerializer serializer, SolutionModel solution, string output)
    {
        var temporary = $"{output}.{Guid.NewGuid():N}.tmp";
        try
        {
            await serializer.SaveAsync(temporary, solution, CancellationToken.None);
            await serializer.OpenAsync(temporary, CancellationToken.None);
            File.Move(temporary, output, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    private static string? FindSolution(string startDirectory)
    {
        for (var directory = startDirectory; directory != null; directory = Path.GetDirectoryName(directory))
        {
            var name = Path.GetFileName(directory);
            if (!File.Exists(Path.Combine(directory, $"{name}.uproject")))
                continue;

            foreach (var extension in new[] { ".sln", ".slnx" })
            {
                var path = Path.Combine(directory, name + extension);
                if (File.Exists(path))
                    return path;
            }
        }

        return null;
    }
}
