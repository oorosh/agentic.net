using System.Text.RegularExpressions;
using Agentic.Abstractions;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Agentic.Loaders;

public sealed class FileSystemSkillLoader : ISkillLoader
{
    private readonly string _skillsPath;
    private readonly Dictionary<string, Skill> _skills = new();
    private bool _loaded;

    public FileSystemSkillLoader(string skillsPath)
    {
        _skillsPath = skillsPath;
    }

    public async Task<IReadOnlyList<Skill>> LoadSkillsAsync(CancellationToken cancellationToken = default)
    {
        if (_loaded)
        {
            return _skills.Values.ToList();
        }

        if (!Directory.Exists(_skillsPath))
        {
            _loaded = true;
            return [];
        }

        var directories = Directory.GetDirectories(_skillsPath);
        foreach (var dir in directories)
        {
            var skillFile = Path.Combine(dir, "SKILL.md");
            if (!File.Exists(skillFile))
            {
                continue;
            }

            var skill = await ParseSkillAsync(skillFile, cancellationToken);
            if (skill is not null)
            {
                _skills[skill.Name] = skill;
            }
        }

        _loaded = true;
        return _skills.Values.ToList();
    }

    public async Task<Skill?> LoadSkillAsync(string name, CancellationToken cancellationToken = default)
    {
        if (!_loaded)
        {
            await LoadSkillsAsync(cancellationToken);
        }

        if (_skills.TryGetValue(name, out var skill))
        {
            var skillFile = Path.Combine(skill.Path, "SKILL.md");
            if (File.Exists(skillFile))
            {
                var content = await File.ReadAllTextAsync(skillFile, cancellationToken);
                var (frontmatter, body) = ExtractFrontmatter(content);
                return skill with { Instructions = body };
            }
        }

        return skill;
    }

    private static async Task<Skill?> ParseSkillAsync(string skillFile, CancellationToken cancellationToken)
    {
        var content = await File.ReadAllTextAsync(skillFile, cancellationToken);
        var (frontmatter, body) = ExtractFrontmatter(content);

        if (string.IsNullOrWhiteSpace(frontmatter))
        {
            return null;
        }

        var deserializer = new DeserializerBuilder()
            .IgnoreUnmatchedProperties()
            .Build();

        try
        {
            var yaml = deserializer.Deserialize<Dictionary<string, string>>(frontmatter);
            if (yaml is null || !yaml.TryGetValue("name", out var name) || string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            if (!yaml.TryGetValue("description", out var description) || string.IsNullOrWhiteSpace(description))
            {
                return null;
            }

            var skillPath = Path.GetDirectoryName(skillFile) ?? "";

            return new Skill
            {
                Name = name,
                Description = description,
                Path = skillPath,
                License = yaml.GetValueOrDefault("license"),
                Compatibility = yaml.GetValueOrDefault("compatibility"),
                AllowedTools = yaml.GetValueOrDefault("allowedTools") ?? yaml.GetValueOrDefault("allowed-tools"),
                Instructions = body
            };
        }
        catch
        {
            return null;
        }
    }

    private static (string frontmatter, string body) ExtractFrontmatter(string content)
    {
        var match = Regex.Match(content, @"^---\s*\n(.*?)\n---\s*\n(.*)$", RegexOptions.Singleline);
        if (match.Success)
        {
            return (match.Groups[1].Value, match.Groups[2].Value);
        }
        return ("", content);
    }

    private static Dictionary<string, string>? ParseMetadata(string metadataYaml)
    {
        return null;
    }

    public static string ToPromptXml(IReadOnlyList<Skill> skills)
    {
        if (skills.Count == 0)
        {
            return string.Empty;
        }

        var lines = new List<string> { "<available_skills>" };
        foreach (var skill in skills)
        {
            lines.Add("  <skill>");
            lines.Add($"    <name>{skill.Name}</name>");
            lines.Add($"    <description>{skill.Description}</description>");
            lines.Add($"    <location>{skill.Path}</location>");
            lines.Add("  </skill>");
        }
        lines.Add("</available_skills>");

        return string.Join(Environment.NewLine, lines);
    }
}
