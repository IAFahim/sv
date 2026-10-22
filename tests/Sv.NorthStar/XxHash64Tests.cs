using System.Text;
using Sv;
using Sv.Compose;
using Xunit;

namespace Sv.NorthStar;

public sealed unsafe class XxHash64Tests
{
    [Theory]
    [InlineData("", 0xEF46DB3751D8E999ul)]
    [InlineData("a", 0xD24EC4F1A98C6E5Bul)]
    [InlineData("abc", 0x44BC2CF5AD770999ul)]
    [InlineData("Nobody inspects the spammish repetition", 0xFBCEA83C8A378BF1ul)]
    public void KnownVectors(string text, ulong expected)
    {
        var bytes = Encoding.ASCII.GetBytes(text);
        fixed (byte* p = bytes)
        {
            Assert.Equal(expected, XxHash64.Hash(p, bytes.Length));
        }
    }

    [Fact]
    public void MatchesReferenceImplementation_EveryLengthTo300()
    {
        var random = new Random(31337);
        var bytes = new byte[300];
        random.NextBytes(bytes);
        fixed (byte* p = bytes)
        {
            for (var length = 0; length <= bytes.Length; length++)
            {
                var reference = System.IO.Hashing.XxHash64.HashToUInt64(bytes.AsSpan(0, length));
                Assert.True(reference == XxHash64.Hash(p, length), $"length {length}");
            }
        }
    }
}
