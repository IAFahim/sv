using Unity.Entities;

namespace Sv.Unity
{
    /// <summary>
    /// Chunk component written by <see cref="UnityPopulation.Reconcile"/>: the chunk's SvSlot rows
    /// are its row prefix [0, <see cref="Count"/>) and map to board slots
    /// [<see cref="Start"/>, <see cref="Start"/> + <see cref="Count"/>) — contiguity is reconcile's
    /// contract, so <see cref="SvChunk.RunIntrinsics{TColumn}"/> resolves the chunk's slot range in
    /// O(1) instead of scanning slots on every visit. Count 0 marks a reconciled chunk that binds
    /// nothing; a chunk with no SvRange has not been reconciled since its last structural change.
    /// </summary>
    public struct SvRange : IComponentData
    {
        public int Start;

        public int Count;
    }
}
