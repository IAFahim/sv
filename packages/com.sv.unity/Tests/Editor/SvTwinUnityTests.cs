using NUnit.Framework;

namespace Sv.Unity.Tests
{
    public sealed class UnityTwinHost : ISvTwinHost
    {
        readonly SvTestWorld world;
        readonly SvInboxWriter inbox;

        public UnityTwinHost(SvTestWorld world)
        {
            this.world = world;
            world.Host.DigestEvery = 1;
            inbox = world.Host.InboxWriter(0);
        }

        public void AddDelta(int row, int intrinsic, int amount)
        {
            if (intrinsic == 0)
            {
                world.AddHpDelta(row, amount);
            }
            else
            {
                world.AddManaDelta(row, amount);
            }
        }

        public void Send(int row, int amount) => inbox.Send(world.Slots[row], amount);

        public void Enqueue(StatOp op) => world.Host.Enqueue(op);

        public void Frame() => world.Frame();

        public ulong Digest() => world.Host.Digest;
    }

    public class SvTwinUnityTests
    {
        public const ulong NetPinned = 0xDA47AD9B5CD74335ul;

        [Test]
        public void FoldScenarioDigestEqualsTheDotNetPin()
        {
            using var t = new SvTestWorld(SvTwinScenario.Rows, 512);
            Assert.AreEqual(1, t.Host.Links.Intrinsics[0].Column);
            Assert.AreEqual(5, t.Host.Links.Intrinsics[1].Column);
            for (var i = 0; i < SvTwinScenario.Rows; i++)
            {
                Assert.AreEqual(i, t.Slots[i]);
            }

            var digest = SvTwinScenario.Run(new UnityTwinHost(t), t.MaxHp);
            UnityEngine.Debug.Log($"svtwin unity digest {digest:X16}");
            Assert.AreEqual(NetPinned, digest);
        }
    }
}
