using UnityEngine;

public sealed class MeowFestivalVisual : MonoBehaviour
{
    private static GameObject prefab;
    private readonly GameObject[] cats = new GameObject[3];
    private readonly Vector3[] starts = new Vector3[3];
    private Player owner;
    private int sequence = -1;
    private float elapsed;
    public void Initialize(Player player)
    {
        owner = player;
        if (prefab == null) prefab = Resources.Load<GameObject>("MeowFestivalVisual");
    }
    public void Show(bool active)
    {
        if (!active) { Hide(); return; }
        if (prefab == null) return;
        if (sequence != owner.FestivalSequence)
        {
            sequence = owner.FestivalSequence; elapsed = 0;
            for (int i=0; i<cats.Length; i++)
            {
                starts[i] = owner.FestivalOrigin + Vector3.up * (i*.12f);
                if (cats[i] == null) cats[i] = MagicVfxPool.Rent(prefab, starts[i], Quaternion.identity, transform);
                TimeVfxSpeed.Bind(cats[i], owner.Runner, owner.TeamIndex, false);
            }
        }
    }
    private void LateUpdate()
    {
        if (owner == null || !owner.HasMeowFestival) { Hide(); return; }
        elapsed += Time.deltaTime * owner.TimeFieldSpeed;
        float arrival = Mathf.SmoothStep(0,1,Mathf.Clamp01(elapsed/.8f));
        bool visible = !owner.IsHiddenBySmokeFor(Player.LocalPlayer);
        for (int i=0; i<cats.Length; i++)
        {
            if (cats[i] == null) continue;
            cats[i].SetActive(visible);
            float angle = elapsed*2f+i*Mathf.PI*2f/3f;
            Vector3 destination = owner.LockAimPoint + new Vector3(Mathf.Cos(angle)*1.15f, .2f+Mathf.Sin(angle*2)*.12f, Mathf.Sin(angle)*1.15f);
            Vector3 previous = cats[i].transform.position;
            Vector3 position = Vector3.Lerp(starts[i],destination,arrival) + Vector3.up*(Mathf.Sin(arrival*Mathf.PI)*.5f);
            Vector3 forward = position-previous;
            Quaternion rotation = forward.sqrMagnitude > .0001f ? Quaternion.LookRotation(forward) : cats[i].transform.rotation;
            cats[i].transform.SetPositionAndRotation(position,rotation);
        }
    }
    public void Hide()
    {
        for (int i=0; i<cats.Length; i++)
        {
            if (cats[i] != null) MagicVfxPool.Release(cats[i]);
            cats[i] = null;
        }
        sequence = -1;
    }
    private void OnDisable() => Hide();
    private void OnDestroy() => Hide();
}
