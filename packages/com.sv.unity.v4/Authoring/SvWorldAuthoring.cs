using Unity.Entities;
using UnityEngine;

namespace Sv.Unity.Authoring
{
    [DisallowMultipleComponent]
    public sealed class SvWorldAuthoring : MonoBehaviour
    {
        [Tooltip("Fixed Step steps sv once per fixed tick, after tl, in the fixed step group. Render Frame steps it once per rendered frame.")]
        public SvClock Clock = SvClock.FixedStep;

        [Min(1)]
        public int Capacity = 65536;

        [Min(1)]
        public int Workers = 1;

        [Min(1)]
        public int Parts = 64;

        sealed class Baker : Baker<SvWorldAuthoring>
        {
            public override void Bake(SvWorldAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.None);
                AddComponent(entity, new SvWorldConfig { Capacity = authoring.Capacity, Workers = authoring.Workers, Parts = authoring.Parts });
                if (authoring.Clock == SvClock.FixedStep)
                {
                    AddComponent<SvFixedStep>(entity);
                }
            }
        }
    }
}
