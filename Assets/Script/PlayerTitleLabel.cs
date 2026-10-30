using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public sealed class PlayerTitleLabel : MonoBehaviour
{
    [Tooltip("프로필 카드 등 다른 플레이어의 칭호라면 해당 PlayerData를 지정합니다.")]
    [SerializeField] private PlayerData player;
    [SerializeField] private bool useLocalAccount = true;
    private TMP_Text label;
    private int shown = -1;
    private void Awake() {label=GetComponent<TMP_Text>();}
    private void OnEnable() {shown=-1;Refresh();}
    private void Update() => Refresh();
    private void Refresh()
    {
        int id=useLocalAccount?(TitleProgression.Ready?DataConfig.equippedTitleId:0):
            player!=null && player.Object!=null && player.Object.IsValid?player.EquippedTitleId:0;
        if(shown==id || label==null)return;
        shown=id;label.richText=false;label.text=TitleCatalog.Name(id);
    }
    public void SetPlayer(PlayerData data) {player=data;useLocalAccount=false;shown=-1;Refresh();}

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        SceneManager.sceneLoaded-=SceneLoaded;
        SceneManager.sceneLoaded+=SceneLoaded;
        InstallLabels();
    }
    private static void SceneLoaded(Scene scene,LoadSceneMode mode)=>InstallLabels();
    private static void InstallLabels()
    {
        foreach(var text in FindObjectsByType<TMP_Text>(FindObjectsInactive.Include,FindObjectsSortMode.None)) {
            if(!text.gameObject.scene.isLoaded || !text.name.Equals("chingho",System.StringComparison.OrdinalIgnoreCase))continue;
            bool award=false,remote=false;
            for(Transform p=text.transform.parent;p!=null;p=p.parent) {
                if(p.name=="MVPUI")award=true;
                if(p.name=="TabUI" || p.name=="VSUI" || p.name.Contains("WaitingRoom"))remote=true;
            }
            if(award)continue; // MVP award category is not an equipped account title.
            var label=text.GetComponent<PlayerTitleLabel>() ?? text.gameObject.AddComponent<PlayerTitleLabel>();
            if(remote)label.SetPlayer(null);
        }
    }
    // Called by the profile UI at its existing roster refresh, not by a global frame search.
    public static void Bind(Transform root,PlayerData data)
    {
        if(root==null)return;
        var group=root.GetComponent<PlayerTitleLabelGroup>() ?? root.gameObject.AddComponent<PlayerTitleLabelGroup>();
        group.Bind(data);
    }
}

public sealed class PlayerTitleLabelGroup : MonoBehaviour
{
    private PlayerTitleLabel[] labels;
    private PlayerData current;
    public void Bind(PlayerData data)
    {
        if(labels==null) {
            var list=new System.Collections.Generic.List<PlayerTitleLabel>();
            foreach(var text in GetComponentsInChildren<TMP_Text>(true))
                if(text.name.Equals("chingho",System.StringComparison.OrdinalIgnoreCase))
                    list.Add(text.GetComponent<PlayerTitleLabel>() ?? text.gameObject.AddComponent<PlayerTitleLabel>());
            labels=list.ToArray();
        }
        if(current==data && data!=null)return;
        current=data;foreach(var label in labels)if(label!=null)label.SetPlayer(data);
    }
}
