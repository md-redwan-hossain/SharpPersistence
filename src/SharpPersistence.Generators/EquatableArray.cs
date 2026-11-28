using System.Collections;
using System.Collections.Immutable;

namespace SharpPersistence.Generators;

internal readonly struct EquatableArray<T>(ImmutableArray<T> array) : IEquatable<EquatableArray<T>>, IEnumerable<T>
    where T : IEquatable<T>
{
    public static readonly EquatableArray<T> Empty = [];

    private readonly ImmutableArray<T> _array = array.IsDefault ? [] : array;

    public int Length => _array.IsDefault ? 0 : _array.Length;

    public T this[int index] => _array[index];

    public bool Equals(EquatableArray<T> other)
    {
        var left = _array.IsDefault ? [] : _array;
        var right = other._array.IsDefault ? [] : other._array;

        if (left.Length != right.Length)
        {
            return false;
        }

        for (var i = 0; i < left.Length; i++)
        {
            if (!left[i].Equals(right[i]))
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

    public override int GetHashCode()
    {
        if (_array.IsDefaultOrEmpty)
        {
            return 0;
        }

        unchecked
        {
            var hash = 17;
            foreach (var t in _array)
            {
                hash = (hash * 31) + (t?.GetHashCode() ?? 0);
            }

            return hash;
        }
    }

    public ImmutableArray<T>.Enumerator GetEnumerator() =>
        (_array.IsDefault ? [] : _array).GetEnumerator();

    IEnumerator<T> IEnumerable<T>.GetEnumerator() =>
        ((IEnumerable<T>)(_array.IsDefault ? [] : _array)).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() =>
        ((IEnumerable)(_array.IsDefault ? [] : _array)).GetEnumerator();
}
