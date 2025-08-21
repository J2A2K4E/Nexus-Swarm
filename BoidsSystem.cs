using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[BurstCompile]
public partial struct BoidsSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<BoidsParams>();
        state.RequireForUpdate<GridConfig>();
        state.RequireForUpdate<WorldBounds>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var dt = SystemAPI.Time.DeltaTime;
        var boids = SystemAPI.GetSingleton<BoidsParams>();
        var grid = SystemAPI.GetSingleton<GridConfig>();
        var bounds = SystemAPI.GetSingleton<WorldBounds>();

        // Collect agent data into SoA arrays for cache-friendly compute
        var agentsQuery = SystemAPI.QueryBuilder().WithAll<AgentTag, Agent, TransformData, GridIndex, AgentRenderState>().Build();
        int N = agentsQuery.CalculateEntityCount();
        if (N == 0) return;

        var Positions = new NativeArray<float3>(N, Allocator.TempJob);
        var Velocities = new NativeArray<float3>(N, Allocator.TempJob);
        var States = new NativeArray<byte>(N, Allocator.TempJob);
        var Groups = new NativeArray<short>(N, Allocator.TempJob);
        var Cells = new NativeArray<int>(N, Allocator.TempJob);
        var Entities = new NativeArray<Entity>(N, Allocator.TempJob);

        int i = 0;
        foreach (var (a, td, gi, entity) in SystemAPI.Query<RefRO<Agent>, RefRO<TransformData>, RefRO<GridIndex>>().WithAll<AgentTag>().WithEntityAccess())
        {
            Positions[i] = td.ValueRO.Position;
            Velocities[i] = a.ValueRO.Velocity;
            States[i] = a.ValueRO.State;
            Groups[i] = a.ValueRO.GroupId;
            Cells[i] = gi.ValueRO.Index;
            Entities[i] = entity;
            i++;
        }

        // Build a lightweight cell → [start,end) map from GridBuildSystem if available:
        // For simplicity in this vertical slice, do a naive neighbor check within 27 neighboring cells by recomputing indices.
        // We’ll scan the world to find neighbors in a bounded radius using the grid math.

        var newVel = new NativeArray<float3>(N, Allocator.TempJob);
        var newPos = new NativeArray<float3>(N, Allocator.TempJob);
        var newRot = new NativeArray<quaternion>(N, Allocator.TempJob);
        var newAgg = new NativeArray<float>(N, Allocator.TempJob);
        var newTask = new NativeArray<float>(N, Allocator.TempJob);

        // Copy goals into NativeList
        var goals = new NativeList<GroupGoal>(Allocator.TempJob);
        foreach (var g in SystemAPI.Query<RefRO<GroupGoal>>())
            goals.Add(g.ValueRO);

        var randSeed = 1337u;

        // Main job (single-threaded loop in example; you can convert to IJobParallelFor with a neighbor fetch helper)
        for (int idx = 0; idx < N; idx++)
        {
            var p = Positions[idx];
            var v = Velocities[idx];
            var gid = Groups[idx];

            float3 sep = 0, ali = 0, coh = 0;
            int nAli = 0, nCoh = 0, nSep = 0;

            float rSep2 = boids.SeparationRadius * boids.SeparationRadius;
            float rAli2 = boids.AlignmentRadius * boids.AlignmentRadius;
            float rCoh2 = boids.CohesionRadius * boids.CohesionRadius;

            int neighborsSeen = 0;
            // Iterate all agents (O(N)) for simplicity; replace with cell bins for 50k+. Good for 10–20k on desktop with Burst disabled; with Burst, still fine for demo.
            for (int j = 0; j < N; j++)
            {
                if (j == idx) continue;
                var pj = Positions[j];
                float3 d = pj - p;
                float d2 = math.lengthsq(d);
                if (d2 > rCoh2) continue;

                if (d2 < rSep2) { sep -= d / math.max(1e-3f, d2); nSep++; }
                if (d2 < rAli2) { ali += math.normalizesafe(Velocities[j]); nAli++; }
                coh += pj; nCoh++;

                neighborsSeen++;
                if (neighborsSeen >= (int)boids.NeighborCap) break;
            }

            float3 S = nSep > 0 ? math.normalizesafe(sep / nSep) : float3.zero;
            float3 A = nAli > 0 ? math.normalizesafe(ali / nAli) : float3.zero;
            float3 C = nCoh > 0 ? math.normalizesafe((coh / nCoh) - p) : float3.zero;

            // Goal influence
            float3 G = float3.zero;
            float task = 0f;
            for (int g = 0; g < goals.Length; g++)
            {
                var goal = goals[g];
                if (goal.GroupId != gid) continue;
                if (goal.Type == GoalType.Harvest)
                {
                    var to = goal.TargetPos - p;
                    G += math.normalizesafe(to) * goal.Intensity;
                    task = math.max(task, 1f);
                }
                else if (goal.Type == GoalType.Perimeter)
                {
                    var toC = p - goal.TargetPos;
                    var radial = math.normalizesafe(toC);
                    var ringPoint = goal.TargetPos + radial * math.max(1f, goal.Radius);
                    var toRing = ringPoint - p;
                    G += math.normalizesafe(toRing) * goal.Intensity;
                    task = math.max(task, 2f);
                }
            }

            // Bounds avoidance/wrap
            float3 O = float3.zero;
            var min = bounds.Center - bounds.Extents;
            var max = bounds.Center + bounds.Extents;
            const float edge = 6f;
            if (bounds.Wrap == 1)
            {
                // Position wrap; avoidance small to keep inside visually
                float3 pp = p;
                if (p.x < min.x) pp.x = max.x;
                if (p.x > max.x) pp.x = min.x;
                if (p.y < min.y) pp.y = max.y;
                if (p.y > max.y) pp.y = min.y;
                if (p.z < min.z) pp.z = max.z;
                if (p.z > max.z) pp.z = min.z;
                p = pp;
            }
            else
            {
                if (p.x - min.x < edge) O.x += 1f;
                if (max.x - p.x < edge) O.x -= 1f;
                if (p.y - min.y < edge) O.y += 1f;
                if (max.y - p.y < edge) O.y -= 1f;
                if (p.z - min.z < edge) O.z += 1f;
                if (max.z - p.z < edge) O.z -= 1f;
            }

            // Random jitter
            randSeed = 1664525u * randSeed + 1013904223u;
            float3 J = new float3(
                ((randSeed >> 16) & 0x7FFF) / 32768f - 0.5f,
                ((randSeed >> 1) & 0x7FFF) / 32768f - 0.5f,
                ((randSeed >> 5) & 0x7FFF) / 32768f - 0.5f
            );
            J = math.normalizesafe(J) * boids.Jitter;

            float3 desired = math.normalizesafe(
                S * boids.SeparationWeight +
                A * boids.AlignmentWeight +
                C * boids.CohesionWeight +
                G * boids.GoalWeight +
                O * boids.AvoidWeight +
                J
            ) * boids.MaxSpeed;

            float3 accel = math.clamp(desired - v, -boids.MaxAccel, boids.MaxAccel);
            v = v + accel * dt;
            float speed = math.length(v);
            if (speed > boids.MaxSpeed) v = (v / speed) * boids.MaxSpeed;

            var newP = p + v * dt;
            if (bounds.Wrap == 1)
            {
                var minB = bounds.Center - bounds.Extents;
                var maxB = bounds.Center + bounds.Extents;
                if (newP.x < minB.x) newP.x = maxB.x;
                if (newP.x > maxB.x) newP.x = minB.x;
                if (newP.y < minB.y) newP.y = maxB.y;
                if (newP.y > maxB.y) newP.y = minB.y;
                if (newP.z < minB.z) newP.z = maxB.z;
                if (newP.z > maxB.z) newP.z = minB.z;
            }
            else
            {
                newP = math.clamp(newP, min, max);
            }

            newVel[idx] = v;
            newPos[idx] = newP;
            newRot[idx] = quaternion.LookRotationSafe(math.select(new float3(0,0,1), math.normalizesafe(v), speed > 1e-3f), math.up());
            newAgg[idx] = math.saturate(math.length(v) / boids.MaxSpeed);
            newTask[idx] = task;
        }

        // Write back
        for (int k = 0; k < N; k++)
        {
            var e = Entities[k];
            SystemAPI.SetComponent(e, new Agent { Velocity = newVel[k], State = States[k], GroupId = Groups[k], Energy = 1f, Carry = 0f });
            SystemAPI.SetComponent(e, new TransformData { Position = newPos[k], Rotation = newRot[k] });
            SystemAPI.SetComponent(e, new AgentRenderState { Aggression = newAgg[k], Task = newTask[k] });
        }

        Positions.Dispose(); Velocities.Dispose(); States.Dispose(); Groups.Dispose(); Cells.Dispose(); Entities.Dispose();
        newVel.Dispose(); newPos.Dispose(); newRot.Dispose(); newAgg.Dispose(); newTask.Dispose();
        goals.Dispose();
    }
}
