using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

[BurstCompile]
public partial struct GridBuildSystem : ISystem
{
    private NativeArray<int> _cellCounts;
    private NativeArray<int> _cellOffsets;
    private NativeArray<int> _agentIndices;
    private int _totalCells;

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GridConfig>();
        state.RequireForUpdate<WorldBounds>();
    }

    public void OnDestroy(ref SystemState state)
    {
        if (_cellCounts.IsCreated) _cellCounts.Dispose();
        if (_cellOffsets.IsCreated) _cellOffsets.Dispose();
        if (_agentIndices.IsCreated) _agentIndices.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var grid = SystemAPI.GetSingleton<GridConfig>();
        var qAgents = SystemAPI.QueryBuilder().WithAll<AgentTag, TransformData>().Build();
        int agentCount = qAgents.CalculateEntityCount();

        var neededCells = grid.GridSize.x * grid.GridSize.y * grid.GridSize.z;
        if (_totalCells != neededCells)
        {
            _totalCells = neededCells;
            if (_cellCounts.IsCreated) _cellCounts.Dispose();
            if (_cellOffsets.IsCreated) _cellOffsets.Dispose();
            _cellCounts = new NativeArray<int>(_totalCells, Allocator.Persistent, NativeArrayOptions.ClearMemory);
            _cellOffsets = new NativeArray<int>(_totalCells, Allocator.Persistent, NativeArrayOptions.ClearMemory);
        }

        if (!_agentIndices.IsCreated || _agentIndices.Length != math.max(1, agentCount))
        {
            if (_agentIndices.IsCreated) _agentIndices.Dispose();
            _agentIndices = new NativeArray<int>(math.max(1, agentCount), Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        }

        // Pass 1: Zero counts
        var counts = _cellCounts;
        new MemsetJob { Array = counts }.Schedule(_totalCells, 128).Complete();

        // Gather positions into temp arrays for cache-friendly jobs
        var positions = new NativeArray<float3>(agentCount, Allocator.TempJob);
        var indices = new NativeArray<int>(agentCount, Allocator.TempJob);
        var entities = new NativeArray<Entity>(agentCount, Allocator.TempJob);

        int i = 0;
        foreach (var (td, entity) in SystemAPI.Query<RefRO<TransformData>>().WithAll<AgentTag>().WithEntityAccess())
        {
            positions[i] = td.ValueRO.Position;
            entities[i] = entity;
            i++;
        }

        // Pass 2: Count agents per cell
        var jobCount = new CountCellsJob
        {
            Positions = positions,
            Origin = grid.Origin,
            CellSize = grid.CellSize,
            GridSize = grid.GridSize,
            Counts = counts
        }.Schedule(agentCount, 256);
        jobCount.Complete();

        // Prefix sum (exclusive) for offsets
        var offsets = _cellOffsets;
        var prefix = new PrefixSumJob { Counts = counts, Offsets = offsets }.Schedule();
        prefix.Complete();

        // Fill agentIndices with compacted lists
        var writeIndices = _agentIndices;
        var fill = new FillCellsJob
        {
            Positions = positions,
            Origin = grid.Origin,
            CellSize = grid.CellSize,
            GridSize = grid.GridSize,
            Offsets = offsets,
            Counts = counts,
            WriteIndices = writeIndices
        }.Schedule(agentCount, 256);
        fill.Complete();

        // Write GridIndex to agents based on their cell
        i = 0;
        foreach (var (td, gi, entity) in SystemAPI.Query<RefRO<TransformData>, RefRW<GridIndex>>().WithAll<AgentTag>().WithEntityAccess())
        {
            var cell = GridIndexOf(td.ValueRO.Position, grid);
            SystemAPI.SetComponent(entity, new GridIndex { Index = cell });
            indices[i] = i; // local index for neighbor scans
            i++;
        }

        positions.Dispose();
        indices.Dispose();
        entities.Dispose();
    }

    [BurstCompile]
    private static int GridIndexOf(float3 pos, GridConfig grid)
    {
        var rel = pos - grid.Origin;
        int3 c = (int3)math.floor(rel / grid.CellSize);
        c = math.clamp(c, new int3(0,0,0), grid.GridSize - new int3(1,1,1));
        return c.x + grid.GridSize.x * (c.y + grid.GridSize.y * c.z);
    }

    [BurstCompile]
    private struct MemsetJob : IJobParallelFor
    {
        public NativeArray<int> Array;
        public void Execute(int index) { Array[index] = 0; }
    }

    [BurstCompile]
    private struct CountCellsJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<float3> Positions;
        public float3 Origin;
        public float CellSize;
        public int3 GridSize;
        public NativeArray<int> Counts;

        public void Execute(int index)
        {
            var p = Positions[index];
            var rel = p - Origin;
            int3 c = (int3)math.floor(rel / CellSize);
            c = math.clamp(c, new int3(0,0,0), GridSize - new int3(1,1,1));
            int flat = c.x + GridSize.x * (c.y + GridSize.y * c.z);
            Unity.Mathematics.AtomicHelpers.Add(ref Counts.GetRef(flat), 1);
        }
    }

    [BurstCompile]
    private struct PrefixSumJob : IJob
    {
        public NativeArray<int> Counts;
        public NativeArray<int> Offsets;

        public void Execute()
        {
            int sum = 0;
            for (int i = 0; i < Counts.Length; i++)
            {
                Offsets[i] = sum;
                sum += Counts[i];
            }
        }
    }

    [BurstCompile]
    private struct FillCellsJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<float3> Positions;
        public float3 Origin;
        public float CellSize;
        public int3 GridSize;

        public NativeArray<int> Offsets; // exclusive
        public NativeArray<int> Counts;  // will be used as cursors
        public NativeArray<int> WriteIndices;

        public void Execute(int index)
        {
            var p = Positions[index];
            var rel = p - Origin;
            int3 c = (int3)math.floor(rel / CellSize);
            c = math.clamp(c, new int3(0,0,0), GridSize - new int3(1,1,1));
            int flat = c.x + GridSize.x * (c.y + GridSize.y * c.z);
            int cursor = Unity.Mathematics.AtomicHelpers.Add(ref Counts.GetRef(flat), 1) - 1;
            int write = Offsets[flat] + cursor;
            WriteIndices[write] = index;
        }
    }
}
