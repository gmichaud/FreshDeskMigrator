
using System.Text.Json;

public static partial class JsonExtensions
{
    public static IEnumerable<JsonElement> DescendantPropertyValues(this JsonElement element, string name, StringComparison comparison = StringComparison.Ordinal)
    {
        if (name == null)
            throw new ArgumentNullException();
        return DescendantPropertyValues(element, n => name.Equals(n, comparison));
    }

    public static IEnumerable<JsonElement> DescendantPropertyValues(this JsonElement element, Predicate<string> match)
    {
        if (match == null)
            throw new ArgumentNullException();
        var query = RecursiveEnumerableExtensions.Traverse(
            (Name: (string)null, Value: element),
            t =>
            {
                switch (t.Value.ValueKind)
                {
                    case JsonValueKind.Array:
                        return t.Value.EnumerateArray().Select(i => ((string)null, i));
                    case JsonValueKind.Object:
                        return t.Value.EnumerateObject().Select(p => (p.Name, p.Value));
                    default:
                        return Enumerable.Empty<(string, JsonElement)>();
                }
            }, false)
            .Where(t => t.Name != null && match(t.Name))
            .Select(t => t.Value);
        return query;
    }
}

public static partial class RecursiveEnumerableExtensions
{
    // Rewritten from the answer by Eric Lippert https://stackoverflow.com/users/88656/eric-lippert
    // to "Efficient graph traversal with LINQ - eliminating recursion" https://stackoverflow.com/questions/10253161/efficient-graph-traversal-with-linq-eliminating-recursion
    // to ensure items are returned in the order they are encountered.
    public static IEnumerable<T> Traverse<T>(
        T root,
        Func<T, IEnumerable<T>> children, bool includeSelf = true)
    {
        if (includeSelf)
            yield return root;
        var stack = new Stack<IEnumerator<T>>();
        try
        {
            stack.Push(children(root).GetEnumerator());
            while (stack.Count != 0)
            {
                var enumerator = stack.Peek();
                if (!enumerator.MoveNext())
                {
                    stack.Pop();
                    enumerator.Dispose();
                }
                else
                {
                    yield return enumerator.Current;
                    stack.Push(children(enumerator.Current).GetEnumerator());
                }
            }
        }
        finally
        {
            foreach (var enumerator in stack)
                enumerator.Dispose();
        }
    }
}