using UnityEngine;

public static class EnvironmentImpactVfx
{
    private static GameObject prefab;

    public static bool IsEnvironment(Collider collider)
    {
        return collider != null && !collider.isTrigger &&
            collider.GetComponentInParent<Player>() == null &&
            collider.GetComponentInParent<MagicProjectile>() == null &&
            collider.GetComponentInParent<BattleFlag>() == null &&
            collider.GetComponentInParent<BattleItemPickup>() == null;
    }

    public static void Play(Vector3 point, Vector3 normal, Player caster)
    {
        BattleAudio.Play(BattleSound.EnvironmentHit, point);
        if (prefab == null) prefab = Resources.Load<GameObject>("EnvironmentImpact_RedShoot");
        if (prefab == null) return;
        Vector3 direction = normal.sqrMagnitude > .0001f ? normal.normalized : Vector3.up;
        var view = CombatPresentation.InstantiateVfx(prefab, point + direction * .02f,
            Quaternion.LookRotation(direction), null, caster);
        RearThreatCamera.ExcludeLocalEffect(view, caster);
        if (view != null) MagicVfxPool.Release(view, 4f);
    }
}
