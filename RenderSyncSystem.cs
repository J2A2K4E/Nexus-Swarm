using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[BurstCompile]
public partial struct RenderSyncSystem : ISystem
{
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (td, l2w, post) in SystemAPI.Query<RefRO<TransformData>, RefRW<LocalToWorld>, RefRO<PostTransformMatrix>>().WithAll<AgentTag>())
        {
            float4x4 m = float4x4.TRS(td.ValueRO.Position, td.ValueRO.Rotation, new float3(1,1,1));
            l2w.ValueRW.Value = math.mul(m, post.ValueRO.Value);
        }
    }
}
