using Sv;
using Xunit;

public class LinkTableTests
{
    static readonly ColumnDef[] Columns =
    {
        new ColumnDef(0xA1, ColumnRole.StatOutput, 0, 0),
        new ColumnDef(0xA2, ColumnRole.Intrinsic, 0, 0),
        new ColumnDef(0xA3, ColumnRole.Delta, 0, 1),
        new ColumnDef(0xA4, ColumnRole.Inbox, 0, 1),
        new ColumnDef(0xA5, ColumnRole.Effect, 0, 7),
        new ColumnDef(0xA6, ColumnRole.Signal, 0, 8),
        new ColumnDef(0xA7, ColumnRole.Condition, 0, 0),
        new ColumnDef(0xA8, ColumnRole.Sense, 0, 0),
        new ColumnDef(0xA9, ColumnRole.Delta, 1, 2),
    };

    static LinkFault Validate(params Link[] links) => Links.Validate(links, Columns);

    [Fact]
    public void Validate_LegalTable_IsNone()
    {
        Assert.Equal(LinkFault.None, Validate(
            new Link(1, 0, LinkMode.Read),
            new Link(1, 1, LinkMode.Check),
            new Link(1, 6, LinkMode.Check),
            new Link(1, 7, LinkMode.Read, LinkScope.Remote),
            new Link(1, 2, LinkMode.Delta),
            new Link(2, 2, LinkMode.Delta, LinkScope.Remote),
            new Link(2, 3, LinkMode.Delta, LinkScope.Remote),
            new Link(2, 4, LinkMode.Accumulate),
            new Link(3, 0, LinkMode.Modifier, LinkScope.Remote),
            new Link(3, 5, LinkMode.Event),
            new Link(3, 6, LinkMode.Event)));
        Assert.Equal(LinkFault.None, Links.Validate(ReadOnlySpan<Link>.Empty, ReadOnlySpan<ColumnDef>.Empty));
    }

    [Fact]
    public void Validate_EveryFault()
    {
        Assert.Equal(LinkFault.UnknownColumn, Validate(new Link(1, 9, LinkMode.Read)));
        Assert.Equal(LinkFault.UnknownColumn, Validate(new Link(1, 0, (LinkMode)6)));
        Assert.Equal(LinkFault.UnknownColumn, Validate(new Link(1, 0, LinkMode.Read, (LinkScope)2)));
        Assert.Equal(LinkFault.ReadOfAccumulate, Validate(new Link(1, 2, LinkMode.Read)));
        Assert.Equal(LinkFault.ReadOfAccumulate, Validate(new Link(1, 4, LinkMode.Check)));
        Assert.Equal(LinkFault.WriteOfStore, Validate(new Link(1, 1, LinkMode.Delta)));
        Assert.Equal(LinkFault.WriteOfStore, Validate(new Link(1, 0, LinkMode.Accumulate)));
        Assert.Equal(LinkFault.WriteOfStore, Validate(new Link(1, 1, LinkMode.Modifier)));
        Assert.Equal(LinkFault.WriteOfStore, Validate(new Link(1, 7, LinkMode.Event)));
        Assert.Equal(LinkFault.RemoteFloat, Validate(new Link(1, 4, LinkMode.Accumulate, LinkScope.Remote)));
        Assert.Equal(LinkFault.RemoteFloat, Validate(new Link(1, 5, LinkMode.Event, LinkScope.Remote)));
        Assert.Equal(LinkFault.RemoteWithoutInbox, Validate(new Link(1, 8, LinkMode.Delta, LinkScope.Remote)));
        var duplicate = new[] { new ColumnDef(0xB1, ColumnRole.Effect, 0, 1), new ColumnDef(0xB1, ColumnRole.Effect, 0, 2) };
        Assert.Equal(LinkFault.SecondConsumer, Links.Validate(ReadOnlySpan<Link>.Empty, duplicate));
        var twoInboxes = new[] { new ColumnDef(0xB1, ColumnRole.Inbox, 0, 1), new ColumnDef(0xB2, ColumnRole.Inbox, 0, 1) };
        Assert.Equal(LinkFault.SecondConsumer, Links.Validate(ReadOnlySpan<Link>.Empty, twoInboxes));
        var storeTwice = new[] { new ColumnDef(0xB1, ColumnRole.StatOutput, 0, 0), new ColumnDef(0xB1, ColumnRole.StatOutput, 0, 0) };
        Assert.Equal(LinkFault.None, Links.Validate(ReadOnlySpan<Link>.Empty, storeTwice));
        Assert.Equal(LinkFault.ReadOfAccumulate, Validate(new Link(1, 0, LinkMode.Read), new Link(1, 3, LinkMode.Read), new Link(1, 9, LinkMode.Read)));
    }

    [Fact]
    public void Hash_IsStable_AndSeesEveryField()
    {
        var links = new[] { new Link(1, 0, LinkMode.Read), new Link(2, 2, LinkMode.Delta, LinkScope.Remote) };
        var hash = Links.Hash(links, Columns);
        Assert.Equal(hash, Links.Hash(links.ToArray(), Columns.ToArray()));
        var variants = new ulong[]
        {
            Links.Hash(new[] { new Link(1, 0, LinkMode.Read), new Link(2, 2, LinkMode.Delta, LinkScope.Self) }, Columns),
            Links.Hash(new[] { new Link(1, 0, LinkMode.Check), new Link(2, 2, LinkMode.Delta, LinkScope.Remote) }, Columns),
            Links.Hash(new[] { new Link(3, 0, LinkMode.Read), new Link(2, 2, LinkMode.Delta, LinkScope.Remote) }, Columns),
            Links.Hash(new[] { new Link(1, 1, LinkMode.Read), new Link(2, 2, LinkMode.Delta, LinkScope.Remote) }, Columns),
            Links.Hash(links, Columns.Take(8).ToArray()),
            Links.Hash(links, Columns.Select((c, i) => i == 2 ? new ColumnDef(c.Name, c.Role, c.Schema, 5) : c).ToArray()),
            Links.Hash(links.Take(1).ToArray(), Columns),
        };
        Assert.DoesNotContain(hash, variants);
        Assert.Equal(variants.Length, variants.Distinct().Count());
    }
}
