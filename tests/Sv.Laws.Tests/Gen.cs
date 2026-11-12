namespace Sv.Laws.Tests;

public sealed class Gen
{
    ulong state;

    public Gen(ulong seed)
    {
        Seed = seed;
        state = seed;
    }

    public ulong Seed { get; }

    public ulong Next()
    {
        unchecked
        {
            state += 0x9E3779B97F4A7C15ul;
            var mixed = state;
            mixed = (mixed ^ (mixed >> 30)) * 0xBF58476D1CE4E5B9ul;
            mixed = (mixed ^ (mixed >> 27)) * 0x94D049BB133111EBul;
            return mixed ^ (mixed >> 31);
        }
    }

    public int Below(int bound) => bound <= 1 ? 0 : (int)(Next() % (ulong)bound);

    public int Between(int low, int high) => (int)(low + (long)(Next() % (ulong)((long)high - low + 1)));

    public bool Chance(int percent) => Below(100) < percent;

    public int AnyInt() => (int)Next();

    public static readonly int[] EdgeInts = { int.MinValue, int.MinValue + 1, -10001, -10000, -9999, -2, -1, 0, 1, 2, 9999, 10000, 10001, 20000, int.MaxValue - 1, int.MaxValue };

    public int EdgeOrAny() => Chance(40) ? EdgeInts[Below(EdgeInts.Length)] : Chance(50) ? Between(-30000, 30000) : AnyInt();

    public void Shuffle<T>(IList<T> items)
    {
        for (var at = items.Count - 1; at > 0; at--)
        {
            var other = Below(at + 1);
            (items[at], items[other]) = (items[other], items[at]);
        }
    }

    public override string ToString() => $"seed {Seed}";
}

public static class Seeds
{
    public static IEnumerable<object[]> Few() => new[] { 1ul, 2ul, 3ul, 17ul, 4242ul }.Select(seed => new object[] { seed });

    public static IEnumerable<object[]> Many() => Enumerable.Range(1, 16).Select(seed => new object[] { (ulong)seed * 7919ul });
}
