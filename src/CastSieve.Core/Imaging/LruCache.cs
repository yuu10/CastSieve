using System.Diagnostics.CodeAnalysis;

namespace CastSieve.Imaging;

/// <summary>
/// 容量を超えたら、いちばん長く使われていないものから捨てる小さなキャッシュ。
/// スレッドセーフではないので、1つのスレッド (画面なら UI スレッド) からだけ使うこと。
/// </summary>
public sealed class LruCache<TKey, TValue> : IDisposable where TKey : notnull
{
    private readonly int _capacity;
    private readonly Action<TValue>? _onRemove;
    private readonly Dictionary<TKey, LinkedListNode<KeyValuePair<TKey, TValue>>> _map;
    private readonly LinkedList<KeyValuePair<TKey, TValue>> _order = new(); // 先頭 = 最近使った

    /// <param name="onRemove">キャッシュから外れるときに呼ぶ (画像の Dispose など)。</param>
    public LruCache(int capacity, Action<TValue>? onRemove = null, IEqualityComparer<TKey>? comparer = null)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
        _onRemove = onRemove;
        _map = new Dictionary<TKey, LinkedListNode<KeyValuePair<TKey, TValue>>>(comparer);
    }

    public int Count => _map.Count;

    public bool Contains(TKey key) => _map.ContainsKey(key);

    public bool TryGet(TKey key, [MaybeNullWhen(false)] out TValue value)
    {
        if (_map.TryGetValue(key, out var node))
        {
            _order.Remove(node);
            _order.AddFirst(node);
            value = node.Value.Value;
            return true;
        }
        value = default;
        return false;
    }

    public void Set(TKey key, TValue value)
    {
        if (_map.TryGetValue(key, out var existing))
        {
            _order.Remove(existing);
            _map.Remove(key);
            _onRemove?.Invoke(existing.Value.Value);
        }

        _map[key] = _order.AddFirst(new KeyValuePair<TKey, TValue>(key, value));

        while (_map.Count > _capacity && _order.Last is { } last)
        {
            _order.RemoveLast();
            _map.Remove(last.Value.Key);
            _onRemove?.Invoke(last.Value.Value);
        }
    }

    public void Clear()
    {
        foreach (var pair in _order)
            _onRemove?.Invoke(pair.Value);
        _order.Clear();
        _map.Clear();
    }

    public void Dispose() => Clear();
}
