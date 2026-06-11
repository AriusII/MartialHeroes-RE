namespace MartialHeroes.Client.Domain.Actors;

/// <summary>
/// Actor lifecycle / motion state.
/// </summary>
/// <remarks>
/// Numeric values mirror the legacy client's lifecycle/motion state field.
/// spec: Docs/RE/structs/actor.md (Actor base fields, <c>lifecycle_state</c> at +0x58C:
/// 0=uninit, 1=refreshing, 2=walk, 3=run, 8=dead/scripted). Values not enumerated by the spec
/// are intentionally absent; an unknown wire value should be rejected at the decode boundary.
/// </remarks>
public enum LifecycleState : int
{
    /// <summary>Uninitialised. spec: lifecycle_state == 0.</summary>
    Uninitialised = 0,

    /// <summary>Refreshing / re-spawning. spec: lifecycle_state == 1.</summary>
    Refreshing = 1,

    /// <summary>Walking. spec: lifecycle_state == 2.</summary>
    Walking = 2,

    /// <summary>Running. spec: lifecycle_state == 3.</summary>
    Running = 3,

    /// <summary>Dead or under scripted control. spec: lifecycle_state == 8.</summary>
    Dead = 8,
}
