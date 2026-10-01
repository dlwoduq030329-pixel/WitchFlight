using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Fusion;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Reads saved assets only: never opens scenes, saves assets, enters play mode or fixes values.
public static class WitchFlightBattleValidation
{
    private const string CharacterPath = "Assets/Ch/ChPrefab.prefab";
    private const string ProjectilePath = "Assets/MagicProjectile.prefab";
    private const string FlagPath = "Assets/FlagOBJ.prefab";
    private const string MainPath = "Assets/Scenes/Main.unity";
    private const string BattlePath = "Assets/Scenes/Battle.unity";
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    [MenuItem("Tools/WitchFlight/Validate Battle Setup")]
    public static void Validate()
    {
        var report = new Report();
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("Battle validation reads saved assets. Exit Play Mode and run it again.");
            return;
        }
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            if (scene.isDirty)
                report.Warn($"Unsaved scene '{scene.path}' is untouched; validation uses its saved version.");
        }
        try
        {
            EquipmentStatTable equipment = Load<EquipmentStatTable>("Assets/Resources/EquipmentStatTable.asset", report);
            MagicStatTable magic = Load<MagicStatTable>("Assets/Resources/MagicStatTable.asset", report);
            ValidateEquipment(equipment, report);
            ValidateMagic(magic, report);
            ValidatePrefabs(equipment, magic, report);
            ValidateScenes(report);
        }
        catch (Exception exception)
        {
            report.Check(false, $"Validation could not finish: {exception.Message}");
        }
        report.Print();
        if (Application.isBatchMode && report.Errors > 0)
            throw new BuildFailedException($"WitchFlight battle setup has {report.Errors} error(s).");
    }

    private static T Load<T>(string path, Report report) where T : Object
    {
        T asset = AssetDatabase.LoadAssetAtPath<T>(path);
        report.Check(asset != null, $"Missing {typeof(T).Name}: {path}");
        return asset;
    }

    private static void ValidateEquipment(EquipmentStatTable table, Report report)
    {
        if (table == null) return;
        report.Check(table.hats != null && table.hats.Length == 3, "Equipment table must define three hats.");
        report.Check(table.brooms != null && table.brooms.Length == 3, "Equipment table must define three brooms.");
        if (table.hats == null || table.brooms == null) return;
        report.Check(table.hats.Select(x => x.hat).OrderBy(x => x).SequenceEqual(
            new[] { HatType.Classic, HatType.Twisted, HatType.Elemental }), "Hat IDs must be Classic, Twisted, Elemental without duplicates.");
        report.Check(table.brooms.Select(x => x.broom).OrderBy(x => x).SequenceEqual(
            new[] { BroomType.Slow, BroomType.Standard, BroomType.Speed }), "Broom IDs must be Slow, Standard, Speed without duplicates.");
        foreach (HatStatEntry hat in table.hats)
            report.Check(Positive(hat.maxAp) && Positive(hat.apRecoveryPerSecond), $"{hat.hat}: mana capacity/recovery must be finite and positive.");
        foreach (BroomStatEntry broom in table.brooms)
            report.Check(Positive(broom.maxHp) && Positive(broom.maxSpeed) && Positive(broom.turnSpeed) &&
                Positive(broom.speedStageTransitionSpeed) && Positive(broom.brakeSpeed) && Positive(broom.turnAcceleration) &&
                Positive(broom.turnReturnSpeed) && broom.boostMultiplier >= 1f && Positive(broom.boostDuration),
                $"{broom.broom}: health, movement, turn, acceleration and boost settings must be valid.");
        for (int i = 0; i < table.hats.Length; i++)
            for (int j = i + 1; j < table.hats.Length; j++)
                report.Check((table.hats[i].maxAp - table.hats[j].maxAp) *
                    (table.hats[i].apRecoveryPerSecond - table.hats[j].apRecoveryPerSecond) < 0f,
                    $"Hats {table.hats[i].hat}/{table.hats[j].hat}: higher mana capacity must trade away recovery.");
        for (int i = 0; i < table.brooms.Length; i++)
            for (int j = i + 1; j < table.brooms.Length; j++)
            {
                BroomStatEntry a = table.brooms[i], b = table.brooms[j];
                report.Check((a.maxHp - b.maxHp) * (a.maxSpeed - b.maxSpeed) < 0f &&
                    (a.maxHp - b.maxHp) * (a.turnSpeed - b.turnSpeed) < 0f,
                    $"Brooms {a.broom}/{b.broom}: higher health must trade away speed and turning.");
            }
    }

    private static void ValidateMagic(MagicStatTable table, Report report)
    {
        if (table == null) return;
        report.Check(table.magics != null && table.magics.Length == 10, "Magic table must define all ten spells.");
        if (table.magics == null) return;
        report.Check(table.magics.Select(x => (int)x.magic).OrderBy(x => x).SequenceEqual(Enumerable.Range(1, 10)),
            "Magic IDs 1..10 must each occur once.");
        report.Check(Positive(table.parryApCost) && Positive(table.parryWindowSeconds) && NonNegative(table.parryCooldownSeconds),
            "Parry must have valid mana cost, window and cooldown.");
        foreach (MagicStatEntry spell in table.magics)
        {
            string name = spell.magic.ToString();
            report.Check(Positive(spell.apCost) && NonNegative(spell.damage) && NonNegative(spell.castSeconds) &&
                NonNegative(spell.cooldownSeconds) && NonNegative(spell.projectileSpeed), $"{name}: invalid mana/damage/timing/speed.");
            report.Check(!spell.requiresFullLock || spell.requiresTarget, $"{name}: full-lock requirement needs a target.");
            if (spell.requiresTarget)
                report.Check(Positive(spell.lockChargeSeconds) && Positive(spell.range), $"{name}: lock duration and range must be positive.");
            if (spell.projectileSpeed > 0f)
                report.Check(Positive(spell.projectileLifetime) && Positive(spell.projectileRadius), $"{name}: projectile lifetime/radius must be positive.");
            if (spell.effect == MagicEffectKind.AreaDamage || spell.effect == MagicEffectKind.Mine || spell.effect == MagicEffectKind.Scan)
                report.Check(Positive(spell.radius), $"{name}: area effect radius must be positive.");
            if (spell.effect == MagicEffectKind.Mine)
                report.Check(Positive(spell.placementDistance) && Positive(spell.activationDelay), "Mine needs travel distance and arming delay.");
            if (spell.effect == MagicEffectKind.Decoy)
                report.Check(Positive(spell.activationDelay) && spell.effectDuration > spell.activationDelay,
                    "Decoy's visible lifetime must outlast its short stealth interval.");
        }
        report.Check(table.magics.Any(x => x.requiresTarget) && table.magics.Any(x => !x.requiresTarget),
            "Both lock-on and non-lock-on spells must exist.");
        report.Check(table.GetStats(MagicType.Dark).movementMultiplier > 1f, "Wind must increase movement.");
        report.Check(table.GetStats(MagicType.Ice).movementMultiplier > 0f && table.GetStats(MagicType.Ice).movementMultiplier < 1f,
            "Ice must reduce turning with a multiplier between zero and one.");
    }

    private static void ValidatePrefabs(EquipmentStatTable equipment, MagicStatTable magic, Report report)
    {
        GameObject character = ValidateNetworkPrefab<Player>(CharacterPath, true, report);
        ValidateNetworkPrefab<MagicProjectile>(ProjectilePath, true, report);
        GameObject flag = ValidateNetworkPrefab<BattleFlag>(FlagPath, false, report);
        report.Check(flag == null || flag.GetComponent<Collider>() != null, "Flag prefab needs its pickup collider.");
        report.Check(AssetDatabase.LoadAssetAtPath<Shader>("Assets/Resources/CombatFade.shader") != null, "Combat fade shader is missing.");
        if (character == null) return;
        report.Check(character.GetComponent<CharacterController>() != null, "ChPrefab root needs CharacterController.");
        report.Check(character.GetComponent<CombatPresentation>() != null, "ChPrefab root needs CombatPresentation.");
        report.Check(character.GetComponent<enemyLockOn>() != null, "ChPrefab root needs enemyLockOn.");
        Player player = character.GetComponent<Player>();
        if (player == null) return;
        var serialized = new SerializedObject(player);
        report.Check(serialized.FindProperty("equipmentStatTable")?.objectReferenceValue == equipment && equipment != null,
            "Player must reference the editable EquipmentStatTable asset.");
        report.Check(serialized.FindProperty("magicStatTable")?.objectReferenceValue == magic && magic != null,
            "Player must reference the editable MagicStatTable asset.");
        FieldInfo field = typeof(Player).GetField("magicProjectilePrefab", BindingFlags.NonPublic | BindingFlags.Instance);
        NetworkPrefabRef reference = field != null ? (NetworkPrefabRef)field.GetValue(player) : default;
        report.Check(reference.IsValid && ((Guid)reference).ToString("N") == AssetDatabase.AssetPathToGUID(ProjectilePath),
            "Player.magicProjectilePrefab must reference MagicProjectile.prefab.");
        PlayerEquipment visualEquipment = character.GetComponent<PlayerEquipment>();
        report.Check(visualEquipment != null, "ChPrefab needs PlayerEquipment.");
        if (visualEquipment == null) return;
        var slots = new SerializedObject(visualEquipment);
        CheckSlots(slots.FindProperty("hatPrefabs"), 3, character.transform, "hat", report);
        CheckSlots(slots.FindProperty("broomPrefabs"), 3, character.transform, "broom", report);
        CheckSlots(slots.FindProperty("magicStaffPrefabs"), 10, character.transform, "magic", report);
    }

    private static GameObject ValidateNetworkPrefab<T>(string path, bool needsTransform, Report report) where T : NetworkBehaviour
    {
        GameObject prefab = Load<GameObject>(path, report);
        if (prefab == null) return null;
        NetworkObject network = prefab.GetComponent<NetworkObject>();
        T behaviour = prefab.GetComponent<T>();
        report.Check(network != null && behaviour != null, $"{path}: root NetworkObject/{typeof(T).Name} missing.");
        report.Check(!needsTransform || prefab.GetComponent<NetworkTransform>() != null, $"{path}: root NetworkTransform missing.");
        report.Check(AssetDatabase.GetLabels(prefab).Contains("FusionPrefab"), $"{path}: FusionPrefab label missing.");
        if (network != null)
        {
            SerializedProperty list = new SerializedObject(network).FindProperty("NetworkedBehaviours");
            var registered = new HashSet<Object>();
            if (list != null && list.isArray)
                for (int i = 0; i < list.arraySize; i++) registered.Add(list.GetArrayElementAtIndex(i).objectReferenceValue);
            foreach (NetworkBehaviour component in prefab.GetComponents<NetworkBehaviour>())
                report.Check(registered.Contains(component), $"{path}: NetworkObject list is missing {component.GetType().Name}.");
        }
        return prefab;
    }

    private static void CheckSlots(SerializedProperty slots, int count, Transform root, string name, Report report)
    {
        report.Check(slots != null && slots.isArray && slots.arraySize > count, $"{name} visual slots must include None plus {count} entries.");
        if (slots == null || !slots.isArray) return;
        for (int i = 1; i <= count && i < slots.arraySize; i++)
        {
            GameObject item = slots.GetArrayElementAtIndex(i).objectReferenceValue as GameObject;
            report.Check(item != null && item.transform.IsChildOf(root), $"{name} slot {i} must reference a pre-placed ChPrefab child.");
        }
    }

    private static void ValidateScenes(Report report)
    {
        string[] enabled = EditorBuildSettings.scenes.Where(x => x.enabled).Select(x => x.path).ToArray();
        report.Check(enabled.Length >= 2 && enabled[0] == MainPath && enabled[1] == BattlePath,
            "Enabled Build Settings scenes must start with Main (0), Battle (1).");
        var main = new SavedScene(MainPath);
        string network = main.Script("Assets/Script/NetworkGameManager.cs");
        report.Check(network != null, "Saved Main scene needs NetworkGameManager.");
        if (network != null)
        {
            report.Check(ReadFloat(network, "battleSceneIndex") == Array.IndexOf(enabled, BattlePath), "NetworkGameManager.battleSceneIndex differs from Build Settings.");
            float count = ReadFloat(network, "maxPlayerCount");
            report.Check(Positive(count), "Match player count must be positive.");
            if (count != 2f) report.Warn($"Main.maxPlayerCount is {count}; the design's 1v1 match uses 2 (1 is solo testing).");
            report.Check(ReadPrefabGuid(network, "playerPrefab") == AssetDatabase.AssetPathToGUID(CharacterPath), "Main playerPrefab does not reference ChPrefab.");
            string dataPath = AssetDatabase.GUIDToAssetPath(ReadPrefabGuid(network, "playerDataPrefab"));
            GameObject dataPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(dataPath);
            report.Check(dataPrefab != null && dataPrefab.GetComponent<PlayerData>() != null && dataPrefab.GetComponent<NetworkObject>() != null,
                "Main playerDataPrefab must reference networked PlayerData.");
        }
        var battle = new SavedScene(BattlePath);
        string manager = battle.Script("Assets/Script/vibe/BattleManager.cs");
        report.Check(manager != null, "Saved Battle scene needs BattleManager.");
        report.Check(battle.Script("Assets/Script/Camera/CameraFollow.cs") != null && battle.Script("Assets/Script/Camera/CameraManager.cs") != null,
            "Saved Battle scene needs CameraFollow and CameraManager.");
        if (manager == null) return;
        report.Check(ReadPrefabGuid(manager, "flagPrefab") == AssetDatabase.AssetPathToGUID(FlagPath), "BattleManager.flagPrefab does not reference FlagOBJ.");
        report.Check(Positive(ReadFloat(manager, "matchDurationSeconds")), "Match duration must be positive.");
        report.Check(Mathf.Approximately(ReadFloat(manager, "deathDespawnDelay"), 2f), "Death presentation/despawn delay must be 2 seconds.");
        report.Check(Mathf.Approximately(ReadFloat(manager, "respawnDelaySeconds"), 7f), "Respawn delay must be 7 seconds from death.");
        Matrix4x4 a = battle.Transform(ReadReference(manager, "playerSpawnA"));
        Matrix4x4 b = battle.Transform(ReadReference(manager, "playerSpawnB"));
        Matrix4x4 flag = battle.Transform(ReadReference(manager, "flagSpawnPoint"));
        Vector3 aPosition = a.MultiplyPoint3x4(Vector3.zero), bPosition = b.MultiplyPoint3x4(Vector3.zero);
        Vector3 center = flag.MultiplyPoint3x4(Vector3.zero);
        report.Check(Vector3.Distance(aPosition, bPosition) > 1f, "Team spawn positions must be separate.");
        report.Check(Vector3.Dot(a.MultiplyVector(Vector3.forward).normalized, (center - aPosition).normalized) > 0.5f,
            "Team A spawn must face toward the center flag.");
        report.Check(Vector3.Dot(b.MultiplyVector(Vector3.forward).normalized, (center - bPosition).normalized) > 0.5f,
            "Team B spawn must face toward the center flag.");
    }

    private static bool Positive(float value) => !float.IsNaN(value) && !float.IsInfinity(value) && value > 0f;
    private static bool NonNegative(float value) => !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0f;
    private static float ReadFloat(string body, string name)
    {
        Match match = Regex.Match(body, @"(?m)^  " + Regex.Escape(name) + @":\s*([^\r\n]+)");
        return match.Success && float.TryParse(match.Groups[1].Value, NumberStyles.Float, Invariant, out float result) ? result : float.NaN;
    }
    private static long ReadReference(string body, string name)
    {
        Match match = Regex.Match(body, @"(?m)^  " + Regex.Escape(name) + @": \{fileID: (-?\d+)");
        return match.Success ? long.Parse(match.Groups[1].Value, Invariant) : 0;
    }
    private static string ReadPrefabGuid(string body, string name)
    {
        Match match = Regex.Match(body, @"(?m)^  " + Regex.Escape(name) + @":\r?\n    RawGuidValue: ([a-fA-F0-9]{32})");
        return match.Success ? match.Groups[1].Value.ToLowerInvariant() : string.Empty;
    }

    private sealed class SavedScene
    {
        private readonly Dictionary<long, string> objects = new();
        public SavedScene(string path)
        {
            foreach (Match match in Regex.Matches(File.ReadAllText(path), @"(?ms)^--- !u!\d+ &(-?\d+)[^\n]*\n(.*?)(?=^--- !u!|\z)"))
                objects[long.Parse(match.Groups[1].Value, Invariant)] = match.Groups[2].Value;
        }
        public string Script(string scriptPath)
        {
            string guid = AssetDatabase.AssetPathToGUID(scriptPath);
            return objects.Values.FirstOrDefault(x => x.Contains("guid: " + guid + ","));
        }
        public Matrix4x4 Transform(long id, int depth = 0)
        {
            if (id == 0 || depth > 32 || !objects.TryGetValue(id, out string body) || !body.StartsWith("Transform:"))
                throw new InvalidOperationException($"Missing/invalid saved Transform fileID {id}.");
            Vector4 p = Vector(body, "m_LocalPosition", false), q = Vector(body, "m_LocalRotation", true), s = Vector(body, "m_LocalScale", false);
            float length = q.sqrMagnitude;
            if (!Positive(length) || Mathf.Abs(length - 1f) > 0.02f)
                throw new InvalidOperationException($"Spawn Transform {id} has an invalid quaternion.");
            Matrix4x4 local = Matrix4x4.TRS(new Vector3(p.x, p.y, p.z), new Quaternion(q.x, q.y, q.z, q.w), new Vector3(s.x, s.y, s.z));
            long parent = ReadReference(body, "m_Father");
            return parent == 0 ? local : Transform(parent, depth + 1) * local;
        }
        private static Vector4 Vector(string body, string name, bool quaternion)
        {
            Match match = Regex.Match(body, @"(?m)^  " + name + @": \{x: ([^,]+), y: ([^,]+), z: ([^,}\r\n]+)(?:, w: ([^}]+))?\}");
            if (!match.Success) throw new InvalidOperationException($"Missing vector {name}.");
            return new Vector4(float.Parse(match.Groups[1].Value, Invariant), float.Parse(match.Groups[2].Value, Invariant),
                float.Parse(match.Groups[3].Value, Invariant), quaternion ? float.Parse(match.Groups[4].Value, Invariant) : 0f);
        }
    }

    private sealed class Report
    {
        private readonly StringBuilder details = new();
        private int checks;
        private int warnings;
        public int Errors { get; private set; }
        public void Check(bool condition, string message)
        {
            checks++;
            if (condition) return;
            Errors++;
            details.AppendLine("ERROR: " + message);
        }
        public void Warn(string message) { warnings++; details.AppendLine("WARNING: " + message); }
        public void Print()
        {
            string result = $"WitchFlight saved battle setup: {checks} checks, {Errors} errors, {warnings} warnings. No assets/scenes changed.\n{details}";
            if (Errors > 0) Debug.LogError(result);
            else if (warnings > 0) Debug.LogWarning(result);
            else Debug.Log(result);
        }
    }
}
