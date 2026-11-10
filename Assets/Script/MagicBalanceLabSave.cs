using System;
using UnityEngine;

public static class MagicBalanceLabSave
{
    // Copy only controls exposed by the lab. Preserve icons, VFX, descriptions,
    // array order and new entries authored elsewhere while the lab is running.
    public static void CopyTuning(MagicStatTable magic, EquipmentStatTable equipment,
        MagicStatTable destinationMagic, EquipmentStatTable destinationEquipment)
    {
        destinationMagic.useIndividualHomingTurnSpeeds = magic.useIndividualHomingTurnSpeeds;
        destinationMagic.homingTurnSpeed = magic.homingTurnSpeed;
        destinationMagic.homingTurnReferenceSpeed = magic.homingTurnReferenceSpeed;
        destinationMagic.homingTurnSpeedExponent = magic.homingTurnSpeedExponent;
        destinationMagic.homingMaxTurnLossPerSecond = magic.homingMaxTurnLossPerSecond;
        destinationMagic.homingSpeedLossPerSecond = magic.homingSpeedLossPerSecond;
        destinationMagic.homingSpeedLossPerDegree = magic.homingSpeedLossPerDegree;
        destinationMagic.homingTravelDistanceMultiplier = magic.homingTravelDistanceMultiplier;
        destinationMagic.homingLifetimeMultiplier = magic.homingLifetimeMultiplier;
        destinationMagic.homingLeadSeconds = magic.homingLeadSeconds;
        destinationMagic.homingFallThreshold = magic.homingFallThreshold;
        for (int i = 0; i < destinationMagic.magics.Length; i++)
        {
            var value = destinationMagic.magics[i];
            int source = Array.FindIndex(magic.magics, x => x.magic == value.magic);
            if (source < 0) continue;
            var edited = magic.magics[source];
            value.projectileSpeed = edited.projectileSpeed;
            value.projectileTurnSpeed = edited.projectileTurnSpeed;
            value.range = edited.range;
            value.projectileLifetime = edited.projectileLifetime;
            value.damage = edited.damage;
            value.apCost = edited.apCost;
            value.cooldownSeconds = edited.cooldownSeconds;
            destinationMagic.magics[i] = value;
        }
        for (int i = 0; i < destinationEquipment.brooms.Length; i++)
        {
            var value = destinationEquipment.brooms[i];
            int source = Array.FindIndex(equipment.brooms, x => x.broom == value.broom);
            if (source < 0) continue;
            var edited = equipment.brooms[source];
            value.maxSpeed = edited.maxSpeed; value.turnSpeed = edited.turnSpeed;
            value.boostMultiplier = edited.boostMultiplier; value.maxHp = edited.maxHp;
            destinationEquipment.brooms[i] = value;
        }
        for (int i = 0; i < destinationEquipment.hats.Length; i++)
        {
            var value = destinationEquipment.hats[i];
            int source = Array.FindIndex(equipment.hats, x => x.hat == value.hat);
            if (source < 0) continue;
            value.maxAp = equipment.hats[source].maxAp;
            value.apRecoveryPerSecond = equipment.hats[source].apRecoveryPerSecond;
            destinationEquipment.hats[i] = value;
        }
    }

#if UNITY_EDITOR
    public static string SaveAssets(MagicStatTable magic, EquipmentStatTable equipment,
        MagicStatTable destinationMagic, EquipmentStatTable destinationEquipment)
    {
        string magicPath = UnityEditor.AssetDatabase.GetAssetPath(destinationMagic);
        string equipmentPath = UnityEditor.AssetDatabase.GetAssetPath(destinationEquipment);
        if (!magicPath.StartsWith("Assets/", StringComparison.Ordinal) ||
            !equipmentPath.StartsWith("Assets/", StringComparison.Ordinal))
            throw new InvalidOperationException("프로젝트 원본 데이터 테이블이 연결되지 않았습니다.");
        string backup = System.IO.Path.Combine("Library", "MagicBalanceLabBackups",
            DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff"));
        System.IO.Directory.CreateDirectory(backup);
        System.IO.File.Copy(magicPath, System.IO.Path.Combine(backup, "MagicStatTable.asset"));
        System.IO.File.Copy(equipmentPath, System.IO.Path.Combine(backup, "EquipmentStatTable.asset"));
        UnityEditor.Undo.RecordObjects(new UnityEngine.Object[] { destinationMagic, destinationEquipment }, "Apply balance lab tuning");
        CopyTuning(magic, equipment, destinationMagic, destinationEquipment);
        UnityEditor.EditorUtility.SetDirty(destinationMagic);
        UnityEditor.EditorUtility.SetDirty(destinationEquipment);
        UnityEditor.AssetDatabase.SaveAssetIfDirty(destinationMagic);
        UnityEditor.AssetDatabase.SaveAssetIfDirty(destinationEquipment);
        return "저장 완료 · 실제 배틀과 다음 Play에 적용됩니다.\n이전 파일 백업: " + backup;
    }
#endif
}
