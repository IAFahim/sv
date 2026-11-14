using NUnit.Framework;

namespace Hello
{
    public sealed class HelloSvTests
    {
        static readonly string[] Expected =
        {
            "frame 1: hero health 25, knight health 85 shield 0, hero low health True, digest D0391ABCE50C62DB",
            "frame 2: hero health 75, knight health 85 shield 0, hero low health False, digest 9078B3C66F1DD3FE",
        };

        [Test]
        public void The_sample_prints_the_same_numbers_and_digests_as_the_dotnet_host() =>
            Assert.That(HelloSv.Run(), Is.EqualTo(Expected));
    }
}
