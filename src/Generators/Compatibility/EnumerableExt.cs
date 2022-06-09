namespace ActualLab.Generators.Compatibility;

public static class EnumerableCompatExt
{
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
