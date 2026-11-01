using System;
using Fusion;
using UnityEngine;

[Serializable] public class OpenSpaceFish
{
    public string name, description;
    public Sprite icon;
    public Color color = Color.cyan;
    public int weight = 25, coins = 5;
}
[Serializable] public class OpenSpaceCatMotion
{
    public string name;
    public CatAnimation animation;
    public int weight = 1;
    public float duration = 3;
}
[CreateAssetMenu(menuName = "WitchFlight/Open Space Settings")]
public sealed class OpenSpaceSettings : ScriptableObject
{
    public const int Capacity = 20, SeatCount = 22;
    public NetworkObject avatarPrefab;
    public NetworkRunner runnerPrefab;
    public float walkSpeed = 4, interactionDistance = 2, castSeconds = 1.5f;
    public Vector2 waitSeconds = new Vector2(15, 25);
    public float biteSeconds = 6, resultSeconds = 2;
    public bool enableCoinRewards;
    public float nameDistance = 14, chatCooldown = 2, emoteCooldown = 5;
    public AudioClip purr, bite, catchSound;
    public AnimationClip idle, walk, greet;
    public OpenSpaceFish[] fish = {
        new() { name="은빛 멸치", description="햇살 아래 은빛으로 반짝이는 작은 멸치.", color=new Color(.7f,.85f,.95f) },
        new() { name="청록 고등어", description="바다의 청록빛을 닮은 고등어.", color=new Color(.15f,.7f,.65f) },
        new() { name="통통 복어", description="작고 동그란 몸으로 물살을 헤엄치는 복어.", color=new Color(1,.8f,.3f) },
        new() { name="별무늬 도미", description="비늘에 작은 별무늬가 빛나는 도미.", weight=11, coins=10, color=new Color(1,.5f,.7f) },
        new() { name="달빛 가오리", description="달빛 같은 지느러미로 바다를 미끄러지는 가오리.", weight=11, coins=10, color=new Color(.65f,.6f,1) },
        new() { name="무지개 날치", description="수면 위로 뛰어오르면 무지갯빛이 반짝이는 희귀한 날치.", weight=3, coins=25, color=new Color(.5f,1,.8f) }
    };
    public OpenSpaceCatMotion[] motions = {
        new() { name="만세", animation=CatAnimation.Cheer },
        new() { name="기지개", animation=CatAnimation.Yawn },
        new() { name="앞발 꾹꾹이", animation=CatAnimation.Knead },
        new() { name="꼬리 쫓기", animation=CatAnimation.Walk },
        new() { name="고개 갸웃", animation=CatAnimation.SittingIdle },
        new() { name="행복한 폴짝", animation=CatAnimation.Jump }
    };
    public int PickFish(float unit) => OpenSpaceProgress.WeightedIndex(Array.ConvertAll(fish, f => f.weight), unit);
    public int PickMotion(float unit) => OpenSpaceProgress.WeightedIndex(Array.ConvertAll(motions, f => f.weight), unit);
}

public enum OpenSpaceAction { Forward, Backward, Left, Right, Interact, Emote }
public static class OpenSpaceControls
{
    static readonly KeyCode[] Defaults = { KeyCode.W, KeyCode.S, KeyCode.A, KeyCode.D, KeyCode.E, KeyCode.T };
    static readonly string[] PreferenceKeys={"OpenSpace.Key.Forward","OpenSpace.Key.Backward","OpenSpace.Key.Left","OpenSpace.Key.Right","OpenSpace.Key.Interact","OpenSpace.Key.Emote"};
    static KeyCode[] cachedKeys;
    public static KeyCode Key(OpenSpaceAction action)
    {
        if(cachedKeys==null)
        {cachedKeys=new KeyCode[Defaults.Length];for(int i=0;i<cachedKeys.Length;i++)cachedKeys[i]=(KeyCode)PlayerPrefs.GetInt(PreferenceKeys[i],(int)Defaults[i]);}
        return cachedKeys[(int)action];
    }
    public static bool Held(OpenSpaceAction action) => Input.GetKey(Key(action));
    public static bool Down(OpenSpaceAction action) => Input.GetKeyDown(Key(action));
    public static bool Assign(OpenSpaceAction action, KeyCode key)
    {
        if (key == KeyCode.None || key == KeyCode.Escape || key == KeyCode.Return || key >= KeyCode.Mouse0) return false;
        foreach (OpenSpaceAction other in Enum.GetValues(typeof(OpenSpaceAction)))
            if (other != action && Key(other) == key) return false;
        PlayerPrefs.SetInt(PreferenceKeys[(int)action], (int)key);cachedKeys=null; PlayerPrefs.Save(); return true;
    }
    public static void Reset() { foreach (string key in PreferenceKeys) PlayerPrefs.DeleteKey(key);cachedKeys=null; PlayerPrefs.Save(); }
}
