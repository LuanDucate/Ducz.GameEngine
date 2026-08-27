using System.Numerics;

namespace Ducz;

/// <summary>
/// Connects a skinned mesh to a <see cref="Skeleton3D"/>: which bones the mesh's
/// joint indices refer to, and their inverse bind matrices. Created automatically
/// when instantiating models; build manually only for procedural skinned meshes.
/// </summary>
public sealed class SkinBinding
{
    private readonly Matrix4x4[] _matrices;

    /// <summary>The skeleton that drives the mesh.</summary>
    public Skeleton3D Skeleton { get; }

    /// <summary>For each mesh joint index, the bone index in the skeleton.</summary>
    public int[] JointToBone { get; }

    /// <summary>Inverse bind matrix per mesh joint.</summary>
    public Matrix4x4[] InverseBinds { get; }

    public SkinBinding(Skeleton3D skeleton, int[] jointToBone, Matrix4x4[] inverseBinds)
    {
        if (jointToBone.Length != inverseBinds.Length)
            throw new ArgumentException("jointToBone and inverseBinds must have the same length.");

        Skeleton = skeleton;
        JointToBone = jointToBone;
        InverseBinds = inverseBinds;
        _matrices = new Matrix4x4[Math.Min(jointToBone.Length, Skeleton3D.MaxBones)];
    }

    /// <summary>Final skinning matrices for the GPU (index-aligned with the mesh's joint indices).</summary>
    public Matrix4x4[] GetSkinningMatrices()
    {
        for (int i = 0; i < _matrices.Length; i++)
        {
            int bone = JointToBone[i];
            _matrices[i] = InverseBinds[i] * Skeleton.GetBoneGlobalPose(bone);
        }
        return _matrices;
    }
}

/// <summary>
/// A node that follows a skeleton bone every frame. Parent it under the skeleton
/// and add children (e.g. a sword mesh) that should move with the bone.
/// </summary>
public class BoneAttachment3D : Node3D
{
    private int _boneIndex = -1;

    /// <summary>The skeleton to follow (defaults to the closest ancestor skeleton).</summary>
    public Skeleton3D? Skeleton { get; set; }

    /// <summary>Name of the bone to follow.</summary>
    public string BoneName { get; set; } = "";

    public BoneAttachment3D(string? name = null) : base(name) { }

    public BoneAttachment3D(string boneName, string? name = null) : base(name)
    {
        BoneName = boneName;
    }

    protected override void OnReady()
    {
        Skeleton ??= FindAncestor<Skeleton3D>();
        if (Skeleton != null)
            _boneIndex = Skeleton.FindBone(BoneName);
        if (_boneIndex < 0)
            Log.Warning($"BoneAttachment3D '{Name}': bone \"{BoneName}\" not found.");
    }

    protected override void OnUpdate(float dt)
    {
        if (Skeleton == null || _boneIndex < 0)
            return;

        var pose = Skeleton.GetBoneGlobalPose(_boneIndex);
        if (Matrix4x4.Decompose(pose, out var scale, out var rotation, out var translation))
        {
            Position = translation;
            Rotation = rotation;
            Scale = scale;
        }
    }
}
