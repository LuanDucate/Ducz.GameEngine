using System.Numerics;

namespace Ducz;

/// <summary>Easing functions used by <see cref="Tween"/>.</summary>
public enum Ease
{
    Linear,
    InQuad, OutQuad, InOutQuad,
    InCubic, OutCubic, InOutCubic,
    InSine, OutSine, InOutSine,
    OutBack, OutBounce, OutElastic
}

/// <summary>
/// A lightweight sequential tween. Create with <see cref="SceneTree.CreateTween"/>:
/// <code>
/// Tree.CreateTween()
///     .To(v => node.Position = node.Position with { Y = v }, 0f, 3f, 0.5f, Ease.OutQuad)
///     .Wait(0.2f)
///     .Call(() => Log.Info("done"));
/// </code>
/// Steps run one after another; the tween is removed automatically when finished.
/// </summary>
public sealed class Tween
{
    private abstract class TweenStep
    {
        /// <summary>Advances the step; returns leftover time when finished, or -1 while running.</summary>
        public abstract float Advance(float dt);
        public abstract void Reset();
    }

    private sealed class InterpolateStep : TweenStep
    {
        public required Action<float> Setter;
        public required float From;
        public required float To;
        public required float Duration;
        public required Ease Ease;
        private float _elapsed;

        public override float Advance(float dt)
        {
            _elapsed += dt;
            float t = Duration <= 0f ? 1f : Mathf.Clamp01(_elapsed / Duration);
            Setter(Mathf.Lerp(From, To, Easing.Apply(Ease, t)));
            return _elapsed >= Duration ? _elapsed - Duration : -1f;
        }

        public override void Reset() => _elapsed = 0f;
    }

    private sealed class WaitStep : TweenStep
    {
        public required float Duration;
        private float _elapsed;

        public override float Advance(float dt)
        {
            _elapsed += dt;
            return _elapsed >= Duration ? _elapsed - Duration : -1f;
        }

        public override void Reset() => _elapsed = 0f;
    }

    private sealed class CallStep : TweenStep
    {
        public required Action Action;

        public override float Advance(float dt)
        {
            Action();
            return dt;
        }

        public override void Reset() { }
    }

    private readonly List<TweenStep> _steps = new();
    private int _current;
    private bool _killed;

    /// <summary>When true the tween restarts after the last step.</summary>
    public bool Looping { get; set; }

    /// <summary>Raised once when all steps complete (not raised for looping tweens).</summary>
    public event Action? Finished;

    /// <summary>Animates a float from <paramref name="from"/> to <paramref name="to"/> over <paramref name="duration"/> seconds.</summary>
    public Tween To(Action<float> setter, float from, float to, float duration, Ease ease = Ease.Linear)
    {
        _steps.Add(new InterpolateStep { Setter = setter, From = from, To = to, Duration = duration, Ease = ease });
        return this;
    }

    /// <summary>Animates a Vector2 (e.g. a UI element's Position).</summary>
    public Tween To(Action<Vector2> setter, Vector2 from, Vector2 to, float duration, Ease ease = Ease.Linear) =>
        To(t => setter(Vector2.Lerp(from, to, t)), 0f, 1f, duration, ease);

    /// <summary>Animates a Vector3 (e.g. a node's Position or Scale).</summary>
    public Tween To(Action<Vector3> setter, Vector3 from, Vector3 to, float duration, Ease ease = Ease.Linear) =>
        To(t => setter(Vector3.Lerp(from, to, t)), 0f, 1f, duration, ease);

    /// <summary>Animates a Color (e.g. fades and flashes).</summary>
    public Tween To(Action<Color> setter, Color from, Color to, float duration, Ease ease = Ease.Linear) =>
        To(t => setter(Color.Lerp(from, to, t)), 0f, 1f, duration, ease);

    /// <summary>Pauses the sequence for the given seconds.</summary>
    public Tween Wait(float seconds)
    {
        _steps.Add(new WaitStep { Duration = seconds });
        return this;
    }

    /// <summary>Invokes a callback, then continues.</summary>
    public Tween Call(Action action)
    {
        _steps.Add(new CallStep { Action = action });
        return this;
    }

    /// <summary>Marks the tween to repeat forever.</summary>
    public Tween SetLooping(bool looping = true)
    {
        Looping = looping;
        return this;
    }

    /// <summary>Stops and removes the tween.</summary>
    public void Kill() => _killed = true;

    /// <summary>Returns false when the tween should be removed.</summary>
    internal bool Step(float dt)
    {
        if (_killed)
            return false;

        while (dt >= 0f && _current < _steps.Count)
        {
            float leftover = _steps[_current].Advance(dt);
            if (leftover < 0f)
                return true; // step still running

            _steps[_current].Reset();
            _current++;
            dt = leftover;

            if (_current >= _steps.Count && Looping)
                _current = 0;

            if (dt <= 0f)
                dt = 0f;

            // Avoid infinite loops when every step has zero duration and looping is on.
            if (Looping && _steps.Count > 0 && dt == 0f && _current == 0)
                return true;
        }

        if (_current >= _steps.Count)
        {
            Finished?.Invoke();
            return false;
        }
        return true;
    }
}

/// <summary>Evaluates easing curves (t in 0..1).</summary>
public static class Easing
{
    public static float Apply(Ease ease, float t) => ease switch
    {
        Ease.InQuad => t * t,
        Ease.OutQuad => 1f - (1f - t) * (1f - t),
        Ease.InOutQuad => t < 0.5f ? 2f * t * t : 1f - MathF.Pow(-2f * t + 2f, 2f) / 2f,
        Ease.InCubic => t * t * t,
        Ease.OutCubic => 1f - MathF.Pow(1f - t, 3f),
        Ease.InOutCubic => t < 0.5f ? 4f * t * t * t : 1f - MathF.Pow(-2f * t + 2f, 3f) / 2f,
        Ease.InSine => 1f - MathF.Cos(t * MathF.PI / 2f),
        Ease.OutSine => MathF.Sin(t * MathF.PI / 2f),
        Ease.InOutSine => -(MathF.Cos(MathF.PI * t) - 1f) / 2f,
        Ease.OutBack => 1f + 2.70158f * MathF.Pow(t - 1f, 3f) + 1.70158f * MathF.Pow(t - 1f, 2f),
        Ease.OutBounce => OutBounce(t),
        Ease.OutElastic => OutElastic(t),
        _ => t
    };

    private static float OutBounce(float t)
    {
        const float n1 = 7.5625f;
        const float d1 = 2.75f;
        if (t < 1f / d1) return n1 * t * t;
        if (t < 2f / d1) { t -= 1.5f / d1; return n1 * t * t + 0.75f; }
        if (t < 2.5f / d1) { t -= 2.25f / d1; return n1 * t * t + 0.9375f; }
        t -= 2.625f / d1;
        return n1 * t * t + 0.984375f;
    }

    private static float OutElastic(float t)
    {
        if (t <= 0f) return 0f;
        if (t >= 1f) return 1f;
        const float c4 = 2f * MathF.PI / 3f;
        return MathF.Pow(2f, -10f * t) * MathF.Sin((t * 10f - 0.75f) * c4) + 1f;
    }
}
