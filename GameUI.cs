using Unity.Entities;
using UnityEngine;
using UnityEngine.UI;

public class GameUI : MonoBehaviour
{
    public Slider Separation;
    public Slider Alignment;
    public Slider Cohesion;
    public Slider Goal;
    public Slider MaxSpeed;
    public Slider Aggression; // maps to GoalWeight for visual punch

    public Text AgentCountText;
    public Text FpsText;

    private EntityManager _em;

    void Start()
    {
        _em = World.DefaultGameObjectInjectionWorld.EntityManager;
    }

    void Update()
    {
        if (!_em.CreateEntityQuery(typeof(BoidsParams)).TryGetSingleton(out BoidsParams bp))
            return;

        // Update from sliders
        bp.SeparationWeight = Separation.value;
        bp.AlignmentWeight = Alignment.value;
        bp.CohesionWeight = Cohesion.value;
        bp.GoalWeight = Goal.value + Aggression.value * 0.5f;
        bp.MaxSpeed = MaxSpeed.value;
        _em.CreateEntityQuery(typeof(BoidsParams)).SetSingleton(bp);

        // Agent count
        int count = _em.CreateEntityQuery(typeof(AgentTag)).CalculateEntityCount();
        if (AgentCountText) AgentCountText.text = $"Agents: {count:N0}";

        // FPS
        if (FpsText) FpsText.text = $"FPS: {(1f / Mathf.Max(0.0001f, Time.unscaledDeltaTime)):N0}";
    }
}
