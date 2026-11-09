using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

// Binds only designer-authored controls. Hierarchy traversal happens once in Awake.
public sealed class EmoteLoadoutUI : MonoBehaviour
{
    [SerializeField] private Transform contentRoot;
    [SerializeField] private Button[] slotButtons;
    [SerializeField] private Image[] slotIcons;
    [SerializeField] private Button equipButton;
    [SerializeField] private GameObject lobbyCharacter;
    private TMP_Text equipLabel;
    private int selectedSlot = -1, pendingId;
    private bool lastEditable;
    private Color[] slotColors;
    private UnityAction[] slotActions;
    private readonly List<Choice> choices = new();
    private static readonly string[] DirectionNames = { "위", "오른쪽", "아래", "왼쪽" };
    private sealed class Choice
    {
        public int id;
        public Button button;
        public Image icon;
        public GameObject check;
        public UnityAction action;
    }
    private bool Editable => DatabaseManager.Instance != null && DatabaseManager.Instance.CanModifyAccountData;
    private void Awake()
    {
        slotColors = new Color[slotButtons.Length]; slotActions = new UnityAction[slotButtons.Length];
        for (int i=0;i<slotButtons.Length;i++)
        {
            int slot=i; slotActions[i]=()=>SelectSlot(slot);
            if(slotButtons[i]==null) continue;
            slotColors[i]=slotButtons[i].image!=null?slotButtons[i].image.color:Color.white;
            slotButtons[i].onClick.AddListener(slotActions[i]);
        }
        var entries = new List<EmoteEntry>();
        if (EmoteTable.Default != null)
            foreach (var entry in EmoteTable.Default.Entries)
                if (entry != null && entry.id > 0) entries.Add(entry);
        entries.Sort((a,b)=>a.id.CompareTo(b.id));
        int index=0;
        foreach(Transform row in contentRoot)
            foreach(Transform child in row)
            {
                var button=child.GetComponent<Button>();
                if(button==null) continue;
                var choice=new Choice { id=index<entries.Count?entries[index].id:0,button=button,
                    icon=child.Find("Image")?.GetComponent<Image>(),check=child.Find("Check")?.gameObject };
                index++;
                choice.action=()=>SelectEmote(choice.id);
                button.onClick.AddListener(choice.action); choices.Add(choice);
            }
        if(equipButton!=null)
        {
            equipButton.onClick.AddListener(Equip);
            equipLabel=equipButton.GetComponentInChildren<TMP_Text>(true);
        }
    }
    private void OnEnable() { DataConfig.Changed+=Refresh; DataConfig.InventoryChanged+=Refresh; DataConfig.SnapshotApplied+=Refresh; Refresh(); }
    private void OnDisable()
    {
        DataConfig.Changed-=Refresh;
        DataConfig.InventoryChanged-=Refresh;
        DataConfig.SnapshotApplied-=Refresh;
        selectedSlot=-1; pendingId=0; Refresh();
        if (lobbyCharacter != null && gameObject.scene.isLoaded)
            lobbyCharacter.SetActive(true);
    }
    private void Update() { if(lastEditable!=Editable) Refresh(); }
    public void SelectSlot(int slot)
    {
        if(slot<0 || slot>=4) return;
        selectedSlot=slot; pendingId=0; Refresh();
    }
    public void SelectEmote(int id)
    {
        if(selectedSlot<0 || EmoteTable.Default==null || !EmoteTable.Default.TryGet(id,out _) || !ItemInventory.OwnsEmote(id)) return;
        pendingId=id; Refresh();
    }
    public void Equip()
    {
        if(!Editable || selectedSlot<0 || pendingId<=0) return;
        DataConfig.SetEmote(selectedSlot,pendingId);
        Refresh();
    }
    private void Refresh()
    {
        lastEditable=Editable;
        var table=EmoteTable.Default;
        foreach(var choice in choices)
        {
            bool valid=table!=null && table.TryGet(choice.id,out _);
            bool owned=valid && ItemInventory.OwnsEmote(choice.id);
            choice.button.interactable=owned && selectedSlot>=0;
            if(choice.icon!=null)
            {
                choice.icon.sprite=valid && table.TryGet(choice.id,out var entry)?entry.icon:null;
                choice.icon.enabled=choice.icon.sprite!=null;
                choice.icon.color=owned?Color.white:new Color(1f,1f,1f,.35f);
            }
            bool equipped=false;
            for(int i=0;i<4;i++) equipped|=DataConfig.GetEmote(i)==choice.id;
            if(choice.check!=null) choice.check.SetActive(owned && equipped);
        }
        for(int i=0;i<slotButtons.Length && i<4;i++)
        {
            if(slotButtons[i]!=null && slotButtons[i].image!=null)
                slotButtons[i].image.color=i==selectedSlot?new Color(.267f,.863f,.8f):slotColors[i];
            if(i<slotIcons.Length && slotIcons[i]!=null && table!=null && table.TryGet(DataConfig.GetEmote(i),out var entry))
                slotIcons[i].sprite=entry.icon;
        }
        if(equipButton!=null) equipButton.interactable=Editable && selectedSlot>=0 && pendingId>0 && ItemInventory.OwnsEmote(pendingId) && DataConfig.GetEmote(selectedSlot)!=pendingId;
        if(equipLabel!=null)
        {
            string label=selectedSlot<0?"현재 슬롯에 장착":DirectionNames[selectedSlot]+" 슬롯에 장착";
            if(selectedSlot>=0 && table!=null && table.TryGet(pendingId,out var pending))
                label=DirectionNames[selectedSlot]+" · "+pending.displayName+" 장착";
            equipLabel.text=label;
        }
    }
    private void OnDestroy()
    {
        DataConfig.Changed-=Refresh;
        foreach(var choice in choices) if(choice.button!=null) choice.button.onClick.RemoveListener(choice.action);
        for(int i=0;i<slotButtons.Length;i++) if(slotButtons[i]!=null && slotActions!=null) slotButtons[i].onClick.RemoveListener(slotActions[i]);
        if(equipButton!=null) equipButton.onClick.RemoveListener(Equip);
    }
}
