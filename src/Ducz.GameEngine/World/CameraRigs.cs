using System.Numerics;

namespace Ducz;

/// <summary>
/// A free-flying debug/editor camera: WASD to move, right mouse button (hold) or
/// captured mouse to look, Shift to speed up, E/Q for up/down.
/// Add it and you can fly around instantly - perfect for inspecting scenes.
/// </summary>
public class FlyCamera : Camera3D
{
    private float _yaw;
    private float _pitch;

    /// <summary>Movement speed in units/second.</summary>
    public float MoveSpeed { get; set; } = 8f;

    /// <summary>Multiplier while Shift is held.</summary>
    public float SprintMultiplier { get; set; } = 3.5f;

    /// <summary>Mouse look sensitivity.</summary>
    public float Sensitivity { get; set; } = 0.0035f;

    /// <summary>When true the cursor is captured permanently; otherwise hold the right mouse button to look.</summary>
    public bool CaptureMouse { get; set; }

    /// <summary>
    /// Seconds the right button must be held before mouse-look starts (0 = immediately).
    /// Lets tools treat a quick right-click as a click instead of a camera drag.
    /// </summary>
    public float LookHoldDelay { get; set; }

    private bool _looking;
    private float _rightHeld;

    public FlyCamera(string? name = null) : base(name) { }

    protected override void OnReady()
    {
        var euler = RotationDegrees;
        _pitch = euler.X * Mathf.Deg2Rad;
        _yaw = euler.Y * Mathf.Deg2Rad;
        if (CaptureMouse)
            Input.SetMouseMode(MouseMode.Captured);
    }

    protected override void OnUpdate(float dt)
    {
        if (!CaptureMouse)
        {
            if (Input.IsMouseButtonDown(MouseButton.Right))
            {
                _rightHeld += dt;
                if (!_looking && _rightHeld >= LookHoldDelay)
                {
                    _looking = true;
                    Input.SetMouseMode(MouseMode.Captured);
                }
            }
            else
            {
                if (_looking)
                    Input.SetMouseMode(MouseMode.Visible);
                _looking = false;
                _rightHeld = 0f;
            }
        }

        bool looking = CaptureMouse || _looking;

        if (looking)
        {
            _yaw -= Input.MouseDelta.X * Sensitivity;
            _pitch -= Input.MouseDelta.Y * Sensitivity;
            _pitch = Mathf.Clamp(_pitch, -1.55f, 1.55f);
            Rotation = Quaternion.CreateFromYawPitchRoll(_yaw, _pitch, 0f);
        }

        var move = Vector3.Zero;
        if (Input.IsKeyDown(Key.W)) move.Z -= 1f;
        if (Input.IsKeyDown(Key.S)) move.Z += 1f;
        if (Input.IsKeyDown(Key.A)) move.X -= 1f;
        if (Input.IsKeyDown(Key.D)) move.X += 1f;
        if (Input.IsKeyDown(Key.E)) move.Y += 1f;
        if (Input.IsKeyDown(Key.Q)) move.Y -= 1f;

        if (move != Vector3.Zero)
        {
            move = Vector3.Normalize(move);
            float speed = MoveSpeed * (Input.IsKeyDown(Key.LeftShift) ? SprintMultiplier : 1f);
            TranslateLocal(move * speed * dt);
        }
    }
}

/// <summary>
/// A third-person orbit camera that stays glued to a target node, with
/// mouse-controlled orbit. Works out of the box for action/adventure games:
///
/// <code>
/// var camera = AddChild(new ThirdPersonCamera { Target = player, Distance = 6f });
/// Input.SetMouseMode(MouseMode.Captured);
/// </code>
///
/// By default the camera follows the target rigidly (no lag) and only rotates
/// while the mouse is captured, so it never spins away from the character.
/// </summary>
public class ThirdPersonCamera : Camera3D
{
    private float _yaw;
    private float _pitch = 0.4f;
    private bool _snapped;

    /// <summary>The node to follow.</summary>
    public Node3D? Target { get; set; }

    /// <summary>Distance from the target.</summary>
    public float Distance { get; set; } = 6f;

    /// <summary>Extra height added to the target position (aim at the head, not the feet).</summary>
    public float TargetHeight { get; set; } = 1.2f;

    /// <summary>Mouse sensitivity (radians per pixel).</summary>
    public float Sensitivity { get; set; } = 0.0035f;

    /// <summary>
    /// Position smoothing. 0 (default) = rigid: the camera is locked to the target
    /// with zero lag. Higher values add a soft follow (12 = subtle, 5 = floaty).
    /// </summary>
    public float Smoothing { get; set; }

    /// <summary>
    /// When true (default) the orbit only reacts to the mouse while it is captured
    /// (<see cref="MouseMode.Captured"/>). Prevents the camera from spinning while
    /// the cursor is free over menus/editors.
    /// </summary>
    public bool RotateOnlyWhenCaptured { get; set; } = true;

