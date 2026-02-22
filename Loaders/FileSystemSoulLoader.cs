using System.Text.RegularExpressions;
using Agentic.Abstractions;

namespace Agentic.Loaders;

public sealed class FileSystemSoulLoader : ISoulLoader
{
    private readonly string _soulFilePath;
    private SoulDocument? _cached;

    public FileSystemSoulLoader(string soulFilePath)
    {
        _soulFilePath = soulFilePath;
    }

    public FileSystemSoulLoader(DirectoryInfo directory)
        : this(Path.Combine(directory.FullName, "SOUL.md"))
    {
    }

    public async Task<SoulDocument?> LoadSoulAsync(CancellationToken cancellationToken = default)
    {
        if (_cached is not null)
        {
            return _cached;
        }

        if (!File.Exists(_soulFilePath))
        {
            return null;
        }

        var content = await File.ReadAllTextAsync(_soulFilePath, cancellationToken);
        _cached = ParseSoulDocument(content, _soulFilePath);
        return _cached;
    }

    private static SoulDocument ParseSoulDocument(string content, string filePath)
    {
        var lines = content.Split('\n');
        string? name = null;
        var currentSection = "";
        var sectionContent = new List<string>();
        var sections = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            
            if (trimmed.StartsWith("# ") && name is null)
            {
                name = trimmed.TrimStart('#').Trim();
            }
            else if (Regex.IsMatch(trimmed, @"^#{2}\s+\w+"))
            {
                if (currentSection.Length > 0)
                {
                    sections[currentSection] = sectionContent;
                }
                
                currentSection = trimmed.TrimStart('#').Trim();
                sectionContent = [];
            }
            else if (!string.IsNullOrWhiteSpace(trimmed))
            {
                sectionContent.Add(trimmed);
            }
        }

        if (currentSection.Length > 0)
        {
            sections[currentSection] = sectionContent;
        }

        name ??= Path.GetFileNameWithoutExtension(filePath);

        return new SoulDocument
        {
            Name = name,
            Role = GetSectionContent(sections, "Role"),
            Personality = GetSectionContent(sections, "Personality"),
            Rules = GetSectionContent(sections, "Rules"),
            OutputFormat = GetSectionContent(sections, "Output Format"),
            Tools = GetSectionContent(sections, "Tools"),
            Handoffs = GetSectionContent(sections, "Handoffs"),
            RawContent = content
        };
    }

    private static string? GetSectionContent(Dictionary<string, List<string>> sections, string sectionName)
    {
        if (sections.TryGetValue(sectionName, out var content) && content.Count > 0)
        {
            return string.Join("\n", content);
        }
        return null;
    }

    public static string ToSystemPrompt(SoulDocument soul)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(soul.Role))
        {
            parts.Add(soul.Role);
        }

        if (!string.IsNullOrWhiteSpace(soul.Personality))
        {
            parts.Add($"Personality:\n{soul.Personality}");
        }

        if (!string.IsNullOrWhiteSpace(soul.Rules))
        {
            parts.Add($"Rules:\n{soul.Rules}");
        }

        if (!string.IsNullOrWhiteSpace(soul.Tools))
        {
            parts.Add($"Available Tools:\n{soul.Tools}");
        }

        if (!string.IsNullOrWhiteSpace(soul.OutputFormat))
        {
            parts.Add($"Output Format:\n{soul.OutputFormat}");
        }

        if (!string.IsNullOrWhiteSpace(soul.Handoffs))
        {
            parts.Add($"Handoffs:\n{soul.Handoffs}");
        }

        return string.Join("\n\n", parts);
    }
}
