namespace MergeScriptSln;

public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length < 1)
        {
            var found = FindSln(Directory.GetCurrentDirectory());
            if (found == null) { Console.Error.WriteLine("Usage: MergeScriptSln [UEProject.sln]"); return 1; }
            args = new[] { found };
        }

        var ueSlnPath = Path.GetFullPath(args[0]);
        if (!File.Exists(ueSlnPath)) { Console.Error.WriteLine($"Not found: {ueSlnPath}"); return 1; }

        var projectDir = Path.GetDirectoryName(ueSlnPath)!;
        var projectName = Path.GetFileNameWithoutExtension(ueSlnPath);
        var outputPath = Path.Combine(projectDir, $"{projectName}.mixed.sln");
        var managedSlns = Directory.Exists(Path.Combine(projectDir, "Script"))
            ? Directory.GetFiles(Path.Combine(projectDir, "Script"), "*.sln", SearchOption.AllDirectories)
            : Array.Empty<string>();

        Console.WriteLine($"UE:   {Path.GetRelativePath(projectDir, ueSlnPath)}");
        Console.WriteLine($"Out:  {Path.GetRelativePath(projectDir, outputPath)}");

        try { if (File.Exists(outputPath)) File.Delete(outputPath); }
        catch (IOException) { Console.Error.WriteLine("Cannot overwrite — close IDE first."); return 1; }

        // ── Parse managed SLNs ────────────────────────────────────────
        var managedProjects = new List<ManagedProject>();
        var managedConfigs = new List<string>();
        var managedNested = new List<string>();
        var managedSlnConfigs = new List<string>();

        foreach (var slnPath in managedSlns)
        {
            Console.WriteLine($"  + {Path.GetRelativePath(projectDir, slnPath)}");
            var slnDir = Path.GetDirectoryName(slnPath)!;
            var lines = File.ReadAllLines(slnPath);

            // Project blocks
            for (int i = 0; i < lines.Length; i++)
            {
                if (!lines[i].Trim().StartsWith("Project(")) continue;

                int end = Array.FindIndex(lines, i + 1, l => l.Trim() == "EndProject");
                if (end < 0) continue;

                var parts = ExtractQuoted(lines[i]);
                if (parts.Count < 4) { i = end; continue; }

                var absPath = Path.GetFullPath(Path.Combine(slnDir, parts[2]));
                var relPath = Path.GetRelativePath(projectDir, absPath).Replace('/', '\\');

                managedProjects.Add(new ManagedProject(
                    Guid: parts[3],
                    Line: $@"Project(""{parts[0]}"") = ""{parts[1]}"", ""{relPath}"", ""{parts[3]}""",
                    IsCsProj: parts[2].EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)));

                i = end;
            }

            // Global sections
            ReadSection(lines, "SolutionConfigurationPlatforms", managedSlnConfigs);
            ReadSection(lines, "ProjectConfigurationPlatforms", managedConfigs);
            ReadSection(lines, "NestedProjects", managedNested);
        }

        // ── Merge into native lines ───────────────────────────────────
        var native = File.ReadAllLines(ueSlnPath).ToList();

        var lastEnd = native.FindLastIndex(l => l.Trim() == "EndProject");
        if (lastEnd < 0) { Console.Error.WriteLine("Invalid SLN — no EndProject found."); return 1; }
        var ins = lastEnd + 1;
        foreach (var mp in managedProjects)
            foreach (var line in new[] { mp.Line, "EndProject" })
                native.Insert(ins++, line);

        MergeSection(native, "ProjectConfigurationPlatforms", managedConfigs, null, "\t");
        MergeSection(native, "SolutionConfigurationPlatforms", managedSlnConfigs, null, "\t");
        MergeSection(native, "NestedProjects", managedNested, null, "\t\t");

        // ── Write ────────────────────────────────────────────────────
        File.WriteAllLines(outputPath, native);
        Console.WriteLine($"Done: {managedProjects.Count} managed projects");
        return 0;
    }

    // ═══════════════════════════════════════════════════════════════════
    // SLN helpers
    // ═══════════════════════════════════════════════════════════════════

    static void ReadSection(string[] lines, string sectionName, List<string> into)
    {
        bool inside = false;
        foreach (var raw in lines)
        {
            var t = raw.Trim();
            if (!inside && t.StartsWith($"GlobalSection({sectionName})"))
            {
                if (!t.EndsWith("preSolution") && !t.EndsWith("postSolution")) continue;
                inside = true;
                continue;
            }
            if (inside && t == "EndGlobalSection") { inside = false; continue; }
            if (inside && t.Contains('=')) into.Add(t);
        }
    }

    static void MergeSection(List<string> native, string sectionName, List<string> lines,
        Func<string, string>? filter, string prefix)
    {
        if (!FindSection(native, sectionName, out int start, out int end)) return;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = start + 1; i < end; i++) seen.Add(native[i].Trim());

        var insAt = end;
        foreach (var raw in lines)
        {
            if (!seen.Add(raw)) continue;
            var line = prefix + raw;
            if (filter != null) line = filter(line);
            native.Insert(insAt++, line);
        }
    }

    static bool FindSection(List<string> lines, string name, out int start, out int end)
    {
        start = end = -1;
        var prefix = $"GlobalSection({name})";
        for (int i = 0; i < lines.Count; i++)
        {
            if (!lines[i].Trim().StartsWith(prefix)) continue;
            start = i;
            for (int j = i + 1; j < lines.Count; j++)
                if (lines[j].Trim() == "EndGlobalSection") { end = j; return true; }
            return false;
        }
        return false;
    }

    // ═══════════════════════════════════════════════════════════════════
    // General helpers
    // ═══════════════════════════════════════════════════════════════════

    static List<string> ExtractQuoted(string line)
    {
        var parts = new List<string>();
        var inQuote = false;
        var start = -1;
        for (int i = 0; i < line.Length; i++)
        {
            if (line[i] != '"') continue;
            if (!inQuote) { inQuote = true; start = i + 1; }
            else { parts.Add(line[start..i]); inQuote = false; }
        }
        return parts;
    }

    static string? FindSln(string startDir)
    {
        for (var dir = startDir; dir != null; dir = Path.GetDirectoryName(dir))
        {
            var name = Path.GetFileName(dir);
            if (File.Exists(Path.Combine(dir, $"{name}.uproject")) &&
                File.Exists(Path.Combine(dir, $"{name}.sln")))
                return Path.Combine(dir, $"{name}.sln");
        }
        return null;
    }
}

record struct ManagedProject(string Guid, string Line, bool IsCsProj);
