namespace Ducz.AI;

/// <summary>
/// A lightweight finite state machine for enemies and game logic.
///
/// <code>
/// var fsm = new StateMachine();
/// fsm.AddState("idle",
///     onUpdate: dt => { if (SeesPlayer()) fsm.ChangeState("chase"); });
/// fsm.AddState("chase",
///     onEnter: () => PlayAnimation("Run"),
///     onUpdate: dt => MoveTowardsPlayer(dt));
/// fsm.Start("idle");
/// // then call fsm.Update(dt) every frame
/// </code>
/// </summary>
public sealed class StateMachine
{
    private sealed class State
    {
        public Action? OnEnter;
        public Action<float>? OnUpdate;
        public Action? OnExit;
    }

    private readonly Dictionary<string, State> _states = new(StringComparer.OrdinalIgnoreCase);
    private State? _current;

    /// <summary>Name of the active state, or null before <see cref="Start"/>.</summary>
    public string? CurrentState { get; private set; }

    /// <summary>Seconds since the active state was entered.</summary>
    public float TimeInState { get; private set; }

    /// <summary>Raised on every transition (from, to).</summary>
    public event Action<string?, string>? StateChanged;

    /// <summary>Registers a state with optional callbacks. Returns this (fluent).</summary>
    public StateMachine AddState(string name, Action? onEnter = null, Action<float>? onUpdate = null, Action? onExit = null)
    {
        _states[name] = new State { OnEnter = onEnter, OnUpdate = onUpdate, OnExit = onExit };
        return this;
    }

    /// <summary>Enters the initial state.</summary>
    public void Start(string state) => ChangeState(state);

    /// <summary>Transitions to another state (exit callbacks fire, then enter).</summary>
    public void ChangeState(string name)
    {
        if (!_states.TryGetValue(name, out var next))
        {
            Log.Warning($"StateMachine: unknown state \"{name}\".");
            return;
        }

        if (CurrentState != null && string.Equals(CurrentState, name, StringComparison.OrdinalIgnoreCase))
            return;

        _current?.OnExit?.Invoke();
        var previous = CurrentState;
        _current = next;
        CurrentState = name;
        TimeInState = 0f;
        _current.OnEnter?.Invoke();
        StateChanged?.Invoke(previous, name);
    }

    /// <summary>Checks the active state name.</summary>
    public bool IsIn(string state) => string.Equals(CurrentState, state, StringComparison.OrdinalIgnoreCase);

    /// <summary>Advances the active state. Call every frame or physics tick.</summary>
    public void Update(float dt)
    {
        TimeInState += dt;
        _current?.OnUpdate?.Invoke(dt);
    }
}
