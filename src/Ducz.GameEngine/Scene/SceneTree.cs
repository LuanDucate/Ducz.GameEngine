namespace Ducz;

/// <summary>
/// Holds the running node hierarchy. The engine creates one tree; access it from
/// nodes via <see cref="Node.Tree"/> or globally via <see cref="Engine.Tree"/>.
/// </summary>
public sealed class SceneTree
{
    private readonly List<Node> _deletionQueue = new();
    private readonly Dictionary<string, HashSet<Node>> _groups = new();
    private readonly List<Tween> _tweens = new();

    /// <summary>The always-present root of the tree.</summary>
    public Node Root { get; }

    /// <summary>The current "main" scene (a child of <see cref="Root"/>) if one was set via <see cref="ChangeScene"/>.</summary>
    public Node? CurrentScene { get; private set; }

    /// <summary>When true, nodes with <see cref="Node.Active"/> still get Update calls but gameplay can check this flag.</summary>
    public bool Paused { get; set; }

    internal SceneTree()
    {
        Root = new Node("Root");
        Root.PropagateEnterTree(this);
    }

    /// <summary>
    /// Replaces <see cref="CurrentScene"/> with a new scene node.
    /// The previous scene is removed and freed.
    /// </summary>
    public void ChangeScene(Node newScene)
    {
        if (CurrentScene != null)
        {
            Root.RemoveChild(CurrentScene);
        }
        CurrentScene = newScene;
        Root.AddChild(newScene);
    }

    // ---- Groups ----

    /// <summary>All nodes currently tagged in a group (see <see cref="Node.AddToGroup"/>).</summary>
    public IReadOnlyCollection<Node> GetNodesInGroup(string group) =>
        _groups.TryGetValue(group, out var set) ? set : Array.Empty<Node>();

    /// <summary>First node in a group, or null.</summary>
    public Node? GetFirstNodeInGroup(string group)
    {
        if (_groups.TryGetValue(group, out var set))
            foreach (var node in set)
                return node;
        return null;
    }

    internal void RegisterInGroup(string group, Node node)
    {
        if (!_groups.TryGetValue(group, out var set))
        {
            set = new HashSet<Node>();
            _groups[group] = set;
        }
        set.Add(node);
    }

    internal void UnregisterFromGroup(string group, Node node)
    {
        if (_groups.TryGetValue(group, out var set))
            set.Remove(node);
    }

    // ---- Tweens ----

    /// <summary>Creates a tween that is updated by the tree every frame.</summary>
    public Tween CreateTween()
    {
        var tween = new Tween();
        _tweens.Add(tween);
        return tween;
    }

    // ---- Frame processing (called by the engine) ----

    internal void Update(float dt)
    {
        Root.PropagateUpdate(dt);

        for (int i = _tweens.Count - 1; i >= 0; i--)
        {
            if (!_tweens[i].Step(dt))
                _tweens.RemoveAt(i);
        }

        FlushDeletionQueue();
    }

    internal void PhysicsUpdate(float dt)
    {
        Root.PropagatePhysicsUpdate(dt);
    }

    internal void QueueForDeletion(Node node) => _deletionQueue.Add(node);

    private void FlushDeletionQueue()
    {
        if (_deletionQueue.Count == 0)
            return;

        foreach (var node in _deletionQueue)
            node.Parent?.RemoveChild(node);
        _deletionQueue.Clear();
    }
}
