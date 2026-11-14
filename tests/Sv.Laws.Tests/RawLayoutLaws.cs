using System.Reflection;
using System.Runtime.CompilerServices;

namespace Sv.Laws.Tests;

public sealed class RawLayoutLaws
{
    static int SizeOf(Type type) => (int)typeof(Unsafe).GetMethod(nameof(Unsafe.SizeOf))!.MakeGenericMethod(type).Invoke(null, null)!;

    static int FieldBytes(Type type) =>
        type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Sum(field => SizeOf(field.FieldType));

    public static TheoryData<Type> Hashed() => new() { typeof(Effect), typeof(LoggedRecord), typeof(StatOp), typeof(JournalEntry) };

    [Theory]
    [MemberData(nameof(Hashed))]
    public void Structs_hashed_logged_or_journaled_as_raw_bytes_have_no_implicit_padding(Type type) =>
        Assert.Equal(SizeOf(type), FieldBytes(type));
}
