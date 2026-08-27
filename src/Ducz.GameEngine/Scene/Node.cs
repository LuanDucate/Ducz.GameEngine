namespace Ducz;

/// <summary>
/// Base class of everything that lives in the scene tree.
/// A node can have children, receives lifecycle callbacks and can be tagged in groups.
///
/// Lifecycle:
/// <list type="bullet">
/// <item><see cref="OnReady"/> - called once, when the node enters the tree.</item>
/// <item><see cref="OnUpdate"/> - called every rendered frame.</item>
/// <item><see cref="OnPhysicsUpdate"/> - called at a fixed rate (<see cref="Time.FixedDeltaTime"/>).</item>
/// <item><see cref="OnExitTree"/> - called when the node leaves the tree (including <see cref="QueueFree"/>).</item>
/// </list>
/// </summary>
public class Node
{
    private static long _nextId = 1;

    private readonly List<Node> _children = new();
    private readonly HashSet<string> _groups = new();
    private bool _readyCalled;

    /// <summary>Unique id assigned at construction (never reused during a run).</summary>
    public long Id { get; } = _nextId++;

    /// <summary>Node name, used by <see cref="FindNode"/>. Defaults to the type name.</summary>
    public string Name { get; set; }

    /// <summary>The parent node, or null when not attached.</summary>
    public Node? Parent { get; private set; }

    /// <summary>The tree this node belongs to, or null when detached.</summary>
    public SceneTree? Tree { get; internal set; }

    /// <summary>True once the node is part of the running scene tree.</summary>
    public bool IsInsideTree => Tree != null;

    /// <summary>When false, this node and its children skip Update/PhysicsUpdate callbacks.</summary>
    public bool Active { get; set; } = true;

    /// <summary>True after <see cref="QueueFree"/> was requested.</summary>
    public bool IsQueuedForDeletion { get; private set; }

    /// <summary>Read-only view of the children list.</summary>
    public IReadOnlyList<Node> Children => _children;

    public Node(string? name = null)
    {
        Name = name ?? GetType().Name;
    }

    // ---- Hierarchy ----

    /// <summary>
    /// Adds a child and returns it, so scenes can be built fluently:
    /// <code>var player = AddChild(new Player());</code>
    /// </summary>
    public T AddChild<T>(T child) where T : Node
    {
        if (child.Parent != null)
            throw new InvalidOperationException($"Node '{child.Name}' already has a parent ('{child.Parent.Name}'). Remove it first.");

        _children.Add(child);
        child.Parent = this;
        child.OnParentChanged();

        if (IsInsideTree)
            child.PropagateEnterTree(Tree!);

        return child;
    }

    /// <summary>Removes a child without destroying it (it can be re-added elsewhere).</summary>
    public void RemoveChild(Node child)
    {
        if (child.Parent != this)
            return;

        if (child.IsInsideTree)
            child.PropagateExitTree();

        _children.Remove(child);
        child.Parent = null;
        child.OnParentChanged();
    }

    /// <summary>Detaches this node from its parent (without destroying it).</summary>
    public void RemoveFromParent() => Parent?.RemoveChild(this);

    /// <summary>Removes every child (without destroying them). Useful when rebuilding UI containers.</summary>
    public void ClearChildren()
    {
        for (int i = _children.Count - 1; i >= 0; i--)
            RemoveChild(_children[i]);
    }

    /// <summary>
    /// Changes a child's position in the children list. Children later in the list
    /// update later and (for UI) draw on top.
    /// </summary>
    public void MoveChild(Node child, int index)
    {
        if (child.Parent != this)
            return;
        _children.Remove(child);
        _children.Insert(Mathf.Clamp(index, 0, _children.Count), child);
    }

    /// <summary>
    /// Marks this node for safe removal at the end of the current frame.
    /// Prefer this over <see cref="RemoveChild"/> during Update callbacks.
    /// </summary>
    public void QueueFree()
    {
        if (IsQueuedForDeletion)
            return;
        IsQueuedForDeletion = true;
        Tree?.QueueForDeletion(this);
    }

