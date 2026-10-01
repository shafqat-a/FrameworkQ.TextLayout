namespace TextMagic.Text;

/// <summary>A dictionary phrase found in text.</summary>
internal sealed record PhraseHit<T>(int Start, int Length, T Value)
{
    public int End => Start + Length;
}

/// <summary>
/// Aho–Corasick multi-pattern matching over lower-cased text: every phrase of a gazetteer found in
/// one pass, whatever the number of phrases. Keeps whole-word matches only and resolves overlaps
/// longest-first ("google ads" beats "google"; "web conversions" beats "conversions").
/// </summary>
internal sealed class PhraseMatcher<T>
{
    private sealed class Node
    {
        public readonly Dictionary<char, Node> Next = new();
        public Node? Fail;
        public readonly List<(int Length, T Value)> Outputs = new();
    }

    private readonly Node _root = new();

    public PhraseMatcher(IEnumerable<(string Phrase, T Value)> phrases)
    {
        foreach (var (phrase, value) in phrases)
        {
            var p = phrase.Trim().ToLowerInvariant();
            if (p.Length == 0) continue;
            var node = _root;
            foreach (var ch in p)
            {
                if (!node.Next.TryGetValue(ch, out var child)) node.Next[ch] = child = new Node();
                node = child;
            }
            node.Outputs.Add((p.Length, value));
        }

        // Breadth-first failure links.
        var queue = new Queue<Node>();
        foreach (var child in _root.Next.Values) { child.Fail = _root; queue.Enqueue(child); }
        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            foreach (var (ch, child) in node.Next)
            {
                var f = node.Fail;
                while (f is not null && !f.Next.ContainsKey(ch)) f = f.Fail;
                child.Fail = f?.Next[ch] ?? _root;
                if (ReferenceEquals(child.Fail, child)) child.Fail = _root;
                child.Outputs.AddRange(child.Fail.Outputs);
                queue.Enqueue(child);
            }
        }
    }

    public IReadOnlyList<PhraseHit<T>> FindAll(string text)
    {
        var lower = text.ToLowerInvariant();
        var raw = new List<PhraseHit<T>>();
        var node = _root;
        for (var i = 0; i < lower.Length; i++)
        {
            var ch = lower[i];
            while (!ReferenceEquals(node, _root) && !node.Next.ContainsKey(ch)) node = node.Fail!;
            if (node.Next.TryGetValue(ch, out var next)) node = next;
            foreach (var (len, value) in node.Outputs)
            {
                var start = i - len + 1;
                if (IsBoundary(lower, start - 1) && IsBoundary(lower, i + 1)) raw.Add(new PhraseHit<T>(start, len, value));
            }
        }

        // Longest first, then earliest; drop anything overlapping a kept hit.
        var kept = new List<PhraseHit<T>>();
        foreach (var hit in raw.OrderByDescending(h => h.Length).ThenBy(h => h.Start))
            if (!kept.Any(k => hit.Start < k.End && k.Start < hit.End)) kept.Add(hit);
        return kept.OrderBy(h => h.Start).ToList();
    }

    private static bool IsBoundary(string s, int index)
        => index < 0 || index >= s.Length || !(char.IsLetterOrDigit(s[index]) || s[index] == '_');
}
