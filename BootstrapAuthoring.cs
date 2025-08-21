using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public class BootstrapAuthoring : MonoBehaviour
{
    [Header("Counts")]
    public int AgentCount = 20000;

    [Header("Bounds")]
    public Vector3 Center = Vector3.zero;
    public Vector3 Extents = new Vector3(150, 50, 150);
    public bool Wrap = true;

    [Header("Grid")]
    public float CellSize = 4f;

    [Header("Boids")]
    public float SeparationWeight = 1.5f;
    public float AlignmentWeight = 1.0f;
    public float CohesionWeight = 0.8f;
    public float GoalWeight = 1.2f;
    public float AvoidWeight = 2.0f;

    public float SeparationRadius = 2.5f;
    public float AlignmentRadius = 6f;
    public float CohesionRadius = 10f;

    public float MaxSpeed = 8f;
    public float MaxAccel = 20f;
    public int NeighborCap = 12;
    public float Jitter = 0.25f;

    [Header("Visuals")]
    public Mesh AgentMesh;
    public Material AgentMaterial;
    public float AgentScale = 0.4f;

    [Header("Goal Defaults")]
    public Vector3 HarvestPos = new Vector3(0, 0, 0);
    public Vector3 PerimeterPos = new Vector3(0, 0, 0);
    public float PerimeterRadius = 40f;

    private void OnValidate()
    {
        NeighborCap = Mathf.Clamp(NeighborCap, 4, 32);
        SeparationRadius = Mathf.Min(SeparationRadius, AlignmentRadius);
        AlignmentRadius = Mathf.Min(AlignmentRadius, CohesionRadius);
    }

    private class Baker : Baker<BootstrapAuthoring>
    {
        public override void Bake(BootstrapAuthoring a)
        {
            var entity = GetEntity(TransformUsageFlags.None);

            AddComponent(entity, new WorldBounds
            {
                Center = a.Center,
                Extents = a.Extents,
                Wrap = (byte)(a.Wrap ? 1 : 0)
            });

            var gridSize = new int3(
                math.max(1, (int)math.ceil((a.Extents.x * 2f) / a.CellSize)),
                math.max(1, (int)math.ceil((a.Extents.y * 2f) / a.CellSize)),
                math.max(1, (int)math.ceil((a.Extents.z * 2f) / a.CellSize))
            );
            var origin = (float3)a.Center - (float3)a.Extents;

            AddComponent(entity, new GridConfig
            {
                CellSize = a.CellSize,
                GridSize = gridSize,
                Origin = origin
            });

            AddComponent(entity, new BoidsParams
            {
                SeparationWeight = a.SeparationWeight,
                AlignmentWeight = a.AlignmentWeight,
                CohesionWeight = a.CohesionWeight,
                GoalWeight = a.GoalWeight,
                AvoidWeight = a.AvoidWeight,
                MaxSpeed = a.MaxSpeed,
                MaxAccel = a.MaxAccel,
                SeparationRadius = a.SeparationRadius,
                AlignmentRadius = a.AlignmentRadius,
                CohesionRadius = a.CohesionRadius,
                NeighborCap = a.NeighborCap,
                Jitter = a.Jitter
            });

            AddComponent(entity, new GlobalRng { State = 1u });

            // Initial default goals for Group 0
            var g0 = CreateAdditionalEntity(TransformUsageFlags.None);
            AddComponent(g0, new GroupGoal
            {
                Type = GoalType.Harvest,
                TargetPos = a.HarvestPos,
                Radius = 0,
                Intensity = 1,
                GroupId = 0
            });
            var g1 = CreateAdditionalEntity(TransformUsageFlags.None);
            AddComponent(g1, new GroupGoal
            {
                Type = GoalType.Perimeter,
                TargetPos = a.PerimeterPos,
                Radius = a.PerimeterRadius,
                Intensity = 0.8f,
                GroupId = 1
            });

            // Store rendering references in a blob via an authoring singleton
            var renderEntity = CreateAdditionalEntity(TransformUsageFlags.Renderable);
            AddComponentObject(renderEntity, new AgentRenderRefs
            {
                Mesh = a.AgentMesh,
                Material = a.AgentMaterial,
                Scale = a.AgentScale
            });

            // Spawn request
            var spawner = CreateAdditionalEntity(TransformUsageFlags.None);
            AddComponent(spawner, new SpawnRequest { Count = a.AgentCount });
        }
    }
}

// Mono-only holder for asset refs (Entities.Graphics requires conversion at runtime)
public class AgentRenderRefs : IComponentData, IEnableableComponent
{
    public Mesh Mesh;
    public Material Material;
    public float Scale;
}

public struct SpawnRequest : IComponentData
{
    public int Count;
}
