using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using UnityEngine;

// Simple per-instance color via MaterialProperty (requires a shader with _Color, _EmissionColor)
[BurstCompile]
public partial struct AgentMaterialController : ISystem
{
    private int _ColorID;
    private int _EmissID;

    public void OnCreate(ref SystemState state)
    {
        _ColorID = Shader.PropertyToID("_BaseColor"); // URP Lit uses _BaseColor
        _EmissID = Shader.PropertyToID("_EmissionColor");
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (rState, matInfo, entity) in SystemAPI.Query<RefRO<AgentRenderState>, RefRW<MaterialMeshInfo>>().WithEntityAccess())
        {
            // Use MaterialPropertyOverride via MaterialProperty component
            var col = Color.Lerp(new Color(0.2f, 0.8f, 1f), new Color(1f, 0.3f, 0.2f), rState.ValueRO.Aggression);
            var emiss = col * (0.1f + 1.2f * math.saturate(rState.ValueRO.Task * 0.5f));

            SystemAPI.SetComponentEnabled<URPMaterialPropertyBaseColor>(entity, true);
            SystemAPI.SetComponentEnabled<URPMaterialPropertyEmissionColor>(entity, true);

            if (!SystemAPI.HasComponent<URPMaterialPropertyBaseColor>(entity))
                state.EntityManager.AddComponentData(entity, new URPMaterialPropertyBaseColor { Value = (Vector4)col });
            else
                state.EntityManager.SetComponentData(entity, new URPMaterialPropertyBaseColor { Value = (Vector4)col });

            if (!SystemAPI.HasComponent<URPMaterialPropertyEmissionColor>(entity))
                state.EntityManager.AddComponentData(entity, new URPMaterialPropertyEmissionColor { Value = (Vector4)emiss });
            else
                state.EntityManager.SetComponentData(entity, new URPMaterialPropertyEmissionColor { Value = (Vector4)emiss });
        }
    }
}
