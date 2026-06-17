using Lodestone.Domain;

namespace Lodestone.Application.Library;

/// <summary>
/// One category section of the grouped "My Content" view: the source category slug (or the
/// <see cref="LibraryGrouping.UncategorizedKey"/> sentinel) and the items under it, in their original order.
/// </summary>
public sealed record CategoryGroup(string Key, IReadOnlyList<InstalledContent> Items);

/// <summary>
/// Lays the un-filtered ("All categories") library out as category sections. Each item is placed under a
/// single section — its <em>primary</em> (first declared) category — so a multi-tag mod is listed once, not
/// duplicated. Items that declare no category fall into a trailing "uncategorized" bucket. Sections are
/// ordered by slug to mirror the My Content category dropdown, with "uncategorized" pinned last.
/// </summary>
public static class LibraryGrouping
{
    /// <summary>Sentinel key for the section holding items that declare no category.</summary>
    public const string UncategorizedKey = "uncategorized";

    /// <summary>
    /// Groups <paramref name="items"/> by their primary category, preserving the incoming order within each
    /// section and ordering the sections to match the category dropdown ("uncategorized" last).
    /// </summary>
    public static IReadOnlyList<CategoryGroup> ByPrimaryCategory(IEnumerable<InstalledContent> items)
    {
        // Build the buckets in first-seen order, then sort only the section order — the rows inside a
        // section keep the caller's ordering, exactly like the flat list does.
        var buckets = new Dictionary<string, List<InstalledContent>>(StringComparer.Ordinal);

        foreach (InstalledContent item in items)
        {
            string key = PrimaryCategoryOf(item);
            if (!buckets.TryGetValue(key, out List<InstalledContent>? bucket))
            {
                bucket = [];
                buckets[key] = bucket;
            }

            bucket.Add(item);
        }

        return buckets
            .OrderBy(b => b.Key == UncategorizedKey)        // the uncategorized bucket always sorts last
            .ThenBy(b => b.Key, StringComparer.Ordinal)     // then alphabetically by slug, matching the dropdown
            .Select(b => new CategoryGroup(b.Key, b.Value))
            .ToList();
    }

    // The primary category is the first non-blank declared category, normalised to a lower-case slug so it
    // matches the dropdown's keys; items with no category resolve to the uncategorized sentinel.
    private static string PrimaryCategoryOf(InstalledContent item)
    {
        foreach (string category in item.Categories)
        {
            if (!string.IsNullOrWhiteSpace(category))
            {
                return category.Trim().ToLowerInvariant();
            }
        }

        return UncategorizedKey;
    }
}
