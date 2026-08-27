using Ducz.UI;

namespace Ducz.Tools.SceneEditor;

/// <summary>
/// Press-and-hold support for the small "-" / "+" steppers: holding one keeps nudging the value
/// so you can watch the object slide into place instead of clicking once per step.
/// </summary>
partial class EditorScene
{
    private const float RepeatDelay = 0.35f;    // hold this long before repeating
    private const float RepeatInterval = 0.06f; // then one step every interval

    private sealed class Stepper
    {
        public Button Button = null!;
        public Action<bool> Step = null!;       // argument: is this the first step of the hold?
        public float Held;
        public float Next;
    }

    private readonly List<Stepper> _steppers = new();

    /// <summary>Registers a stepper button. <paramref name="step"/> gets true on the first step.</summary>
    private void AddRepeat(Button button, Action<bool> step) =>
        _steppers.Add(new Stepper { Button = button, Step = step });

    private void UpdateSteppers(float dt)
    {
        foreach (var stepper in _steppers)
        {
            if (!stepper.Button.IsHeld)
            {
                stepper.Held = 0f;
                stepper.Next = 0f;
                continue;
            }

            stepper.Held += dt;
            if (stepper.Held < RepeatDelay)
                continue;

            stepper.Next -= dt;
            if (stepper.Next > 0f)
                continue;

            stepper.Next = RepeatInterval;
            stepper.Step(false);   // the click event already did the first step
        }
    }
}
