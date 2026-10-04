using System;
using Sv;
using Xunit;

public unsafe class SetMemberPositionTests
{
    static Schema Schema() =>
        SchemaBuilder.Create()
            .Stat("attack", 10f)
            .Stat("armor", 5f)
            .Build();

    static bool Live(Board board, int slot) =>
        (board.LiveWord(slot >> 6) & (1ul << (slot & 63))) != 0;

    [Fact]
    public void BulkMigration_AllSlotsLeaveSharedSet_ResultsMatchOracle()
    {
        const int rows = 512;
        using var board = Board.Create(Schema(), rows);
        var attack = board.Schema.Stat("attack");
        var handles = new ModifierHandle[rows];
        for (var i = 0; i < rows; i++)
        {
            board.Acquire();
            handles[i] = board.AddModifier(i, attack, new Modifier(ModifierKind.Multi, 0.25f));
        }

        board.Recalculate();
        var column = board.OutputPtr(attack);
        for (var i = 0; i < rows; i++)
        {
            Assert.Equal(12.5f, column[i]);
        }

        for (var i = 0; i < rows; i++)
        {
            board.AddModifier(i, attack, new Modifier(ModifierKind.Added, i));
            board.RemoveModifier(i, attack, handles[i]);
        }

        board.Recalculate();
        for (var i = 0; i < rows; i++)
        {
            Assert.Equal(board.Stat(i, attack), column[i]);
            Assert.Equal(10f + i, column[i]);
        }
    }

    [Fact]
    public void Churn_ReleaseReacquireAndMigrate_BroadcastAlwaysMatchesFold()
    {
        const int rows = 256;
        using var board = Board.Create(Schema(), rows);
        var attack = board.Schema.Stat("attack");
        var armor = board.Schema.Stat("armor");
        var rng = 123456789u;
        var handles = new ModifierHandle[rows, 2];
        for (var i = 0; i < rows; i++)
        {
            board.Acquire();
        }

        for (var round = 0; round < 8; round++)
        {
            for (var i = 0; i < rows; i++)
            {
                rng = rng * 1664525u + 1013904223u;
                var choice = rng >> 28;
                if (choice == 4)
                {
                    if (Live(board, i))
                    {
                        board.Release(i);
                    }

                    continue;
                }

                var slot = i;
                if (!Live(board, slot))
                {
                    slot = board.Acquire();
                    handles[slot, 0] = default;
                    handles[slot, 1] = default;
                }

                if (choice == 0 && handles[slot, 0].Id > 0)
                {
                    board.RemoveModifier(slot, attack, handles[slot, 0]);
                    handles[slot, 0] = default;
                }
                else if (choice == 1 && handles[slot, 1].Id > 0)
                {
                    board.RemoveModifier(slot, armor, handles[slot, 1]);
                    handles[slot, 1] = default;
                }
                else if (choice == 2)
                {
                    handles[slot, 0] = board.AddModifier(slot, attack, new Modifier(ModifierKind.Multi, 0.1f * (rng % 5)));
                }
                else if (choice == 3)
                {
                    handles[slot, 1] = board.AddModifier(slot, armor, new Modifier(ModifierKind.Added, rng % 7));
                }
            }

            board.Recalculate();
            var attackColumn = board.OutputPtr(attack);
            var armorColumn = board.OutputPtr(armor);
            for (var i = 0; i < rows; i++)
            {
                if (!Live(board, i))
                {
                    continue;
                }

                Assert.Equal(board.Stat(i, attack), attackColumn[i]);
                Assert.Equal(board.Stat(i, armor), armorColumn[i]);
            }
        }
    }

    [Fact]
    public void InterleavedMigrations_PartialGroupsSwapSetsCleanly()
    {
        const int rows = 128;
        using var board = Board.Create(Schema(), rows);
        var attack = board.Schema.Stat("attack");
        var handles = new ModifierHandle[rows];
        for (var i = 0; i < rows; i++)
        {
            board.Acquire();
            handles[i] = board.AddModifier(i, attack, new Modifier(ModifierKind.Added, 1f));
        }

        board.Recalculate();

        var multiHandles = new ModifierHandle[rows];
        for (var i = 0; i < rows; i += 2)
        {
            multiHandles[i] = board.AddModifier(i, attack, new Modifier(ModifierKind.Multi, 0.5f));
            board.RemoveModifier(i, attack, handles[i]);
        }

        board.Recalculate();
        var column = board.OutputPtr(attack);
        for (var i = 0; i < rows; i++)
        {
            Assert.Equal(board.Stat(i, attack), column[i]);
            Assert.Equal(i % 2 == 0 ? 15f : 11f, column[i]);
        }

        for (var i = 0; i < rows; i += 2)
        {
            handles[i] = board.AddModifier(i, attack, new Modifier(ModifierKind.Added, 2f));
            board.RemoveModifier(i, attack, multiHandles[i]);
        }

        board.Recalculate();
        for (var i = 0; i < rows; i++)
        {
            Assert.Equal(board.Stat(i, attack), column[i]);
            Assert.Equal(i % 2 == 0 ? 12f : 11f, column[i]);
        }
    }
}
