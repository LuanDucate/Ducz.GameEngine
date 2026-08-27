using System.Numerics;

namespace Ducz.Tools.SceneEditor;

/// <summary>
/// Orthographic map view looking straight down: WASD / arrow-free panning, mouse
/// wheel zoom, right-drag pan. Picking (<see cref="Camera3D.ScreenPointToRay"/>) keeps
/// working, so blocks can be placed exactly like in the fly view.
/// </summary>
public sealed class TopDownCamera : Camera3D
{
    public float PanSpeed { get; set; } = 14f;
    public float MinSize { get; set; } = 2f;
    public float MaxSize { get; set; } = 120f;

    public TopDownCamera() : base("TopDownCamera")
    {
        Projection = CameraProjection.Orthographic;
        OrthographicSize = 14f;
        Near = 0.05f;
        Far = 1000f;
        Position = new Vector3(0f, 200f, 0f);
        RotationDegrees = new Vector3(-90f, 0f, 0f);
    }

    /// <summary>Centers the view over a world point (keeps the height).</summary>
    public void LookOver(Vector3 point) => Position = new Vector3(point.X, Position.Y, point.Z);

    protected override void OnUpdate(float dt)
    {
        var move = Vector3.Zero;
        if (Input.IsKeyDown(Key.W)) move.Z -= 1f;
        if (Input.IsKeyDown(Key.S)) move.Z += 1f;
        if (Input.IsKeyDown(Key.A)) move.X -= 1f;
        if (Input.IsKeyDown(Key.D)) move.X += 1f;
        if (move != Vector3.Zero)
        {
            // Pan speed scales with zoom so it feels constant on screen.
            float speed = PanSpeed * (OrthographicSize / 14f) * (Input.IsKeyDown(Key.LeftShift) ? 3f : 1f);
            Position += Vector3.Normalize(move) * speed * dt;
        }

        // Right-drag pans (delta in pixels -> world units).
        if (Input.IsMouseButtonDown(MouseButton.Right))
        {
            float unitsPerPixel = OrthographicSize * 2f / MathF.Max(1f, Engine.WindowSize.Y);
            var delta = Input.MouseDelta * unitsPerPixel;
            Position += new Vector3(-delta.X, 0f, -delta.Y);
        }

        // Wheel zooms around the view center - unless the pointer is over the UI, where the
        // wheel belongs to the panel under it (the sidebar scrolls with it).
        float scroll = Ducz.UI.Canvas.IsMouseOverUI ? 0f : Input.ScrollDelta.Y;
        if (MathF.Abs(scroll) > 0.001f)
            OrthographicSize = Mathf.Clamp(OrthographicSize * MathF.Pow(0.85f, scroll), MinSize, MaxSize);

        // E/Q also zoom (mirrors the fly camera's up/down keys).
        if (Input.IsKeyDown(Key.Q)) OrthographicSize = MathF.Min(MaxSize, OrthographicSize * (1f + 1.5f * dt));
        if (Input.IsKeyDown(Key.E)) OrthographicSize = MathF.Max(MinSize, OrthographicSize * (1f - 1.5f * dt));
    }
}
