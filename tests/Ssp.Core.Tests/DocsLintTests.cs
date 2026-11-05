using System.Text.RegularExpressions;

namespace Ssp.Core.Tests;

/// <summary>
/// The documents are short and plain, for people and for agents (#426).
/// A decision record has four sections. A link points to a file that exists. A document does not
/// record how a work session went.
/// </summary>
public sealed partial class DocsLintTests
{
    static string Root => RepoPaths.Root;

    static string Decisions => Path.Combine(Root, "docs", "decisions");

    // A decision record has these four sections, in this order, and no other section.
    static readonly string[] RecordHeadings = ["## Status", "## Context", "## Decision", "## Consequences"];

    // A document does not name an agent session, a wave, or a scratchpad path.
    static readonly string[] Forbidden =
    [
        "scratchpad", ".claude/", "worktree", "agents-per-wave", "wave plan", "session config",
        "this session", "session log",
    ];

    // A document does not date a work session.
    [GeneratedRegex(@"\b20\d\d-\d\d-\d\d\b")]
    private static partial Regex WorkDate();

    [GeneratedRegex(@"\[[^\]]*\]\(([^)]+)\)")]
    private static partial Regex Link();

    // The repository documents: the two files that a person or an agent reads first, the documents
    // folder, and the two documents that the code refers to.
    static IEnumerable<string> Docs()
    {
        var fixedFiles = new[] { "README.md", "AGENTS.md", "CONTRIBUTING.md", "models/README.md" };
        var docs = Directory.GetFiles(Path.Combine(Root, "docs"), "*.md", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .Select(f => Path.GetRelativePath(Root, f).Replace('\\', '/'));
        return fixedFiles.Concat(docs);
    }

    static IEnumerable<string> Records() =>
        Directory.GetFiles(Decisions, "*.md")
            .Where(f => !Path.GetFileName(f).Equals("README.md", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal);

    [Fact]
    public void The_decision_index_lists_every_record()
    {
        var records = Records().ToList();
        Assert.NotEmpty(records);
        Assert.True(File.Exists(Path.Combine(Decisions, "README.md")), "docs/decisions/README.md is missing");

        var index = File.ReadAllText(Path.Combine(Decisions, "README.md"));
        var missing = records
            .Where(r => !index.Contains(Path.GetFileName(r), StringComparison.Ordinal))
            .Select(r => Path.GetFileName(r));
        Assert.Empty(missing);
    }

    [Fact]
    public void Each_decision_record_has_the_four_sections_in_order()
    {
        var wrong = new List<string>();
        foreach (var record in Records())
        {
            var headings = File.ReadAllLines(record)
                .Where(l => l.StartsWith("## ", StringComparison.Ordinal))
                .ToArray();
            if (!headings.SequenceEqual(RecordHeadings))
                wrong.Add($"{Path.GetFileName(record)}: [{string.Join(", ", headings)}]");
        }

        Assert.Empty(wrong);
    }

    [Fact]
    public void Each_decision_record_is_at_most_sixty_lines()
    {
        var longRecords = Records()
            .Select(r => (Name: Path.GetFileName(r), Lines: File.ReadAllLines(r).Length))
            .Where(r => r.Lines > 60)
            .Select(r => $"{r.Name}: {r.Lines} lines");
        Assert.Empty(longRecords);
    }

    [Fact]
    public void Every_relative_link_points_to_a_file_that_exists()
    {
        var broken = new List<string>();
        foreach (var doc in Docs())
        {
            var text = File.ReadAllText(Path.Combine(Root, doc));
            foreach (Match match in Link().Matches(text))
            {
                var url = match.Groups[1].Value.Trim();
                if (url.StartsWith("http", StringComparison.Ordinal)
                    || url.StartsWith("mailto:", StringComparison.Ordinal)
                    || url.StartsWith('#'))
                    continue;

                // Drop a fragment and a link title. A path may be in angle brackets.
                url = url.Split(' ')[0].Split('#')[0].Trim('<', '>');
                if (url.Length == 0) continue;

                var target = Path.GetFullPath(Path.Combine(Root, Path.GetDirectoryName(doc) ?? "", url));
                if (!File.Exists(target) && !Directory.Exists(target))
                    broken.Add($"{doc}: {match.Groups[1].Value}");
            }
        }

        Assert.Empty(broken);
    }

    [Fact]
    public void No_doc_names_a_work_session()
    {
        var found = new List<string>();
        foreach (var doc in Docs())
        {
            var lines = File.ReadAllLines(Path.Combine(Root, doc));
            for (var i = 0; i < lines.Length; i++)
            {
                foreach (var word in Forbidden.Where(w => lines[i].Contains(w, StringComparison.OrdinalIgnoreCase)))
                    found.Add($"{doc}:{i + 1}: {word}");
            }
        }

        Assert.Empty(found);
    }

    [Fact]
    public void No_doc_dates_a_work_session()
    {
        var found = new List<string>();
        foreach (var doc in Docs())
        {
            var lines = File.ReadAllLines(Path.Combine(Root, doc));
            for (var i = 0; i < lines.Length; i++)
                foreach (Match match in WorkDate().Matches(lines[i]))
                    found.Add($"{doc}:{i + 1}: {match.Value}");
        }

        Assert.Empty(found);
    }
}
