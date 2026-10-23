using System;

namespace Sv;

/// <summary>
/// The composition of intrinsic updates <c>x ↦ clamp(x + s, f, c)</c> (atom 50's saturate-then-clamp
/// totality): <see cref="Then"/> composes two updates in order, <see cref="Repeat"/> is the closed-form
/// n-th power, and <see cref="Apply"/> evaluates one. Canonical form — bounds tightened to the image of
/// int, constant maps carrying <see cref="Shift"/> == 0 — makes equal structs equal functions. Order is
/// semantics: under a ceiling, heal-then-hit differs from hit-then-heal.
/// </summary>
public readonly struct ClampShift
{
    /// <summary>Accumulated delta; 0 for identity and constant maps.</summary>
    public readonly long Shift;

    /// <summary>Lower bound after canonical tightening; always ≤ <see cref="Ceiling"/>.</summary>
    public readonly int Floor;

    /// <summary>Upper bound after canonical tightening; always ≥ <see cref="Floor"/>.</summary>
    public readonly int Ceiling;

    ClampShift(long shift, int floor, int ceiling)
    {
        Shift = shift;
        Floor = floor;
        Ceiling = ceiling;
    }

    /// <summary>The map x ↦ x.</summary>
    public static ClampShift Identity => new(0, int.MinValue, int.MaxValue);

    /// <summary>
    /// One update <c>x ↦ clamp(x + delta, floor, ceiling)</c>; crossed bounds are rejected
    /// (their defined meaning is a bus matter, clause 10).
    /// </summary>
    public static ClampShift Step(int delta, int floor, int ceiling)
    {
        if (floor > ceiling)
        {
            throw new ArgumentException("sv: floor above ceiling");
        }

        return Canonical(delta, floor, ceiling);
    }

    /// <summary>Evaluate the map at one value.</summary>
    public int Apply(int value) => ClampShifted(value, Shift, Floor, Ceiling);

    /// <summary>This update followed by <paramref name="next"/>: the accumulated shift adds, and this map's bounds are pushed through <paramref name="next"/>.</summary>
    public ClampShift Then(ClampShift next) => Canonical(Shift + next.Shift, next.Apply(Floor), next.Apply(Ceiling));

    /// <summary>
    /// The n-th power: <paramref name="ticks"/> applications of this map, equal to eager-stepping
    /// that many times. A rising map settles at the ceiling, a falling one at the floor; ticks ≤ 0
    /// yields <see cref="Identity"/>.
    /// </summary>
    public ClampShift Repeat(long ticks)
    {
        if (ticks <= 0)
        {
            return Identity;
        }

        if (Shift == 0 || Floor == Ceiling)
        {
            return this;
        }

        var rise = Shift > 0 ? Shift : -Shift;
        if (ticks - 1 >= ((long)Ceiling - Floor + rise - 1) / rise)
        {
            var settled = Shift > 0 ? Ceiling : Floor;
            return Canonical(0, settled, settled);
        }

        var travel = (ticks - 1) * Shift;
        return Shift > 0
            ? Canonical(ticks * Shift, (int)(Floor + travel), Ceiling)
            : Canonical(ticks * Shift, Floor, (int)(Ceiling + travel));
    }

    /// <summary>
    /// The earliest tick n in [1, <paramref name="horizon"/>] with
    /// <c>rising.Repeat(n).Apply(value) &gt;= threshold</c>, or <paramref name="horizon"/> + 1 when no
    /// tick in range reaches it. Requires <c>rising.Shift &gt;= 0</c> (the predicate is monotone in n
    /// only for maps that rise or stay constant) and <paramref name="horizon"/> &lt; <see cref="long.MaxValue"/>.
    /// </summary>
    public static long FirstTickAtLeast(ClampShift rising, int value, int threshold, long horizon)
    {
        if (rising.Shift < 0)
        {
            throw new ArgumentException("sv: map does not rise");
        }

        var low = 1L;
        var high = horizon + 1;
        while (low < high)
        {
            var middle = low + ((high - low) >> 1);
            if (rising.Repeat(middle).Apply(value) >= threshold)
            {
                high = middle;
            }
            else
            {
                low = middle + 1;
            }
        }

        return low;
    }

    static ClampShift Canonical(long shift, int floor, int ceiling)
    {
        var low = ClampShifted(int.MinValue, shift, floor, ceiling);
        var high = ClampShifted(int.MaxValue, shift, floor, ceiling);
        return low == high ? new ClampShift(0, low, low) : new ClampShift(shift, low, high);
    }

    static int ClampShifted(int value, long shift, int floor, int ceiling)
    {
        if (shift >= 0)
        {
            if (shift > (long)int.MaxValue - value)
            {
                return ceiling;
            }
        }
        else if (shift < (long)int.MinValue - value)
        {
            return floor;
        }

        var sum = (long)value + shift;
        return sum < floor ? floor : sum > ceiling ? ceiling : (int)sum;
    }
}
