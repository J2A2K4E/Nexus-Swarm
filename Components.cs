using Unity.Entities;
using Unity.Mathematics;

public struct AgentTag : IComponentData {}

public struct Agent : IComponentData
{
    public float3 Velocity;
    public byte State;     // 0=Idle,1=Explore,2=Harvest,3=Attack,4=Defend
    public short GroupId;
    public float Energy;
    public float Carry;
}

public struct TransformData : IComponentData
{
    public float3 Position;
    public quaternion Rotation;
}

public struct BoidsParams : IComponentData
{
    public float SeparationWeight;
    public float AlignmentWeight;
    public float CohesionWeight;
    public float GoalWeight;
    public float AvoidWeight;

    public float MaxSpeed;
    public float MaxAccel;

    public float SeparationRadius;
    public float AlignmentRadius;
    public float CohesionRadius;

    public float NeighborCap; // as float to avoid casting in Burst
    public float Jitter;      // random steering noise
}

public struct WorldBounds : IComponentData
{
    public float3 Center;
    public float3 Extents; // half-size; agents wrap or bounce
    public byte Wrap;      // 1=wrap, 0=bounce
}

// Spatial grid settings
public struct GridConfig : IComponentData
{
    public float CellSize;
    public int3 GridSize;      // cells in each axis
    public float3 Origin;      // world min corner
}

// Per-agent grid index (flattened)
public struct GridIndex : IComponentData
{
    public int Index; // -1 if out of bounds
}

// Goals and fields
public enum GoalType : byte { None=0, Harvest=1, Perimeter=2 }

public struct GroupGoal : IComponentData
{
    public GoalType Type;
    public float3 TargetPos;     // Harvest center or Perimeter center
    public float Radius;         // For Perimeter ring
    public float Intensity;      // 0..1
    public short GroupId;
}

public struct GlobalRng : IComponentData
{
    public uint State;
}

// Render state
public struct AgentRenderState : IComponentData
{
    public float Aggression; // 0..1 affects emissive
    public float Task;       // 0=free,1=harvest,2=defend
}
