using Fusion;
using UnityEngine;

public partial class Player : IInputAuthorityGained
{
    [Networked] public NetworkString<_32> DeathMatchMigrationIdentity { get; private set; }

    public void InputAuthorityGained()
    {
        if (!networkSpawned || Object == null || !Object.HasInputAuthority) return;
        LocalPlayer = this;
        Object.ForceRemoteRenderTimeframe = !SimulatesMovement;
        if (GetComponent<DrowningWarning>() == null) gameObject.AddComponent<DrowningWarning>();
        var cameraManager = CameraManager.Instance;
        if (cameraManager != null)
        {
            localCameraFollow = cameraManager.GetComponent<CameraFollow>();
            cameraManager.SetTarget(gameObject);
        }
        Cursor.lockState = CombatPresentation.MenuOpen ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = CombatPresentation.MenuOpen;
    }

    public void ResetDeathMatchMigrationTargets()
    {
        if (Object == null || !Object.HasStateAuthority) return;
        ClearLockTargetInternal();
        CancelBladeRain();
        ChannelMagic = MagicType.None;
        ChannelTargetId = default;
        castTimer = TickTimer.None;
        pendingMagic = MagicType.None;
        previousButtons = default;
        lockOnTurnSpeed = Mathf.Max(0f, GetBroomStats(Broom).lockOnTurnSpeed);
    }
}
