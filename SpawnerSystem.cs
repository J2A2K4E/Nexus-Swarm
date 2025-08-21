using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.Rendering;

[BurstCompile]
public partial struct SpawnerSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<SpawnRequest>();
        state.RequireForUpdate<WorldBounds>();
    }

    public void OnUpdate(ref SystemState state)
    {
        var ecb = new EntityCommandBuffer(Allocator.Temp);
        var bounds = SystemAPI.GetSingleton<WorldBounds>();
        var request = SystemAPI.GetSingleton<SpawnRequest>();

        // Find render refs
        AgentRenderRefs renderRefs = null;
        foreach (var (refs, entity) in SystemAPI.Query<AgentRenderRefs>().WithEntityAccess())
        {
            renderRefs = refs;
            break;
        }

        var rnd = new Unity.Mathematics.Random(12345);

        for (int i = 0; i < request.Count; i++)
        {
            var e = ecb.CreateEntity();
            ecb.AddComponent<AgentTag>(e);
            ecb.AddComponent(e, new Agent
            {
                Velocity = rnd.NextFloat3Direction() * rnd.NextFloat(0.5f, 2f),
                State = 1,
                GroupId = (short)((i % 2 == 0) ? 0 : 1),
                Energy = 1f,
                Carry = 0f
            });
            var pos = bounds.Center + rnd.NextFloat3(-bounds.Extents, bounds.Extents);
            var rot = quaternion.LookRotationSafe(math.normalize(rnd.NextFloat3Direction()), math.up());
            ecb.AddComponent(e, new TransformData { Position = pos, Rotation = rot });
            ecb.AddComponent(e, new GridIndex { Index = -1 });
            ecb.AddComponent(e, new AgentRenderState { Aggression = 0.2f, Task = 0f });

            // Hybrid Entities.Graphics render components
            if (renderRefs != null && renderRefs.Mesh != null && renderRefs.Material != null)
            {
                ecb.AddComponent(e, new LocalToWorld());
                ecb.AddComponent(e, new WorldRenderBounds());
                ecb.AddSharedComponentManaged(e, new RenderMeshArray(new[] { renderRefs.Material }, new[] { renderRefs.Mesh }));
                ecb.AddComponent(e, new MaterialMeshInfo
                {
                    Material = 0,
                    MeshID = 0
                });
                ecb.AddComponent(e, new RenderBounds { Value = renderRefs.Mesh.bounds.ToAABB() });
                ecb.AddComponent(e, new RenderMeshDescription
                {
                    FilterSettings = RenderFilterSettings.Default,
                    LightProbeUsage = LightProbeUsage.Off,
                    ShadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off,
                    ReceiveShadows = false,
                    MotionVectorMode = MotionVectorGenerationMode.Camera
                });
                // Scale via PostTransformMatrix
                var s = renderRefs.Scale;
                ecb.AddComponent(e, new PostTransformMatrix
                {
                    Value = float4x4.Scale(new float3(s, s, s))
                });
            }
        }

        // Remove spawn request
        foreach (var (sr, entity) in SystemAPI.Query<RefRO<SpawnRequest>>().WithEntityAccess())
            ecb.DestroyEntity(entity);

        ecb.Playback(state.EntityManager);
        ecb.Dispose();
    }
}