    /// <summary>
    /// Pitch limits in radians. Positive pitch looks down (camera rises). The
    /// defaults keep the camera between roughly eye level and a high angle, so it
    /// never rolls, flips or dips below the character.
    /// </summary>
    public float MinPitch { get; set; } = -0.25f;
    public float MaxPitch { get; set; } = 1.2f;

    /// <summary>When true the camera raycasts against physics and moves closer to avoid clipping walls.</summary>
    public bool CollisionEnabled { get; set; } = true;

    /// <summary>Current yaw angle in radians - use it to make the player move camera-relative.</summary>
    public float Yaw => _yaw;

    /// <summary>Planar forward direction of the camera (useful for movement).</summary>
    public Vector3 PlanarForward => new(-MathF.Sin(_yaw), 0f, -MathF.Cos(_yaw));

    /// <summary>Planar right direction of the camera.</summary>
    public Vector3 PlanarRight => new(MathF.Cos(_yaw), 0f, -MathF.Sin(_yaw));

    public ThirdPersonCamera(string? name = null) : base(name)
    {
        _pitch = 0.35f;
    }

    /// <summary>Instantly places the camera behind the target at the given yaw (radians).</summary>
    public void SnapBehindTarget(float yaw = 0f)
    {
        _yaw = yaw;
        _snapped = false;
    }

    protected override void OnUpdate(float dt)
    {
        if (Target == null)
            return;

        // Rotate only with a captured mouse, and ignore huge one-frame spikes
        // (alt-tab, capture toggles) that would otherwise fling the camera.
        bool canRotate = !RotateOnlyWhenCaptured || Input.CurrentMouseMode == MouseMode.Captured;
        var delta = Input.MouseDelta;
        if (canRotate && delta.LengthSquared() < 400f * 400f)
        {
            _yaw -= delta.X * Sensitivity;
            _pitch += delta.Y * Sensitivity;   // mouse down = look down
        }

        // Keep yaw bounded and pitch inside a safe range (never straight up/down).
        _yaw = Mathf.WrapAngle(_yaw);
        _pitch = Mathf.Clamp(_pitch, MinPitch, MaxPitch);

        // The camera's rotation IS the yaw/pitch orientation (roll is always 0, so
        // the horizon can never tilt). The camera sits behind the focus along its
        // own forward (-Z) axis.
        var rotation = Quaternion.CreateFromYawPitchRoll(_yaw, _pitch, 0f);
        var forward = Vector3.Transform(-Vector3.UnitZ, rotation);   // direction the camera looks
        var focus = Target.GlobalPosition + new Vector3(0f, TargetHeight, 0f);

        float distance = Distance;
        if (CollisionEnabled)
        {
            // Cast from the focus towards where the camera wants to be.
            if (Engine.Physics.Raycast(focus, -forward, Distance, out var hit,
                    ignore: Target as Physics.PhysicsBody3D))
            {
                distance = MathF.Max(0.4f, hit.Distance - 0.2f);
            }
        }

        var desired = focus - forward * distance;

        if (!_snapped || Smoothing <= 0f)
        {
            GlobalPosition = desired;   // rigid: glued to the character
            _snapped = true;
        }
        else
        {
            GlobalPosition = Mathf.Damp(GlobalPosition, desired, Smoothing, dt);
        }
        GlobalRotation = rotation;
    }
}

/// <summary>
/// A top-down / RTS strategy camera. It looks down at a focus point on the ground
/// from an angle; you pan the focus (WASD / arrows / screen-edge / middle-drag),
/// zoom with the wheel and rotate with Q/E. Ideal for city builders and strategy games.
///
/// <code>
/// var camera = AddChild(new TopDownCamera { FocusPoint = Vector3.Zero, Distance = 24f });
/// // each frame, pick the tile under the cursor:
/// var ground = camera.ScreenPointToGround(Input.MousePosition);
/// </code>
/// </summary>
public class TopDownCamera : Camera3D
{
    /// <summary>The point on the ground the camera looks at (panned by input).</summary>
    public Vector3 FocusPoint { get; set; } = Vector3.Zero;

    /// <summary>Distance from the focus point (controlled by zoom).</summary>
    public float Distance { get; set; } = 24f;

    /// <summary>Look-down angle in degrees (90 = straight down, 45 = isometric-ish).</summary>
    public float PitchDegrees { get; set; } = 55f;

    /// <summary>Horizontal rotation of the camera around the focus, in degrees.</summary>
    public float YawDegrees { get; set; }

    /// <summary>Pan speed in world units per second (scaled by zoom).</summary>
    public float PanSpeed { get; set; } = 18f;

    /// <summary>Rotation speed in degrees per second (Q/E keys).</summary>
    public float RotateSpeed { get; set; } = 90f;

    /// <summary>Zoom step per mouse-wheel notch.</summary>
    public float ZoomStep { get; set; } = 3f;

    public float MinDistance { get; set; } = 8f;
    public float MaxDistance { get; set; } = 60f;

    /// <summary>Enable WASD / arrow-key panning.</summary>
    public bool KeyboardPan { get; set; } = true;

