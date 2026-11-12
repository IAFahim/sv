using Unity.Entities;

namespace Sv.Unity
{
    public struct SvSlot : IComponentData
    {
        public int Slot;
    }

    public struct SvSlotCleanup : ICleanupComponentData
    {
        public int Slot;
    }
}
