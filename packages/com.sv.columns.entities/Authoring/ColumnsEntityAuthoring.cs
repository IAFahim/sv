using System;
using Unity.Entities;
using UnityEngine;

namespace Sv.Columns.Entities
{
    public sealed class ColumnsEntityAuthoring : MonoBehaviour
    {
        public ColumnsWorldAuthoring? World;

        public string Template = string.Empty;

        public LaneOverride[] Lanes = Array.Empty<LaneOverride>();

        [Serializable]
        public struct LaneOverride
        {
            public string Lane;
            public int Value;
        }

        sealed class Baker : Baker<ColumnsEntityAuthoring>
        {
            public override void Bake(ColumnsEntityAuthoring authoring)
            {
                DependsOn(authoring.World);
                var world = authoring.World;
                if (world == null)
                {
                    throw new MissingReferenceException("ColumnsEntityAuthoring on " + authoring.name + " needs its ColumnsWorldAuthoring wired in the Inspector");
                }

                DependsOn(world.Pack);
                if (world.Pack == null)
                {
                    throw new MissingReferenceException("ColumnsWorldAuthoring on " + world.name + " needs a pack: wire a pack JSON TextAsset in the Inspector");
                }

                using var pack = PackJson.Read(world.Pack.text).Compile();
                var entity = GetEntity(TransformUsageFlags.None);
                AddComponent(entity, new ColumnsTemplate { Value = pack.Template(authoring.Template) });
                var lanes = AddBuffer<ColumnsLaneValue>(entity);
                foreach (var lane in authoring.Lanes ?? Array.Empty<LaneOverride>())
                {
                    lanes.Add(new ColumnsLaneValue { Lane = pack.Lane(lane.Lane), Value = lane.Value });
                }
            }
        }
    }
}