    /// <summary>Finds the first descendant (depth-first) with the given name, or null.</summary>
    public Node? FindNode(string name, bool recursive = true)
    {
        foreach (var child in _children)
        {
            if (child.Name == name)
                return child;
            if (recursive && child.FindNode(name) is { } found)
                return found;
        }
        return null;
    }

    /// <summary>Finds the first descendant of type T (optionally matching a name), or null.</summary>
    public T? FindNode<T>(string? name = null, bool recursive = true) where T : Node
    {
        foreach (var child in _children)
        {
            if (child is T typed && (name == null || child.Name == name))
                return typed;
            if (recursive && child.FindNode<T>(name) is { } found)
                return found;
        }
        return null;
    }

    /// <summary>Returns the closest ancestor of type T, or null.</summary>
    public T? FindAncestor<T>() where T : Node
    {
        var current = Parent;
        while (current != null)
        {
            if (current is T typed)
                return typed;
            current = current.Parent;
        }
        return null;
    }

    /// <summary>Enumerates all descendants depth-first.</summary>
    public IEnumerable<Node> Descendants()
    {
        foreach (var child in _children)
        {
            yield return child;
            foreach (var grandChild in child.Descendants())
                yield return grandChild;
        }
    }

    // ---- Groups ----

    /// <summary>Tags this node in a named group (e.g. "enemies"). Query with <see cref="SceneTree.GetNodesInGroup"/>.</summary>
    public void AddToGroup(string group)
    {
        _groups.Add(group);
        Tree?.RegisterInGroup(group, this);
    }

    public void RemoveFromGroup(string group)
    {
        _groups.Remove(group);
        Tree?.UnregisterFromGroup(group, this);
    }

    public bool IsInGroup(string group) => _groups.Contains(group);

    // ---- Lifecycle callbacks (override in your nodes) ----

    /// <summary>Called once when the node enters the tree. Build/setup here.</summary>
    protected virtual void OnReady() { }

    /// <summary>Called every frame. <paramref name="dt"/> is <see cref="Time.DeltaTime"/>.</summary>
    protected virtual void OnUpdate(float dt) { }

    /// <summary>Called at a fixed rate for physics-related logic.</summary>
    protected virtual void OnPhysicsUpdate(float dt) { }

    /// <summary>Called when the node leaves the tree.</summary>
    protected virtual void OnExitTree() { }

    /// <summary>Called when the parent changes (attach/detach).</summary>
    protected virtual void OnParentChanged() { }

    /// <summary>Called right after entering the tree, before OnReady.</summary>
    protected virtual void OnEnterTree() { }

    // ---- Internal propagation ----

    internal void PropagateEnterTree(SceneTree tree)
    {
        Tree = tree;
        foreach (var group in _groups)
            tree.RegisterInGroup(group, this);

        OnEnterTree();

        if (!_readyCalled)
        {
            _readyCalled = true;
            OnReady();
        }

        // Children added inside OnReady are already in the tree (AddChild handles it),
        // so only propagate to the ones that are not.
        for (int i = 0; i < _children.Count; i++)
        {
            var child = _children[i];
            if (!child.IsInsideTree)
                child.PropagateEnterTree(tree);
        }
    }

    internal void PropagateExitTree()
    {
        for (int i = _children.Count - 1; i >= 0; i--)
            _children[i].PropagateExitTree();

        OnExitTree();

        if (Tree != null)
        {
            foreach (var group in _groups)
                Tree.UnregisterFromGroup(group, this);
            Tree = null;
        }
    }

    internal void PropagateUpdate(float dt)
    {
        if (!Active || IsQueuedForDeletion)
            return;

        OnUpdate(dt);

        for (int i = 0; i < _children.Count; i++)
        {
            if (i < _children.Count)
                _children[i].PropagateUpdate(dt);
        }
    }

    internal void PropagatePhysicsUpdate(float dt)
    {
        if (!Active || IsQueuedForDeletion)
            return;

        OnPhysicsUpdate(dt);

        for (int i = 0; i < _children.Count; i++)
        {
            if (i < _children.Count)
                _children[i].PropagatePhysicsUpdate(dt);
        }
    }

    public override string ToString() => $"{GetType().Name}(\"{Name}\")";
}
