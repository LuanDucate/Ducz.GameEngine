using System.Numerics;

namespace Ducz;

/// <summary>One bone of a <see cref="Skeleton3D"/>.</summary>
public sealed class Bone
{
    public string Name { get; init; } = "";
    public int ParentIndex { get; init; } = -1;

    // Rest (bind) pose, local to the parent bone.
    public Vector3 RestPosition { get; init; }
    public Quaternion RestRotation { get; init; } = Quaternion.Identity;
    public Vector3 RestScale { get; init; } = Vector3.One;

    /// <summary>Matrix that moves a vertex from model space into this bone's bind space.</summary>
    public Matrix4x4 InverseBind { get; init; } = Matrix4x4.Identity;

    // Animated local pose (written by AnimationPlayer or by code).
    public Vector3 LocalPosition { get; set; }
    public Quaternion LocalRotation { get; set; } = Quaternion.Identity;
    public Vector3 LocalScale { get; set; } = Vector3.One;
}

/// <summary>
/// A bone hierarchy that drives skinned meshes. Created automatically when
/// instantiating an animated model; can also be built by code with <see cref="AddBone"/>.
/// Bones must be added parent-first.
/// </summary>
public class Skeleton3D : Node3D
{
    /// <summary>Maximum bones supported by the built-in skinned shader.</summary>
    public const int MaxBones = 128;

    private readonly List<Bone> _bones = new();
    private readonly Dictionary<string, int> _boneIndexByName = new();
    private Matrix4x4[] _skinningMatrices = Array.Empty<Matrix4x4>();
    private Matrix4x4[] _globalPose = Array.Empty<Matrix4x4>();

    /// <summary>All bones, indexable by the values from <see cref="FindBone"/>.</summary>
    public IReadOnlyList<Bone> Bones => _bones;

    public Skeleton3D(string? name = null) : base(name) { }

    /// <summary>Adds a bone (parent must already exist). Returns the bone index.</summary>
    public int AddBone(string name, int parentIndex, Vector3 restPosition, Quaternion restRotation,
        Vector3 restScale, Matrix4x4 inverseBind)
    {
        // Note: a skeleton may hold more bones than the shader limit - skinned
        // meshes connect through a SkinBinding that remaps only the joints they
        // actually use (up to MaxBones per mesh).
        if (parentIndex >= _bones.Count)
            throw new ArgumentException("Bones must be added parent-first.");

        var bone = new Bone
        {
            Name = name,
            ParentIndex = parentIndex,
            RestPosition = restPosition,
            RestRotation = restRotation,
            RestScale = restScale,
            InverseBind = inverseBind,
            LocalPosition = restPosition,
            LocalRotation = restRotation,
            LocalScale = restScale
        };
        _bones.Add(bone);
        _boneIndexByName[name] = _bones.Count - 1;
        return _bones.Count - 1;
    }

    /// <summary>Returns the bone index for a name, or -1.</summary>
    public int FindBone(string name) => _boneIndexByName.TryGetValue(name, out int index) ? index : -1;

    /// <summary>Resets every bone to the bind pose.</summary>
    public void ResetToRestPose()
    {
        foreach (var bone in _bones)
        {
            bone.LocalPosition = bone.RestPosition;
            bone.LocalRotation = bone.RestRotation;
            bone.LocalScale = bone.RestScale;
        }
    }

    /// <summary>World-space (skeleton-space) transform of a bone's animated pose.</summary>
    public Matrix4x4 GetBoneGlobalPose(int boneIndex)
    {
        ComputePoses();
        return _globalPose[boneIndex];
    }

    /// <summary>
    /// Computes the final skinning matrices consumed by the GPU.
    /// Called by the renderer once per frame.
    /// </summary>
    public Matrix4x4[] GetSkinningMatrices()
    {
        ComputePoses();
        return _skinningMatrices;
    }

    private void ComputePoses()
    {
        int count = Math.Min(_bones.Count, MaxBones);
        if (_skinningMatrices.Length != count)
        {
            _skinningMatrices = new Matrix4x4[count];
            _globalPose = new Matrix4x4[Math.Max(count, _bones.Count)];
        }

        for (int i = 0; i < _bones.Count; i++)
        {
            var bone = _bones[i];
            var local = Matrix4x4.CreateScale(bone.LocalScale)
                      * Matrix4x4.CreateFromQuaternion(bone.LocalRotation)
                      * Matrix4x4.CreateTranslation(bone.LocalPosition);

            _globalPose[i] = bone.ParentIndex < 0 ? local : local * _globalPose[bone.ParentIndex];
        }

        for (int i = 0; i < count; i++)
            _skinningMatrices[i] = _bones[i].InverseBind * _globalPose[i];
    }
}
