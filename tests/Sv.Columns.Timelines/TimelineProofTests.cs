using Sv.Pack;
using Xunit;

namespace Sv.Columns.Timelines
{
    public sealed class TimelineProofTests
    {
        const int Frames = 20;
        const int BurnStart = 1;
        const int MendStart = 12;
        const int LowHealthEntered = 4;

        sealed record Run(int Target, int Caster, List<ulong> Digests, List<Cell> Cells, int Health, int PoisonTicks, long Started, long Stopped);

        static List<Cell> Design(int target, int caster)
        {
            var cells = new List<Cell>();
            for (var frame = BurnStart; frame < BurnStart + 4; frame++)
            {
                cells.Add(new Cell(frame, caster, Rig.Fire, TimelineCells.BurnAmount));
            }

            cells.Add(new Cell(LowHealthEntered + 1, target, Rig.Card, TimelineCells.PoisonLevel));
            for (var frame = MendStart; frame < MendStart + 3; frame++)
            {
                cells.Add(new Cell(frame, target, Rig.Healing, TimelineCells.MendAmount));
            }

            return cells;
        }

        static Run Play(Func<CompiledPack, ICellSource> source)
        {
            using var rig = new Rig(source);
            var target = rig.Spawn("dummy");
            var caster = rig.Spawn("sniper");
            var timelines = rig.Source as TimelineCells;
            var digests = new List<ulong>();
            for (var frame = 0; frame < Frames; frame++)
            {
                if (frame == 0)
                {
                    rig.Set(caster, Rig.Aim, target + 1);
                }

                if (frame == BurnStart)
                {
                    timelines?.Start(caster, TimelineCells.Burn);
                }

                if (frame == MendStart)
                {
                    timelines?.Start(target, TimelineCells.Mend);
                }

                digests.Add(rig.Step());
            }

            return new Run(target, caster, digests, rig.Cells, rig.Get(target, "Health"), rig.Get(target, "PoisonTicks"), timelines?.Requests.Started ?? 0, timelines?.Requests.Stopped ?? 0);
        }

        static Run ThroughTl(int workers) => Play(pack => new TimelineCells(pack, workers));

        static Run ByHand(int target, int caster) => Play(pack => new HandCells(pack, Design(target, caster)));

        [Fact]
        public void The_committed_glue_equals_the_emitter_output()
        {
            var committed = File.ReadAllText(Path.Combine(Rig.Root, "tests", "Sv.Columns.Timelines", "Glue.g.cs")).Replace("\r\n", "\n");
            Assert.Equal(committed, ColumnsGlue.Emit(PackJson.Read(Rig.PackText()), "Sv.Columns.Timelines", false));
        }

        [Fact]
        public void The_generated_tracks_write_the_designed_cells_and_the_low_health_event_starts_the_poison_card()
        {
            var run = ThroughTl(1);
            Assert.Equal(Design(run.Target, run.Caster), run.Cells);
            Assert.Equal(1, run.Started);
            Assert.Equal(1, run.Stopped);
        }

        [Fact]
        public void The_burn_poison_and_mend_timelines_move_health_as_the_pack_rules_say()
        {
            var run = ThroughTl(1);
            var health = 100 - 4 * TimelineCells.BurnAmount - TimelineCells.PoisonLevel * 4 + 3 * TimelineCells.MendAmount;
            Assert.Equal(health, run.Health);
            Assert.Equal(0, run.PoisonTicks);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(4)]
        public void Every_frame_digest_through_tl_equals_the_same_cells_written_by_hand(int workers)
        {
            var tl = ThroughTl(workers);
            var hand = ByHand(tl.Target, tl.Caster);
            Assert.Equal(hand.Cells, tl.Cells);
            Assert.Equal(hand.Digests, tl.Digests);
            Assert.Equal(hand.Health, tl.Health);
        }
    }
}
