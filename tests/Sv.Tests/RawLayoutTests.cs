using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Sv;
using Xunit;

public class RawLayoutTests
{
    static int SizeOf(Type type) => (int)typeof(Unsafe).GetMethod(nameof(Unsafe.SizeOf))!.MakeGenericMethod(type).Invoke(null, null)!;

    static int FieldBytes(Type type) =>
        type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Sum(field => SizeOf(field.FieldType));

    [Theory]
    [InlineData("Sv.StatEntry")]
    [InlineData("Sv.Board+KeyframeRecord")]
    [InlineData("Sv.ModRecord")]
    public void Structs_saved_as_raw_bytes_have_no_implicit_padding(string name)
    {
        var type = typeof(Board).Assembly.GetType(name) ?? typeof(Board).Assembly.GetTypes().Single(t => t.FullName!.EndsWith("+" + name.Split('.').Last(), StringComparison.Ordinal) || t.FullName == name);
        Assert.Equal(SizeOf(type), FieldBytes(type));
    }
}
