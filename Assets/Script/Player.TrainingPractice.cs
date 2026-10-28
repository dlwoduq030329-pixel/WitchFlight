using Fusion;
using UnityEngine;

public partial class Player
{
    public void PrepareTrainingPractice()
    {
        if (!CatTutorialDirector.TrainingSessionActive || Object == null || !Object.HasStateAuthority) return;
        SpawnProtectionPending = false;
        SpawnProtectionTimer = TickTimer.None;
    }

    public void SetTrainingPosition(Vector3 position, Quaternion rotation)
    {
        if (BattleManager.Instance == null || !BattleManager.Instance.IsTraining || Object == null || !Object.HasStateAuthority) return;
        bool wasEnabled = characterController != null && characterController.enabled;
        if (wasEnabled) characterController.enabled = false;
        transform.SetPositionAndRotation(position, rotation);
        GetComponent<NetworkTransform>()?.Teleport(position, rotation);
        currentPitch = NormalizePitch(rotation.eulerAngles.x); currentYaw = rotation.eulerAngles.y;
        Speed = 0f; SpeedStage = 0; KnockbackVelocity = Vector3.zero;
        if (!IsTestBot) { PortalExitRotation = rotation; PortalExitSequence++; }
        if (wasEnabled) characterController.enabled = true;
        controllerNeedsRenderReset = false;
    }
}
