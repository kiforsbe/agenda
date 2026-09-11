namespace Agenda.Providers;

public static class FeedConfigurationOrder
{
    // A reorder must be a permutation, never an implicit add, removal, or edit.
    public static IReadOnlyList<FeedConfiguration> Apply(
        IReadOnlyList<FeedConfiguration> feeds, IReadOnlyList<string> orderedIds)
    {
        if(feeds.Count!=orderedIds.Count)
            throw new ArgumentException("The reordered list must contain every feed.",nameof(orderedIds));
        var remaining=feeds.ToDictionary(feed=>feed.Id,StringComparer.Ordinal);
        var ordered=new FeedConfiguration[feeds.Count];
        for(var i=0;i<ordered.Length;i++) {
            if(!remaining.Remove(orderedIds[i],out var feed))
                throw new ArgumentException("The reordered list contains an unknown or duplicate feed.",nameof(orderedIds));
            ordered[i]=feed;
        }
        return ordered;
    }
}