    /// <summary>Enable panning when the cursor touches the window edges.</summary>
    public bool EdgePan { get; set; } = true;

    /// <summary>Thickness of the screen-edge pan border, in pixels.</summary>
    public float EdgeBorder { get; set; } = 12f;

    /// <summary>Enable panning by dragging with the middle mouse button.</summary>
    public bool MiddleDragPan { get; set; } = true;

    /// <summary>Follow smoothing (0 = instant).</summary>
    public float Smoothing { get; set; } = 12f;

    /// <summary>Optional pan bounds on X/Z (half-extents from the origin). 0 = unbounded.</summary>
    public float PanLimit { get; set; }

    private Vector3? _lastDragGround;

    public TopDownCamera(string? name = null) : base(name) { }

    protected override void OnReady()
    {
        // Start already framed so there's no first-frame lerp from the origin.
        ApplyTransform(instant: true);
    }

    protected override void OnUpdate(float dt)
    {
        var forwardPlanar = new Vector3(-MathF.Sin(YawDegrees * Mathf.Deg2Rad), 0f, -MathF.Cos(YawDegrees * Mathf.Deg2Rad));
        var rightPlanar = new Vector3(MathF.Cos(YawDegrees * Mathf.Deg2Rad), 0f, -MathF.Sin(YawDegrees * Mathf.Deg2Rad));

        var pan = Vector2.Zero;
        if (KeyboardPan)
        {
            if (Input.IsKeyDown(Key.W) || Input.IsKeyDown(Key.Up)) pan.Y += 1f;
            if (Input.IsKeyDown(Key.S) || Input.IsKeyDown(Key.Down)) pan.Y -= 1f;
            if (Input.IsKeyDown(Key.D) || Input.IsKeyDown(Key.Right)) pan.X += 1f;
            if (Input.IsKeyDown(Key.A) || Input.IsKeyDown(Key.Left)) pan.X -= 1f;
        }

        if (EdgePan && !UI.Canvas.IsMouseOverUI)
        {
            var mouse = Input.MousePosition;
            var size = Engine.WindowSize;
            if (mouse.X <= EdgeBorder) pan.X -= 1f;
            else if (mouse.X >= size.X - EdgeBorder) pan.X += 1f;
            if (mouse.Y <= EdgeBorder) pan.Y += 1f;
            else if (mouse.Y >= size.Y - EdgeBorder) pan.Y -= 1f;
        }

        if (pan != Vector2.Zero)
        {
            pan = Vector2.Normalize(pan);
            // Scale pan speed with zoom so it feels consistent close and far.
            float speed = PanSpeed * (Distance / 24f);
            FocusPoint += (rightPlanar * pan.X + forwardPlanar * pan.Y) * speed * dt;
        }

        // Middle-mouse drag pan: keep the grabbed ground point under the cursor.
        if (MiddleDragPan && Input.IsMouseButtonDown(MouseButton.Middle))
        {
            var ground = ScreenPointToGround(Input.MousePosition, FocusPoint.Y);
            if (_lastDragGround is { } last && ground is { } current)
                FocusPoint -= current - last;
            _lastDragGround = ScreenPointToGround(Input.MousePosition, FocusPoint.Y);
        }
        else
        {
            _lastDragGround = null;
        }

        // Rotate.
        if (Input.IsKeyDown(Key.Q)) YawDegrees += RotateSpeed * dt;
        if (Input.IsKeyDown(Key.E)) YawDegrees -= RotateSpeed * dt;

        // Zoom.
        if (!UI.Canvas.IsMouseOverUI && Input.ScrollDelta.Y != 0f)
            Distance = Mathf.Clamp(Distance - Input.ScrollDelta.Y * ZoomStep, MinDistance, MaxDistance);

        if (PanLimit > 0f)
            FocusPoint = new Vector3(
                Mathf.Clamp(FocusPoint.X, -PanLimit, PanLimit),
                FocusPoint.Y,
                Mathf.Clamp(FocusPoint.Z, -PanLimit, PanLimit));

        ApplyTransform(instant: false, dt);
    }

    private void ApplyTransform(bool instant, float dt = 0f)
    {
        // Place the camera above and to one side of the focus, then look at it.
        // Explicit offset + LookAt avoids any yaw/pitch sign ambiguity, and the
        // clamped pitch (never fully vertical) keeps the view roll-free.
        float pitch = Mathf.Clamp(PitchDegrees, 15f, 89f) * Mathf.Deg2Rad;
        float yaw = YawDegrees * Mathf.Deg2Rad;
        var offset = new Vector3(
            MathF.Cos(pitch) * MathF.Sin(yaw),
            MathF.Sin(pitch),
            MathF.Cos(pitch) * MathF.Cos(yaw)) * Distance;

        var desired = FocusPoint + offset;
        GlobalPosition = instant || Smoothing <= 0f
            ? desired
            : Mathf.Damp(GlobalPosition, desired, Smoothing, dt);
        LookAt(FocusPoint);
    }
}
