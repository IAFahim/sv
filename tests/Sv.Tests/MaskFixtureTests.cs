using System.Text.Json;
using Sv;
using Xunit;

public unsafe class MaskFixtureTests
{
    [Fact]
    public void EnabledChunk_ConformsToBusMaskFixtures()
    {
        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "mask-fixtures.json"));
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(128, doc.RootElement.GetProperty("chunkCapacity").GetInt32());

        using var board = Board.Create(SchemaBuilder.Create().Intrinsic("mana").Build(), 128);
        var value = stackalloc int[128];
        var applied = stackalloc int[128];
        var clamp = Clamp.Constant(int.MinValue, int.MaxValue);

        foreach (var pattern in doc.RootElement.GetProperty("patterns").EnumerateArray())
        {
            var name = pattern.GetProperty("name").GetString();
            var low = Convert.ToUInt64(pattern.GetProperty("low").GetString(), 16);
            var high = Convert.ToUInt64(pattern.GetProperty("high").GetString(), 16);

            var expected = new bool[128];
            foreach (var run in pattern.GetProperty("enabledRuns").EnumerateArray())
            {
                var start = run[0].GetInt32();
                var len = run[1].GetInt32();
                for (var i = start; i < start + len; i++)
                {
                    expected[i] = true;
                }
            }

            for (var i = 0; i < 128; i++)
            {
                value[i] = 0;
                applied[i] = -1;
            }

            board.RunEnabledChunk(value, 1, clamp, low, 0, applied);
            board.RunEnabledChunk(value, 1, clamp, high, 64, applied);

            for (var i = 0; i < 128; i++)
            {
                if (expected[i])
                {
                    Assert.True(value[i] == 1 && applied[i] == 1, $"pattern '{name}' slot {i}: expected enabled");
                }
                else
                {
                    Assert.True(value[i] == 0 && applied[i] == 0, $"pattern '{name}' slot {i}: expected disabled/untouched");
                }
            }
        }
    }
}
