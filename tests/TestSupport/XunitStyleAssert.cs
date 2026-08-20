using NUnit.Framework;

namespace TestSupport;

/// <summary>
/// The xUnit assertion surface this suite was written against, implemented on NUnit
/// constraints — assertion SEMANTICS are identical, so the NUnit migration could not
/// silently weaken any test. Aliased project-wide as `Assert` via GlobalUsings.
/// </summary>
public static class XAssert
{
    public static void Equal<T>(T expected, T actual) =>
        NUnit.Framework.Assert.That(actual, Is.EqualTo(expected));

    public static void Equal<T>(IEnumerable<T> expected, IEnumerable<T> actual) =>
        NUnit.Framework.Assert.That(actual, Is.EqualTo(expected).AsCollection);

    public static void True(bool condition) =>
        NUnit.Framework.Assert.That(condition, Is.True);

    public static void True(bool condition, string userMessage) =>
        NUnit.Framework.Assert.That(condition, Is.True, userMessage);

    public static void False(bool condition) =>
        NUnit.Framework.Assert.That(condition, Is.False);

    public static void False(bool condition, string userMessage) =>
        NUnit.Framework.Assert.That(condition, Is.False, userMessage);

    // xUnit accepts nullable booleans (null fails both).
    public static void True(bool? condition) =>
        NUnit.Framework.Assert.That(condition, Is.True);

    public static void False(bool? condition) =>
        NUnit.Framework.Assert.That(condition, Is.False);

    public static void Null(object? value) =>
        NUnit.Framework.Assert.That(value, Is.Null);

    public static void NotNull(object? value) =>
        NUnit.Framework.Assert.That(value, Is.Not.Null);

    public static void Empty(System.Collections.IEnumerable collection) =>
        NUnit.Framework.Assert.That(collection, Is.Empty);

    public static void NotEmpty(System.Collections.IEnumerable collection) =>
        NUnit.Framework.Assert.That(collection, Is.Not.Empty);

    public static void Contains(string expectedSubstring, string? actualString) =>
        NUnit.Framework.Assert.That(actualString, Does.Contain(expectedSubstring));

    public static void Contains<T>(T expected, IEnumerable<T> collection) =>
        NUnit.Framework.Assert.That(collection, Does.Contain(expected));

    public static void Contains<T>(IEnumerable<T> collection, Func<T, bool> filter) =>
        NUnit.Framework.Assert.That(collection.Any(filter), Is.True,
            "Collection contains no element matching the filter.");

    public static void DoesNotContain(string expectedSubstring, string? actualString) =>
        NUnit.Framework.Assert.That(actualString, Does.Not.Contain(expectedSubstring));

    public static void DoesNotContain<T>(T expected, IEnumerable<T> collection) =>
        NUnit.Framework.Assert.That(collection, Does.Not.Contain(expected));

    public static void DoesNotContain<T>(IEnumerable<T> collection, Func<T, bool> filter) =>
        NUnit.Framework.Assert.That(collection.Any(filter), Is.False,
            "Collection contains an element matching the filter.");

    public static T Single<T>(IEnumerable<T> collection)
    {
        var items = collection.ToList();
        NUnit.Framework.Assert.That(items, Has.Count.EqualTo(1),
            $"Expected exactly one element, found {items.Count}.");
        return items[0];
    }

    public static T Single<T>(IEnumerable<T> collection, Func<T, bool> predicate)
    {
        var matches = collection.Where(predicate).ToList();
        NUnit.Framework.Assert.That(matches, Has.Count.EqualTo(1),
            $"Expected exactly one matching element, found {matches.Count}.");
        return matches[0];
    }

    public static void StartsWith(string expectedStartString, string? actualString) =>
        NUnit.Framework.Assert.That(actualString, Does.StartWith(expectedStartString));

    public static void All<T>(IEnumerable<T> collection, Action<T> action)
    {
        foreach (var item in collection)
        {
            action(item);
        }
    }

    public static void InRange<T>(T actual, T low, T high) where T : IComparable<T> =>
        NUnit.Framework.Assert.That(
            actual.CompareTo(low) >= 0 && actual.CompareTo(high) <= 0, Is.True,
            $"Value {actual} is not in range [{low}, {high}].");

    public static TException Throws<TException>(Action testCode) where TException : Exception =>
        NUnit.Framework.Assert.Throws<TException>(() => testCode())!;

    public static void Fail(string message) =>
        NUnit.Framework.Assert.Fail(message);
}

/// <summary>
/// Drop-in replacement for xUnit's TheoryData as an NUnit TestCaseSource: each Add call
/// (or collection-initializer row) is one test case's argument list.
/// </summary>
public sealed class TestCases : System.Collections.Generic.IEnumerable<object[]>
{
    private readonly List<object[]> _rows = new();

    public void Add(params object[] arguments) => _rows.Add(arguments);

    public IEnumerator<object[]> GetEnumerator() => _rows.GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}
