using Xunit;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class HeavyAllocationCollection
{
    public const string Name = "heavy-allocation";
}
