using System.Numerics;
using Ducz.Physics;
using Ducz.Rendering;

namespace Ducz;

/// <summary>
/// A ready-to-use third-person player: capsule body, camera-relative WASD movement,
/// jumping, sprint, gravity and fall respawn. Designed for data-driven scenes
/// (the JSON "player" node type creates one) but usable from code too:
///
/// <code>
/// var player = AddChild(new PlayerController3D());
/// var camera = AddChild(new ThirdPersonCamera { Target = player });
/// player.Camera = camera;
/// Input.SetMouseMode(MouseMode.Captured);
/// </code>
///
/// Uses the default movement actions (registered automatically):
/// move_left/right/forward/back, jump, sprint.
/// </summary>
public class PlayerController3D : CharacterBody3D
{
    /// <summary>Walk speed in units/second.</summary>
    public float MoveSpeed { get; set; } = 7f;

    /// <summary>Speed multiplier while the sprint action is held.</summary>
    public float SprintMultiplier { get; set; } = 1.6f;

    /// <summary>Vertical velocity applied on jump.</summary>
    public float JumpSpeed { get; set; } = 8.5f;

    /// <summary>Downward acceleration (positive number).</summary>
    public float Gravity { get; set; } = 22f;

    /// <summary>Camera used for movement direction. Auto-resolved when null.</summary>
    public Camera3D? Camera { get; set; }

    /// <summary>Creates the default capsule visual (disable to attach your own model).</summary>
    public bool ShowDefaultVisual { get; set; } = true;

    /// <summary>Color of the default capsule visual.</summary>
    public Color VisualColor { get; set; } = Color.FromHex("#4f8fea");

    /// <summary>Falling below this Y teleports the player back to its start position.</summary>
    public float RespawnBelowY { get; set; } = -40f;

    /// <summary>Capture the mouse when the player enters the tree (usual for third-person control).</summary>
    public bool CaptureMouseOnReady { get; set; } = true;

    /// <summary>
    /// Animation player driving the visual model (set by <see cref="SetVisualModel"/>).
    /// When present, locomotion clips are switched automatically by movement speed.
    /// </summary>
    public AnimationPlayer? Animator { get; set; }

    /// <summary>Clip names used by the automatic locomotion animation.</summary>
    public string IdleAnimation { get; set; } = "idle";
    public string WalkAnimation { get; set; } = "walk";
    public string RunAnimation { get; set; } = "run";

    /// <summary>Cross-fade time when switching locomotion clips.</summary>
    public float AnimationFadeSeconds { get; set; } = 0.18f;

    private Node3D? _visual;
    private bool _hasCustomVisual;
    private Vector3 _spawnPosition;

    public PlayerController3D(string? name = null) : base(name ?? "Player")
    {
        Shape = new CapsuleShape(0.4f, 1.7f);
        CollisionLayer = 2;
        CollisionMask = 1;
    }

    /// <summary>
    /// Replaces the default capsule with a custom visual (e.g. an animated character
    /// model). The visual is rotated to face the movement direction automatically.
    /// Pass the model's <see cref="AnimationPlayer"/> (or leave null to auto-find it)
    /// to enable automatic idle/walk/run switching.
    /// </summary>
    public void SetVisualModel(Node3D modelRoot, AnimationPlayer? animator = null)
    {
        _visual?.RemoveFromParent();
        _hasCustomVisual = true;

        // Wrapper so facing rotation doesn't fight the model's own orientation offset.
        _visual = AddChild(new Node3D("Visual"));
        _visual.AddChild(modelRoot);
        Animator = animator ?? modelRoot.FindNode<AnimationPlayer>();
    }

    protected override void OnReady()
    {
        InputMap.AddDefaultMovementActions();
        AddToGroup("player");
        _spawnPosition = GlobalPosition;

        if (ShowDefaultVisual && !_hasCustomVisual)
        {
            _visual = AddChild(new Node3D("Visual"));
            _visual.AddChild(new MeshInstance3D(MeshFactory.Capsule(0.4f, 1.7f),
                Material.FromColor(VisualColor)));
            var nose = _visual.AddChild(new MeshInstance3D(
                MeshFactory.Box(0.16f, 0.16f, 0.3f),
                Material.FromColor(Color.White)));
            nose.Position = new Vector3(0f, 0.45f, -0.42f);
        }

        if (CaptureMouseOnReady)
            Input.SetMouseMode(MouseMode.Captured);
    }

    protected override void OnPhysicsUpdate(float dt)
    {
        ResolveCamera();

        var input = Input.GetVector("move_left", "move_right", "move_forward", "move_back");
        var direction = Vector3.Zero;

        if (Camera is ThirdPersonCamera orbit)
        {
            direction = orbit.PlanarForward * -input.Y + orbit.PlanarRight * input.X;
        }
        else if (Camera != null)
        {
            var forward = Mathf.NormalizeSafe(Camera.GlobalForward.Flat());
            var right = Mathf.NormalizeSafe(Camera.GlobalRight.Flat());
            direction = forward * -input.Y + right * input.X;
        }
        else
        {
            direction = new Vector3(input.X, 0f, input.Y);
        }

        float speed = MoveSpeed * (Input.IsActionDown("sprint") ? SprintMultiplier : 1f);
        var velocity = Velocity;
        velocity.X = direction.X * speed;
        velocity.Z = direction.Z * speed;
        velocity.Y -= Gravity * dt;

        if (IsOnFloor && Input.IsActionPressed("jump"))
            velocity.Y = JumpSpeed;

        Velocity = velocity;
        MoveAndSlide();

        // Face the movement direction.
        if (_visual != null && direction.LengthSquared() > 0.01f)
        {
            var look = Mathf.LookRotation(direction);
            _visual.Rotation = Quaternion.Slerp(_visual.Rotation, look, 12f * dt);
        }

        // Fell off the world.
        if (GlobalPosition.Y < RespawnBelowY)
        {
            GlobalPosition = _spawnPosition;
            Velocity = Vector3.Zero;
        }

        UpdateLocomotionAnimation();
    }

    /// <summary>Switches idle/walk/run clips based on planar speed (with graceful fallbacks).</summary>
    private void UpdateLocomotionAnimation()
    {
        if (Animator == null)
            return;

        float planarSpeed = Velocity.Flat().Length();
        string? desired = planarSpeed < 0.4f
            ? IdleAnimation
            : planarSpeed > MoveSpeed * 1.15f ? RunAnimation : WalkAnimation;

        // Fall back to whatever clips actually exist.
        if (!Animator.HasClip(desired))
        {
            if (desired == RunAnimation && Animator.HasClip(WalkAnimation))
                desired = WalkAnimation;
            else if (Animator.HasClip(IdleAnimation))
                desired = IdleAnimation;
            else
                return;
        }

        Animator.Play(desired, AnimationFadeSeconds);
    }

    private void ResolveCamera()
    {
        if (Camera is { IsInsideTree: true })
            return;

        // Prefer a third-person camera that targets us, then the current camera.
        Camera = Tree?.Root.Descendants()
                     .OfType<ThirdPersonCamera>()
                     .FirstOrDefault(c => c.Target == this)
                 ?? Camera3D.CurrentCamera;
    }
}
