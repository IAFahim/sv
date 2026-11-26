using Unity.Entities;
using UnityEngine;

namespace Sv.Unity.Authoring
{
    [DisallowMultipleComponent]
    public sealed class SvSlotAuthoring : MonoBehaviour
    {
        [Tooltip("The sv slot this entity stands for. Outbox cells on it send from this slot, and sv events on this slot start its timelines.")]
        [Min(0)]
        public int Slot;

        sealed class Baker : Baker<SvSlotAuthoring>
        {
            public override void Bake(SvSlotAuthoring authoring) => AddComponent(GetEntity(TransformUsageFlags.None), new SvSlot { Value = authoring.Slot });
        }
    }
}
