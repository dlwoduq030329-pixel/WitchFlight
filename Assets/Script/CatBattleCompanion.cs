using UnityEngine;

[DefaultExecutionOrder(250)]
public sealed class CatBattleCompanion : MonoBehaviour
{
    private Player owner;
    private PlayerEquipment equipment;
    private CatAvatar avatar;
    private int damage, hit, kill;
    private bool initialized;
    private float reactAfter;
    private GameObject seatedBroom;
    private Transform broomSeat;
    private bool ownsSeat;
    public Vector3 SeatPosition => avatar != null ? avatar.transform.position : transform.TransformPoint(new Vector3(0, 1, -.7f));

    private void Awake() { owner = GetComponent<Player>(); equipment = GetComponentInChildren<PlayerEquipment>(true); }

    private void LateUpdate()
    {
        var table = CatCompanionTable.Default;
        if (table == null || owner == null || owner.Object == null || !owner.Object.IsValid) return;
        var data = PlayerData.FindPerkData(owner.Runner, owner.ParticipantRef);
        if (data == null || data.Object == null || !data.Object.IsValid || !data.IsLoadoutInitialized) return;
        if (avatar == null) avatar = CatAvatar.Create(null, data.CatBodyIndex, data.CatEyeIndex, false);
        if (avatar == null) return;
        avatar.SetAppearance(data.CatBodyIndex, data.CatEyeIndex);
        var broom = equipment != null ? equipment.CurrentBroomObject : null;
        if (seatedBroom != broom) BindSeat(broom, table);
        if (broomSeat != null)
            avatar.transform.SetPositionAndRotation(broomSeat.position, broomSeat.rotation * Quaternion.Euler(table.broomSeatEuler));
        else
            avatar.transform.SetPositionAndRotation(transform.TransformPoint(table.broomSeatOffset), transform.rotation * Quaternion.Euler(table.broomSeatEuler));
        bool summoned = false;
        if (table.useCatForSummon) foreach (var projectile in MagicProjectile.ActiveProjectiles)
            if (projectile != null && projectile.IsLiveSummon && projectile.Runner == owner.Runner && projectile.Shooter == owner.ParticipantRef)
            { summoned = true; break; }
        avatar.SetVisible(owner.IsAlive && !summoned && !owner.IsHiddenBySmokeFor(Player.LocalPlayer));
        if (!initialized)
        {
            damage = owner.HitSequence; hit = owner.CatHitSequence; kill = owner.CatKillSequence; initialized = true;
        }
        if (owner.CatKillSequence != kill) { avatar.Play(CatAnimation.Cheer); reactAfter = Time.unscaledTime + 2f; }
        else if (owner.HitSequence != damage && Time.unscaledTime >= reactAfter)
        { avatar.Play(CatAnimation.Jump); reactAfter = Time.unscaledTime + .6f; }
        else if (owner.CatHitSequence != hit && Time.unscaledTime >= reactAfter)
        { avatar.Play(CatAnimation.Grooming); reactAfter = Time.unscaledTime + 1.5f; }
        damage = owner.HitSequence; hit = owner.CatHitSequence; kill = owner.CatKillSequence;
    }
    private void BindSeat(GameObject broom, CatCompanionTable table)
    {
        if (ownsSeat && broomSeat != null) Destroy(broomSeat.gameObject);
        seatedBroom = broom; broomSeat = null; ownsSeat = false;
        if (broom == null) return;
        foreach (var child in broom.GetComponentsInChildren<Transform>(true))
            if (child.name == "CatSeat") { broomSeat = child; return; }
        if (!table.autoBroomSeat) return;
        // Store the attachment in the actual animated broom, so bob/pitch/roll stay together.
        // The left hand can still be in takeoff/attack pose when this first runs.
        // Define the seat in the broom's frame, never freeze that temporary player-relative pose.
        Quaternion rotation = broom.transform.rotation * Quaternion.Euler(table.broomSeatingFrameEuler);
        // The broom is skinned. Its authored bounds are a padded 2m cube, not its surface.
        if (!CatSeatGeometry.TryBounds(broom.transform, broom.transform.position, rotation, out Bounds bounds)) return;
        float x = bounds.center.x, z = Mathf.Lerp(bounds.min.z, bounds.max.z, table.broomRearSeatRatio);
        float y = CatSeatGeometry.SurfaceHeight(broom.transform, broom.transform.position, rotation, x, z, bounds.max.y);
        broomSeat = new GameObject("CatSeat (automatic surface)").transform;
        broomSeat.SetParent(broom.transform, false);
        broomSeat.SetPositionAndRotation(broom.transform.position + rotation * (new Vector3(x, y, z) + table.broomSurfaceOffset), rotation);
        ownsSeat = true;
    }
    private void OnDisable() { if (avatar != null) avatar.SetVisible(false); initialized = false; }
    private void OnDestroy() { if (avatar != null) Destroy(avatar.gameObject); if (ownsSeat && broomSeat != null) Destroy(broomSeat.gameObject); }
}
