namespace Lodestone.Infrastructure.Archives;

/// <summary>One parsed section/array-entry from a TOML document.</summary>
internal sealed record TomlBlock(string Header, Dictionary<string, string> Values);

/// <summary>
/// A deliberately tiny TOML reader covering only what Forge/NeoForge <c>mods.toml</c> needs:
/// section/array-of-tables headers and <c>key = value</c> pairs (quoted strings, booleans), with
/// comment stripping. Far lighter than a full TOML library for this narrow, well-defined input.
/// </summary>
internal static class SimpleToml
{
    public static List<TomlBlock> ParseBlocks(string text)
    {
        var blocks = new List<TomlBlock>();
        TomlBlock? current = null;

        foreach (string rawLine in text.Split('\n'))
        {
            string line = StripComment(rawLine).Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (line.StartsWith("[[", StringComparison.Ordinal) && line.EndsWith("]]", StringComparison.Ordinal))
            {
                current = new TomlBlock(line[2..^2].Trim(), new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
                blocks.Add(current);
            }
            else if (line.StartsWith('[') && line.EndsWith(']'))
            {
                current = new TomlBlock(line[1..^1].Trim(), new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
                blocks.Add(current);
            }
            else if (current is not null)
            {
                int eq = line.IndexOf('=', StringComparison.Ordinal);
                if (eq > 0)
                {
                    string key = line[..eq].Trim();
                    string value = Unquote(line[(eq + 1)..].Trim());
                    current.Values[key] = value;
                }
            }
        }

        return blocks;
    }

    private static string StripComment(string line)
    {
        bool inQuotes = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (c == '#' && !inQuotes)
            {
                return line[..i];
            }
        }

        return line;
    }

    private static string Unquote(string value)
    {
        if (value.Length >= 2 &&
            ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
        {
            return value[1..^1];
        }

        return value;
    }
}
