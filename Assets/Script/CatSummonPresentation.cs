using UnityEngine;

public sealed class CatSummonPresentation : MonoBehaviour
{
    private MagicProjectile projectile;
    private CatAvatar avatar;
    private Vector3 origin;
    private float started;
    private bool boarding;
    private int lastAttack;
    private Player owner;
    private int lastHit, lastKill;
    private float reactAfter;

    public static CatSummonPresentation Create(MagicProjectile source, Transform parent, int sequence)
    {
        var table = CatCompanionTable.Default;
        if (table == null || !table.useCatForSummon || source == null) return null;
        var go = new GameObject("WitchCat paper-plane summon");
        go.transform.SetParent(parent, false);
        var view = go.AddComponent<CatSummonPresentation>();
        view.projectile = source; view.lastAttack = sequence;
        var data = PlayerData.FindPerkData(source.Runner, source.Shooter);
        view.avatar = CatAvatar.Create(go.transform, data != null ? data.CatBodyIndex : 1, data != null ? data.CatEyeIndex : 1, true);
        if (view.avatar == null) { Destroy(go); return null; }
        go.AddComponent<CatSummonFeedback>().Initialize(view.avatar, source);
        int summons = 0;
        foreach (var other in MagicProjectile.ActiveProjectiles)
            if (other != null && other.IsLiveSummon && other.Runner == source.Runner && other.Shooter == source.Shooter) summons++;
        Player owner = Player.FindInRunner(source.Runner, source.Shooter);
        view.owner = owner;
        if (owner != null) { view.lastHit = owner.CatHitSequence; view.lastKill = owner.CatKillSequence; }
        view.boarding = summons <= 1 && owner != null && owner.IsAlive;
        view.origin = owner != null ? (owner.GetComponent<CatBattleCompanion>()?.SeatPosition ?? owner.LockAimPoint) : parent.position;
        view.started = Time.unscaledTime;
        if (view.boarding) view.avatar.Play(CatAnimation.Jump);
        return view;
    }

    public void SetAttack(int sequence)
    {
        if (sequence != lastAttack && !boarding && Time.unscaledTime >= reactAfter) avatar.Play(CatAnimation.Punch);
        lastAttack = sequence;
    }

    private void LateUpdate()
    {
        if (avatar == null || projectile == null || projectile.Object == null || !projectile.Object.IsValid) return;
        var table = CatCompanionTable.Default;
        var data = PlayerData.FindPerkData(projectile.Runner, projectile.Shooter);
        if (data != null && data.Object != null && data.Object.IsValid) avatar.SetAppearance(data.CatBodyIndex, data.CatEyeIndex);
        if (boarding)
        {
            float t = Mathf.Clamp01((Time.unscaledTime - started) / table.summonBoardingSeconds);
            avatar.transform.position = Vector3.Lerp(origin, transform.position, Mathf.SmoothStep(0, 1, t)) +
                Vector3.up * (Mathf.Sin(t * Mathf.PI) * table.summonJumpHeight);
            if (avatar.Plane != null)
            {
                avatar.Plane.localScale = table.planeScale * Mathf.SmoothStep(0, 1, Mathf.Clamp01((t - .15f) / .65f));
                avatar.Plane.localPosition = Vector3.back * (.3f * (1 - t));
            }
            if (t >= 1) { boarding = false; avatar.transform.localPosition = Vector3.zero; avatar.Play(CatAnimation.SittingIdle, false); }
        }
        else avatar.transform.localPosition = Vector3.up * (Mathf.Sin(Time.time * 2) * .025f);
        if (owner != null && owner.Object != null && owner.Object.IsValid)
        {
            if (!boarding && owner.CatKillSequence != lastKill)
            { avatar.Play(CatAnimation.Cheer); reactAfter = Time.unscaledTime + 2f; }
            else if (!boarding && owner.CatHitSequence != lastHit && Time.unscaledTime >= reactAfter)
            { avatar.Play(CatAnimation.Grooming); reactAfter = Time.unscaledTime + 1.5f; }
            lastHit = owner.CatHitSequence; lastKill = owner.CatKillSequence;
        }
    }
}
