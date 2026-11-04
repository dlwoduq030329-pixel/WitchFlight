using UnityEngine;

// A virtual parent on the animated torso; the network flag keeps its own hierarchy.
public sealed class FlagBackMount
{
    private Transform owner, chest, spine, left, right;
    public Transform Chest => chest;
    public void Resolve(Transform body)
    {
        if(owner==body)return;
        owner=body; chest=spine=left=right=null;
        if(body==null)return;
        var animator=body.GetComponentInChildren<Animator>();
        if(animator!=null && animator.isHuman && animator.avatar!=null && animator.avatar.isValid)
        {
            chest=animator.GetBoneTransform(HumanBodyBones.UpperChest) ?? animator.GetBoneTransform(HumanBodyBones.Chest);
            spine=animator.GetBoneTransform(HumanBodyBones.Spine) ?? animator.GetBoneTransform(HumanBodyBones.Hips);
            left=animator.GetBoneTransform(HumanBodyBones.LeftShoulder) ?? animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            right=animator.GetBoneTransform(HumanBodyBones.RightShoulder) ?? animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
        }
        if(chest==null)
            foreach(var t in body.GetComponentsInChildren<Transform>())
                if(t.name=="Chest"){chest=t;break;}
    }
    public void Pose(out Vector3 position,out Quaternion rotation)
    {
        position=chest!=null?chest.position:owner.TransformPoint(new Vector3(0,1.1f,0));
        rotation=owner.rotation;
        if(chest==null || spine==null || left==null || right==null)return;
        Vector3 up=chest.position-spine.position, across=right.position-left.position;
        Vector3 forward=Vector3.Cross(across,up);
        if(up.sqrMagnitude>.00001f && forward.sqrMagnitude>.00001f)
            rotation=Quaternion.LookRotation(forward.normalized,up.normalized);
    }
}
