#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public sealed class CharacterShadowWindow : EditorWindow
{
    private sealed class Entry
    {
        public Material material;
        public readonly List<string> users = new();
        public readonly List<string> parts = new();
        public string description;
        public int category;
    }
    private readonly List<Entry> entries = new();
    private Vector2 scroll;
    private int category;
    private static readonly string[] categories = { "전체", "캐릭터", "고양이", "기체" };
    private string search = "";
    private static readonly string[] shadowProperties = { "_ShadowReceive", "_Shadow2ndReceive", "_Shadow3rdReceive" };

    [MenuItem("Tools/WitchFlight/캐릭터 그림자 조절")]
    public static void Open()
    {
        var window = GetWindow<CharacterShadowWindow>("캐릭터 그림자");
        window.minSize = new Vector2(440, 320);
        window.Show();
    }
    private void OnEnable() { RefreshTargets(); Undo.undoRedoPerformed += Repaint; }
    private void OnDisable() { Undo.undoRedoPerformed -= Repaint; }

    private void RefreshTargets()
    {
        entries.Clear();
        var lookup = new Dictionary<Material, Entry>();
        AddPrefab("Assets/Ch/ChPrefab.prefab", lookup, 1);
        AddPrefab("Assets/Ch/WitchCat.prefab", lookup, 2);
        AddPrefab("Assets/Item/Robot.prefab", lookup, 3);
        AddPrefab("Assets/Item/Cleaner.prefab", lookup, 3);
        foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Weapon/Prefabs(usethis)/Broom" }))
            AddPrefab(AssetDatabase.GUIDToAssetPath(guid), lookup, 3);
        // Include every selectable cat skin, including material variants not currently equipped.
        foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets/Ch/cat/Material" }))
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
            var entry = AddMaterial(material, lookup, 2);
            if (entry == null) continue;
            string part = material.name.StartsWith("eye") ? "고양이 눈" : material.name == "Hat" ? "고양이 모자" : "고양이 털·몸통 (선택 가능한 외형)";
            if (!entry.parts.Contains(part)) entry.parts.Add(part);
        }
        foreach (var entry in entries)
            entry.description = "적용 부위: " + string.Join(" · ", entry.parts);
        entries.Sort((a, b) => string.CompareOrdinal(AssetDatabase.GetAssetPath(a.material), AssetDatabase.GetAssetPath(b.material)));
        Repaint();
    }
    private Entry AddMaterial(Material material, Dictionary<Material, Entry> lookup, int group)
    {
        if (material == null || (!material.HasProperty("_ShadowReceive") && !material.HasProperty("_ReceiveShadows"))) return null;
        if (lookup.TryGetValue(material, out var existing)) return existing;
        string path = AssetDatabase.GetAssetPath(material);
        if (path.Contains("/Broom/")) group = 3;
        if (path.Contains("/cat/")) group = 2;
        var entry = new Entry { material = material, category = group };
        lookup.Add(material, entry);
        entries.Add(entry);
        return entry;
    }
    private void AddPrefab(string assetPath, Dictionary<Material, Entry> lookup, int group)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
        if (prefab == null) return;
        foreach (var renderer in prefab.GetComponentsInChildren<Renderer>(true))
        foreach (var material in renderer.sharedMaterials)
        {
            string path = AnimationUtility.CalculateTransformPath(renderer.transform, prefab.transform);
            int partGroup = path.Contains("Brooms") ? 3 : group;
            var entry = AddMaterial(material, lookup, partGroup);
            if (entry == null) continue;
            string location = prefab.name + "/" + path;
            if (!entry.users.Contains(location)) entry.users.Add(location);
            string part = entry.category == 1 ? DescribePart(renderer.name, path) :
                categories[entry.category] + " / " + prefab.name + " / " + renderer.name;
            if (!entry.parts.Contains(part)) entry.parts.Add(part);
        }
    }
    private static string DescribePart(string name, string path)
    {
        switch (name.ToLowerInvariant())
        {
            case "body": return "얼굴·눈 및 얼굴 부속 메시";
            case "body_b": return "몸통·팔다리 피부";
            case "hair_front_rurune": return "앞머리";
            case "hair_back_rurune": return "뒷머리";
            case "hair_base_rurune": return "머리카락 기본 부분";
            case "hair_side_rurune": return "옆머리";
            case "hair": case "hair_2": return "머리카락 (" + name + ")";
            case "sharktail": return "꼬리";
            case "tail_ribbon": return "꼬리 리본";
            case "pierce_rurune": return "피어싱 장식";
            case "brouse_rurune": return "블라우스 상의";
            case "vest_rurune": return "조끼";
            case "skirt_rurune": return "치마";
            case "pettipants_rurune": return "속바지";
            case "sox_rurune": return "양말";
            case "shoes_rurune": return "신발";
            case "bag_rurune": return "가방";
            case "belt_leg_rurune": return "다리 벨트";
            case "collarpins_rurune": return "옷깃 핀 장식";
            case "potion_rurune": return "의상에 달린 포션 장식";
            case "sortinghat": return "모자 (SortingHat)";
        }
        string lower = path.ToLowerInvariant();
        if (lower.Contains("hat")) return "모자 구성 메시 (" + name + ")";
        if (lower.Contains("wand")) return "지팡이 구성 메시 (" + name + ")";
        if (lower.Contains("broom")) return "기체 구성 메시 (" + name + ")";
        return "연결 오브젝트: " + name;
    }
    private void OnGUI()
    {
        EditorGUILayout.LabelField("머티리얼별 그림자 수신 강도", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("0 = 외부 그림자 영향 없음 / 1 = 최대 반영\n원본 머티리얼에 즉시 적용됩니다. 같은 머티리얼을 공유하는 부위는 함께 변경됩니다. Ctrl+Z로 취소할 수 있습니다.", MessageType.Info);
        category = GUILayout.Toolbar(category, categories);
        search = EditorGUILayout.TextField("이름 / 부위 검색", search);
        scroll = EditorGUILayout.BeginScrollView(scroll);
        foreach (var entry in entries)
        {
            var material = entry.material;
            if (material == null || (category != 0 && entry.category != category)) continue;
            string users = string.Join("\n", entry.users);
            if (!string.IsNullOrEmpty(search) &&
                material.name.IndexOf(search, System.StringComparison.OrdinalIgnoreCase) < 0 &&
                entry.description.IndexOf(search, System.StringComparison.OrdinalIgnoreCase) < 0 &&
                users.IndexOf(search, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUI.DisabledScope(true))
                    EditorGUILayout.ObjectField(material, typeof(Material), false);
                EditorGUILayout.LabelField(entry.description, EditorStyles.wordWrappedLabel);
                EditorGUILayout.LabelField(new GUIContent("사용: " + string.Join(", ", entry.users.ConvertAll(p => p.Substring(p.LastIndexOf('/') + 1))), users), EditorStyles.wordWrappedMiniLabel);
                if (material.HasProperty("_ShadowReceive"))
                {
                float value = material.GetFloat("_ShadowReceive");
                bool mixed = false;
                foreach (var property in shadowProperties)
                    if (material.HasProperty(property) && !Mathf.Approximately(value, material.GetFloat(property))) mixed = true;
                EditorGUI.showMixedValue = mixed;
                EditorGUI.BeginChangeCheck();
                float next = EditorGUILayout.Slider("외부 그림자 수신", value, 0, 1);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(material, material.name + " 그림자 강도");
                    if (material.HasProperty("_UseShadow")) material.SetFloat("_UseShadow", 1f);
                    foreach (var property in shadowProperties)
                        if (material.HasProperty(property)) material.SetFloat(property, next);
                    EditorUtility.SetDirty(material);
                    SceneView.RepaintAll();
                    EditorApplication.QueuePlayerLoopUpdate();
                }
                EditorGUI.showMixedValue = false;
                }
                else
                {
                    EditorGUI.BeginChangeCheck();
                    bool receive = EditorGUILayout.Toggle("외부 그림자 수신", material.GetFloat("_ReceiveShadows") > .5f);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(material, material.name + " 그림자 수신");
                        material.SetFloat("_ReceiveShadows", receive ? 1f : 0f);
                        if (receive) material.DisableKeyword("_RECEIVE_SHADOWS_OFF");
                        else material.EnableKeyword("_RECEIVE_SHADOWS_OFF");
                        RefreshMaterial(material);
                    }
                    EditorGUILayout.HelpBox("URP Lit 부품: 그림자 수신 ON/OFF를 지원합니다. 이 셰이더에는 개별 그림자 농도·색 속성이 없습니다.", MessageType.Info);
                }
                if (material.HasProperty("_ShadowStrength"))
                {
                    EditorGUI.BeginChangeCheck();
                    float strength = EditorGUILayout.Slider(new GUIContent("그림자 농도", "그림자 자체의 적용 강도입니다. 높일수록 설정된 그림자 색이 강하게 반영됩니다."), material.GetFloat("_ShadowStrength"), 0, 1);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(material, material.name + " 그림자 농도");
                        material.SetFloat("_ShadowStrength", strength);
                        RefreshMaterial(material);
                    }
                }
                if (material.HasProperty("_ShadowColor"))
                {
                    EditorGUI.BeginChangeCheck();
                    Color color = EditorGUILayout.ColorField(new GUIContent("그림자 색", "색을 어둡게 설정하면 그림자 부분이 더 어두워집니다. 기존 알파값은 유지합니다."), material.GetColor("_ShadowColor"), true, false, false);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(material, material.name + " 그림자 색");
                        color.a = material.GetColor("_ShadowColor").a;
                        material.SetColor("_ShadowColor", color);
                        RefreshMaterial(material);
                    }
                }
            }
        }
        EditorGUILayout.EndScrollView();
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("대상 새로고침")) RefreshTargets();
            if (GUILayout.Button("머티리얼 저장"))
                foreach (var entry in entries)
                    if (entry.material != null) AssetDatabase.SaveAssetIfDirty(entry.material);
        }
        EditorGUILayout.LabelField($"캐릭터·고양이·기체 머티리얼 {entries.Count}개 (그림자 수신 지원)", EditorStyles.miniLabel);
    }
    private static void RefreshMaterial(Material material)
    {
        EditorUtility.SetDirty(material);
        SceneView.RepaintAll();
        EditorApplication.QueuePlayerLoopUpdate();
    }
}
#endif
