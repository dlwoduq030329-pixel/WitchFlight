using UnityEngine;

public sealed class RobbyGoal : MonoBehaviour
{
    [SerializeField] private Collider goalTrigger;
    [SerializeField] private Animator[] goalCats;
    [SerializeField] private RobbyGoalNet net;
    private Vector3[] positions;
    private Quaternion[] rotations;
    private int[] idleStates;
    private float returnAt = -1f;
    private int cheerState;
    private void Awake()
    {
        int count = goalCats == null ? 0 : goalCats.Length;
        positions = new Vector3[count]; rotations = new Quaternion[count]; idleStates = new int[count];
        var entry = CatCompanionTable.Default != null ? CatCompanionTable.Default.GetAnimation(CatAnimation.Cheer) : null;
        cheerState = Animator.StringToHash(entry != null ? entry.stateName : "cat - cheer");
        for (int i = 0; i < count; i++) if (goalCats[i] != null)
        {
            positions[i] = goalCats[i].transform.localPosition;
            rotations[i] = goalCats[i].transform.localRotation;
            goalCats[i].applyRootMotion = false;
        }
        enabled = false;
    }
    public bool ContainsBall(Vector3 from, Vector3 to, float radius)
    {
        if (goalTrigger == null || !goalTrigger.enabled || !goalTrigger.isTrigger || !goalTrigger.gameObject.activeInHierarchy) return false;
        if ((goalTrigger.ClosestPoint(to) - to).sqrMagnitude <= radius * radius) return true;
        Vector3 delta = to - from;
        return delta.sqrMagnitude > 0.00001f && goalTrigger.Raycast(new Ray(from, delta.normalized), out _, delta.magnitude);
    }
    public void Celebrate(float seconds)
    {
        returnAt = Time.unscaledTime + seconds;
        for (int i = 0; i < goalCats.Length; i++) if (goalCats[i] != null)
        {
            idleStates[i] = goalCats[i].GetCurrentAnimatorStateInfo(0).fullPathHash;
            if (goalCats[i].HasState(0, cheerState)) goalCats[i].CrossFadeInFixedTime(cheerState, 0.1f, 0, 0);
        }
        enabled = true;
    }
    private void Update()
    {
        if (Time.unscaledTime < returnAt) return;
        for (int i = 0; i < goalCats.Length; i++) if (goalCats[i] != null)
        {
            goalCats[i].transform.localPosition = positions[i];
            goalCats[i].transform.localRotation = rotations[i];
            if (idleStates[i] != 0) goalCats[i].CrossFadeInFixedTime(idleStates[i], 0.12f, 0, 0);
        }
        enabled = false;
    }
    public bool IsNet(Collider hit) => net != null && hit != null && hit.transform.IsChildOf(net.transform);
    public void HitNet(Vector3 point, Vector3 impulse) { if (net != null) net.Impact(point, impulse); }
}
