using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class AccountTitleUI : MonoBehaviour
{
    [SerializeField] private Transform titleChoicesRoot;
    [SerializeField] private GameObject popup;
    [SerializeField] private TMP_Text nameText, descriptionText, ownershipText, ownedCountText;
    [SerializeField] private Button equipButton;
    [Header("Title button backgrounds")]
    [SerializeField] private Sprite ownedTitleSprite;
    [SerializeField] private Sprite equippedTitleSprite;
    private readonly Dictionary<Button, Sprite> defaultSprites = new();
    private AccountProfileUI profile;
    private int selectedId, disabledFrame=-1;
    private readonly List<(Button button,UnityAction action)> listeners=new();
    private readonly List<(Button button,int id)> choices=new();
    private bool lastReady;
    private int lastEquippedId;
    private string lastProgressJson;

    private void OnEnable()
    {
        profile=GetComponent<AccountProfileUI>();
        titleChoicesRoot ??= profile!=null ? profile.TitleChoicesRoot : null;
        if(titleChoicesRoot==null)titleChoicesRoot=Find(transform,"ChinghoScrollView");
        if(popup==null)popup=Find(transform,"Popup")?.gameObject;
        if(equipButton==null)equipButton=profile!=null?profile.SharedConfirmButton:null;
        if(equipButton==null && popup!=null)equipButton=Find(popup.transform,"ConfirmButton")?.GetComponent<Button>();
        if(popup!=null) {
            nameText ??= Find(popup.transform,"NameText")?.GetComponent<TMP_Text>();
            descriptionText ??= Find(popup.transform,"ContentText")?.GetComponent<TMP_Text>();
            ownershipText ??= Find(popup.transform,"Have")?.GetComponent<TMP_Text>();
        }
        ownedCountText ??= Find(transform,"ChinghoCountText")?.GetComponent<TMP_Text>();
        BuildChoices();
        Bind(equipButton,ConfirmSelectedTitle);
        if(profile!=null)Bind(profile.ProfileCategoryButton,CancelSelection);
        // Category changes release the shared confirmation button back to profile UI.
        Transform profiles=Find(transform,"ProfileScrollView");
        if(profiles!=null)foreach(Button button in profiles.GetComponentsInChildren<Button>(true))Bind(button,CancelSelection);
        selectedId=0; disabledFrame=-1;
        DataConfig.Changed+=Refresh;
        Refresh();
    }
    private void OnDisable()
    {
        disabledFrame=Time.frameCount;
        DataConfig.Changed-=Refresh;
        foreach(var l in listeners)if(l.button!=null)l.button.onClick.RemoveListener(l.action);
        listeners.Clear();choices.Clear();
    }
    private void Update()
    {
        if(lastReady!=TitleProgression.Ready || lastEquippedId!=DataConfig.equippedTitleId ||
            lastProgressJson!=DataConfig.titleProgressJson)Refresh();
    }
    private void Bind(Button button,UnityAction action)
    { if(button==null)return;button.onClick.AddListener(action);listeners.Add((button,action)); }
    private void BuildChoices()
    {
        if(titleChoicesRoot==null)return;
        Transform content=Find(titleChoicesRoot,"Content") ?? titleChoicesRoot;
        var buttons=new List<Button>();
        foreach(Transform child in content) {var b=child.GetComponent<Button>();if(b!=null)buttons.Add(b);}
        if(buttons.Count==0) {Debug.LogWarning("칭호 목록 Content 아래에 Button 템플릿을 넣어주세요.",this);return;}
        while(buttons.Count<TitleCatalog.All.Count)buttons.Add(Instantiate(buttons[0],content));
        for(int i=0;i<buttons.Count;i++) {
            bool used=i<TitleCatalog.All.Count;buttons[i].gameObject.SetActive(used);if(!used)continue;
            var t=TitleCatalog.All[i];Button button=buttons[i];
            Image background=button.targetGraphic as Image ?? button.GetComponent<Image>();
            if(background!=null && !defaultSprites.ContainsKey(button))defaultSprites.Add(button,background.sprite);
            Set(button.GetComponentInChildren<TMP_Text>(true),t.name);
            int id=t.id;Bind(button,()=>SelectTitle(id));choices.Add((button,id));
        }
    }
    public void SelectTitle(int id)
    {
        if(!isActiveAndEnabled || TitleCatalog.Find(id)==null)return;
        profile?.BeginTitleSelection();selectedId=id;
        if(popup!=null) {
            popup.SetActive(true);
            foreach(var a in popup.GetComponentsInChildren<DOTweenAnimation>(true)) {
                if(!a.isActiveAndEnabled || !a.isActive || !a.isValid)continue;
                a.CreateTween(false,false);
                if(a.tween!=null && a.tween.IsActive())a.tween.SetUpdate(true).SetAutoKill(false).Restart();
            }
        }
        Refresh();
    }
    public void ConfirmSelectedTitle()
    {
        if(!isActiveAndEnabled && disabledFrame!=Time.frameCount || selectedId==0)return;
        if(!TitleProgression.Equip(selectedId)) {Refresh();return;}
        CancelSelection();
        Refresh();
        if(popup!=null)popup.SetActive(false);
    }
    public void CancelSelection()
    {
        selectedId=0;
        if(equipButton!=null)equipButton.interactable=true;
    }
    public void Refresh()
    {
        lastReady=TitleProgression.Ready;
        lastEquippedId=DataConfig.equippedTitleId;
        lastProgressJson=DataConfig.titleProgressJson;
        var p=lastReady?TitleProgression.Read():null;
        if(!lastReady)selectedId=0;
        Set(ownedCountText,$"{p?.unlocked.Count ?? 0}/{TitleCatalog.All.Count}");
        foreach(var c in choices) {
            if(c.button==null)continue;
            c.button.interactable=lastReady; // Locked titles remain inspectable.
            Image background=c.button.targetGraphic as Image ?? c.button.GetComponent<Image>();
            if(background!=null && defaultSprites.TryGetValue(c.button,out Sprite original))
            {
                bool unlocked=p?.Owns(c.id)??false;
                Sprite sprite=unlocked && c.id==lastEquippedId && equippedTitleSprite!=null ? equippedTitleSprite :
                    unlocked && ownedTitleSprite!=null ? ownedTitleSprite : original;
                background.sprite=sprite;
            }
        }
        if(selectedId==0)return;
        var t=TitleCatalog.Find(selectedId);bool owned=p?.Owns(selectedId)??false;
        Set(nameText,t.name);
        Set(descriptionText,t.hidden&&!owned?"히든 칭호입니다.":t.description);
        Set(ownershipText,owned?(DataConfig.equippedTitleId==selectedId?"장착 중":"보유"):"미보유");
        if(equipButton!=null)equipButton.interactable=owned;
    }
    private static void Set(TMP_Text text,string value) {if(text!=null){text.richText=false;text.text=value;}}
    internal static Transform Find(Transform root,string name)
    {
        foreach(Transform child in root.GetComponentsInChildren<Transform>(true))
            if(child.name.Replace(" ","").Equals(name,System.StringComparison.OrdinalIgnoreCase))return child;
        return null;
    }
}
