using Sv;
using Xunit;

public class UnmanagedShapeTests
{
    static void RequireUnmanaged<T>() where T : unmanaged
    {
    }

    [Fact]
    public void Clamp_SatisfiesTheUnmanagedConstraint()
    {
        RequireUnmanaged<Clamp>();
        RequireUnmanaged<ClampShift>();
    }
}
