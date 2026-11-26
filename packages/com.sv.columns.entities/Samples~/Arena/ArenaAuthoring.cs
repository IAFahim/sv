using Unity.Entities;
using UnityEngine;

namespace Sv.Columns.Entities.Samples.Arena
{
    public sealed class ArenaAttackAuthoring : MonoBehaviour
    {
        public ColumnsWorldAuthoring? World;

        public GameObject? Target;

        public string Channel = "Fire";

        public int Amount = 5;

        sealed class Baker : Baker<ArenaAttackAuthoring>
        {
            public override void Bake(ArenaAttackAuthoring authoring)
            {
                DependsOn(authoring.World);
                if (authoring.World == null || authoring.World.Pack == null || authoring.Target == null)
                {
                    throw new MissingReferenceException("ArenaAttackAuthoring on " + authoring.name + " needs its World (with a pack) and Target wired in the Inspector");
                }

                DependsOn(authoring.World.Pack);
                using var pack = PackJson.Read(authoring.World.Pack.text).Compile();
                AddComponent(GetEntity(TransformUsageFlags.None), new ArenaAttack
                {
                    Target = GetEntity(authoring.Target, TransformUsageFlags.None),
                    Channel = pack.Channel(authoring.Channel),
                    Amount = authoring.Amount,
                });
            }
        }
    }

    public sealed class ArenaHealthAuthoring : MonoBehaviour
    {
        public ColumnsWorldAuthoring? World;

        public string Lane = "Health";

        sealed class Baker : Baker<ArenaHealthAuthoring>
        {
            public override void Bake(ArenaHealthAuthoring authoring)
            {
                DependsOn(authoring.World);
                if (authoring.World == null || authoring.World.Pack == null)
                {
                    throw new MissingReferenceException("ArenaHealthAuthoring on " + authoring.name + " needs its World (with a pack) wired in the Inspector");
                }

                DependsOn(authoring.World.Pack);
                using var pack = PackJson.Read(authoring.World.Pack.text).Compile();
                AddComponent(GetEntity(TransformUsageFlags.None), new ArenaHealth { Lane = pack.Lane(authoring.Lane) });
            }
        }
    }
}
