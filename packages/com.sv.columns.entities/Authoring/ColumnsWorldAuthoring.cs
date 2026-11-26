using Unity.Entities;
using UnityEngine;

namespace Sv.Columns.Entities
{
    [DisallowMultipleComponent]
    public sealed class ColumnsWorldAuthoring : MonoBehaviour
    {
        public TextAsset? Pack;

        [Min(64)]
        public int Capacity = 4096;

        [Min(1)]
        public int Writers = 1;

        [Min(0)]
        public int ResolveParts;

        public FrameKernel Kernel = FrameKernel.Rows;

        sealed class Baker : Baker<ColumnsWorldAuthoring>
        {
            public override void Bake(ColumnsWorldAuthoring authoring)
            {
                DependsOn(authoring.Pack);
                if (authoring.Pack == null)
                {
                    throw new MissingReferenceException("ColumnsWorldAuthoring on " + authoring.name + " needs a pack: wire a pack JSON TextAsset in the Inspector");
                }

                var json = authoring.Pack.text;
                using (PackJson.Read(json).Compile())
                {
                }

                var blob = ColumnsPacks.Create(json);
                AddBlobAsset(ref blob, out _);
                AddComponent(GetEntity(TransformUsageFlags.None), new ColumnsWorldConfig
                {
                    Pack = blob,
                    Capacity = authoring.Capacity,
                    Writers = authoring.Writers,
                    ResolveParts = authoring.ResolveParts,
                    Kernel = authoring.Kernel,
                });
            }
        }
    }
}
