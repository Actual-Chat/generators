namespace ActualLab.Generators.Internal;

public static class EnumerableExt
{
    public static IEnumerable<AutoInjectGenerator.DependencyInfo> Reindex(
        this IEnumerable<AutoInjectGenerator.DependencyInfo> source)
    {
        var index = 0;
        foreach (var dependencyInfo in source)
            yield return dependencyInfo with { Index = index++ };
    }

#if NETSTANDARD2_0

    public static HashSet<TSource> ToHashSet<TSource>(this IEnumerable<TSource> source)
        => new(source);

#endif
#if !NET6_0_OR_GREATER

    public static IEnumerable<T> DistinctBy<T, TKey>(this IEnumerable<T> source, Func<T, TKey> keySelector)
    {
        var hashSet = new HashSet<TKey>();
        foreach (var item in source) {
            var key = keySelector(item);
            if (hashSet.Add(key))
                yield return item;
        }
    }

#endif
}
