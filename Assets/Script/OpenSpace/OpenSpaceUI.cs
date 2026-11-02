using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[Serializable] public sealed class OpenSpaceRow
{
    public GameObject root;
    public Button button;
    public TMP_Text text;
    public Image icon;
}
public enum OpenSpacePanel { None, Target, Outfit, Cat, Bag, Collection, Feed, Motions, Menu, Help, Settings, Emotes }
public sealed class OpenSpaceUI : MonoBehaviour
{
    [SerializeField] private OpenSpaceSettings settings;
    [SerializeField] private OpenSpaceWorld world;
    [Header("Authored HUD")]
    [SerializeField] private TMP_Text status, money, prompt, notice, fishStatus;
    [SerializeField] private GameObject fishingHUD;
    [SerializeField] private Button fishStart,fishHook,fishCancel,fishLeave;
    [Header("Replaceable activity panel")]
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private TMP_Text heading,body;
    [SerializeField] private OpenSpaceRow[] rows;
    [SerializeField] private Button primary,secondary;
    [SerializeField] private TMP_Text primaryText,secondaryText;
    [Header("Confirmation")]
    [SerializeField] private GameObject confirmation;
    [SerializeField] private TMP_Text confirmationText;
    [Header("Chat")]
    [SerializeField] private TMP_InputField chatInput;
    [SerializeField] private TMP_Text chatHistory;
    [SerializeField] private ScrollRect chatScroll;
    [SerializeField] private AudioSource activityAudio;
    private OpenSpacePanel panel;
    private Action confirmed;
    private readonly List<(string id,string name,string text)> chat=new();
    private readonly StringBuilder chatText=new();
    private int selectedFish,blockPage,lastCompositionFrame=-100,bindKey=-1;
    private float nextRefresh,noticeUntil,nextChat;
    private string submittedCatch, inspectedName, targetId,targetName;
    private bool saving;
    private OpenSpaceFishingPhase lastPhase;
    public bool ChatFocused=>chatInput!=null&&chatInput.isFocused;
    public bool CapturingKey=>bindKey>=0;
    public bool BlocksMovement=>confirmation.activeSelf||CapturingKey||panel!=OpenSpacePanel.None&&panel!=OpenSpacePanel.Cat;
    public bool BlocksPointer=>confirmation.activeSelf||CapturingKey||panel!=OpenSpacePanel.None&&panel!=OpenSpacePanel.Cat&&panel!=OpenSpacePanel.Outfit;
    private DatabaseManager DB=>DatabaseManager.Instance;
    private OpenSpaceAvatar Local=>OpenSpaceAvatar.Local;
    private void Start()
    {
        panelRoot.SetActive(false);confirmation.SetActive(false);fishingHUD.SetActive(false);
        chatInput.characterLimit=100;chatInput.textComponent.richText=false;chatHistory.richText=false;
        chatInput.lineType=TMP_InputField.LineType.MultiLineNewline;
        activityAudio.outputAudioMixerGroup=GameAudioSettings.Group(GameAudioCategory.SFX);
        foreach(var row in rows)row.text.richText=false;
        body.richText=false;
    }
    private void Update()
    {
        if(!string.IsNullOrEmpty(Input.compositionString))lastCompositionFrame=Time.frameCount;
        if(Time.unscaledTime<nextRefresh)return;nextRefresh=Time.unscaledTime+.15f;
        var session=OpenSpaceSession.Instance;
        Set(status,session==null?"메인 씬에서 OpenSpace 버튼으로 입장해주세요.":session.Status+
            (session.Connected?"  ·  "+session.Runner.SessionInfo.PlayerCount+" / 20":""));
        Set(money,DB!=null&&DB.HasLoadedProfile?DataConfig.money.ToString("N0"):"");
        if(Time.unscaledTime>noticeUntil&&!saving)Set(notice,DB!=null&&DB.HasOpenSpacePending?"저장 결과 확인이 필요합니다. ‘결과 확인 / 재시도’를 눌러주세요.":"");
        primary.interactable=secondary.interactable=!saving&&!CapturingKey;
        if(panel==OpenSpacePanel.Bag) primary.interactable&=DB.OpenSpaceData.bag[selectedFish]>0&&string.IsNullOrEmpty(DB.OpenSpaceData.collectedUtc[selectedFish]);
        if(panel==OpenSpacePanel.Feed) primary.interactable&=DB.OpenSpaceData.bag[selectedFish]>0;
        if(Local==null){Set(prompt,"");return;}
        string key=OpenSpaceControls.Key(OpenSpaceAction.Interact).ToString();
        Set(prompt,world.PromptCat?"["+key+"] 내 고양이와 교감":world.PromptSeat>=0?"["+key+"] 낚시 자리 선택":"");
        var hub=OpenSpaceHub.Instance;int seatIndex=hub!=null?hub.SeatOf(Local.Object.InputAuthority):-1;
        fishingHUD.SetActive(seatIndex>=0);
        if(seatIndex>=0)
        {
            var seat=hub.Seats[seatIndex];
            string text=seat.phase==OpenSpaceFishingPhase.Ready?"무료 낚싯대 · 한 번에 한 마리씩 낚아요.":
                seat.phase==OpenSpaceFishingPhase.Casting?"찌를 던지고 있어요…":seat.phase==OpenSpaceFishingPhase.Waiting?"물고기를 기다리는 중 · 취소해도 손실이 없어요.":
                seat.phase==OpenSpaceFishingPhase.Bite?"입질! ["+key+"] 또는 챔질 버튼 · "+Mathf.CeilToInt(seat.timer.RemainingTime(hub.Runner)??0)+"초":
                seat.phase==OpenSpaceFishingPhase.Settling?"획득 결과 확인 중…":seat.phase==OpenSpaceFishingPhase.Result?
                settings.fish[Mathf.Clamp(seat.fish,0,5)].name+" +1 · 가방 저장 완료":"물고기가 떠났어요. 다시 낚을 수 있어요.";
            Set(fishStatus,text);
            fishStart.interactable=!saving&&!DB.HasOpenSpacePending&&(seat.phase==OpenSpaceFishingPhase.Ready||seat.phase==OpenSpaceFishingPhase.Result||seat.phase==OpenSpaceFishingPhase.Missed);
            fishHook.interactable=seat.phase==OpenSpaceFishingPhase.Bite;
            fishCancel.interactable=fishLeave.interactable=!saving&&seat.phase!=OpenSpaceFishingPhase.Settling;
            if(seat.phase==OpenSpaceFishingPhase.Bite&&lastPhase!=seat.phase&&settings.bite!=null)activityAudio.PlayOneShot(settings.bite);
            lastPhase=seat.phase;
            if(seat.phase==OpenSpaceFishingPhase.Settling&&!saving&&submittedCatch!=seat.attempt.ToString())
            {submittedCatch=seat.attempt.ToString();CommitCatch(seat);}
        }
        if(panel==OpenSpacePanel.Target&&world.Selected==null)Set(body,targetName+"님은 광장에서 나갔어요.");
    }
    private static void Set(TMP_Text text,string value){if(text.text!=value)text.text=value;}
    public void Notify(string message){Set(notice,message);noticeUntil=Time.unscaledTime+4;}
    private void Show(OpenSpacePanel next,string title,string description)
    {
        if(saving)return;
        if(next!=OpenSpacePanel.Outfit&&world.Inspecting)world.ClosePreview();
        panel=next;heading.text=title;body.text=description;panelRoot.SetActive(next!=OpenSpacePanel.None);
        foreach(var row in rows)row.root.SetActive(false);
        primary.gameObject.SetActive(false);secondary.gameObject.SetActive(false);
    }
    private void Row(int index,string text,Sprite icon=null,bool enabled=true)
    {
        rows[index].root.SetActive(true);rows[index].text.text=text;rows[index].button.interactable=enabled;
        rows[index].icon.sprite=icon;rows[index].icon.enabled=icon!=null;
    }
    private void Actions(string first,string second="")
    {primaryText.text=first;secondaryText.text=second;primary.gameObject.SetActive(!string.IsNullOrEmpty(first));secondary.gameObject.SetActive(!string.IsNullOrEmpty(second));}
    public void OpenTarget()
    {
        var target=world.Selected;if(target==null)return;
        targetId=target.PublicId.ToString();targetName=target.Nickname.ToString();
        Show(OpenSpacePanel.Target,targetName,TitleCatalog.Name(target.Title));Actions("코디 보기","차단");
    }
    public void OpenOwnOutfit(){if(Local!=null&&Local.Activity==OpenSpaceActivity.Walking)Inspect(Local);}
    private void Inspect(OpenSpaceAvatar avatar)
    {
        inspectedName=avatar.Nickname.ToString();
        var c=avatar.Appearance;
        Show(OpenSpacePanel.Outfit,inspectedName+"의 코디",TitleCatalog.Name(avatar.Title)+"\n"+
            CatCompanionTable.Default.GetCatName(avatar.CatBody)+"\n헤어 "+c.hairStylePreset+" · 모자 "+((HatType)c.hatIndex)+
            "\n머리 #"+ColorUtility.ToHtmlStringRGB(c.hairColor)+"\n의상 #"+ColorUtility.ToHtmlStringRGB(c.clothColor)+"\n눈 #"+ColorUtility.ToHtmlStringRGB(c.eyeColor)+
            "\n우클릭 드래그로 회전 · 휠로 확대/축소");
        world.Inspect(avatar);Actions("시점 초기화");
    }
    public void OpenCat()
    {
        if(Local==null||Local.Activity==OpenSpaceActivity.Fishing){Notify("낚시 자리를 떠난 뒤 고양이에게 가주세요.");return;}
        world.ClosePreview();Local.SetActivity(OpenSpaceActivity.Petting);
        Show(OpenSpacePanel.Cat,CatCompanionTable.Default.GetCatName(Local.CatBody),"고양이 몸 위에서 마우스를 누른 채 위아래로 쓰다듬어 주세요.\n누르고 있으면 둥실거림이 멈춰요.");Actions("먹이 주기","고양이 감정표현");
    }
    public void OpenBag(){OpenFishList(OpenSpacePanel.Bag);}
    public void OpenCollection(){OpenFishList(OpenSpacePanel.Collection);}
    private void OpenFishList(OpenSpacePanel mode)
    {
        if(DB==null)return;
        Show(mode,mode==OpenSpacePanel.Collection?"생선 도감":mode==OpenSpacePanel.Feed?"고양이 먹이 선택":"생선 가방","생선을 선택해주세요. 도감 등록과 먹이 주기는 각각 생선 1마리를 사용합니다.");
        var progress=DB.OpenSpaceData;
        for(int i=0;i<settings.fish.Length;i++)
        {
            bool collected=!string.IsNullOrEmpty(progress.collectedUtc[i]);
            string text=mode==OpenSpacePanel.Collection?(collected?settings.fish[i].name:"??? · 미등록"):
                settings.fish[i].name+" × "+progress.bag[i]+(collected?" · 도감 등록됨":" · 미등록");
            Row(i,text,mode==OpenSpacePanel.Collection&&!collected?null:settings.fish[i].icon);
        }
    }
    private void OpenMotions()
    {
        Show(OpenSpacePanel.Motions,"고양이 감정표현","생선을 먹이면 모션을 발견할 수 있어요. 해금한 모션의 재생은 무료예요.");
        for(int i=0;i<settings.motions.Length;i++)Row(i,(DB.OpenSpaceData.unlockedMotions&(1<<i))!=0?settings.motions[i].name:"잠김 · 먹이를 주어 발견",null,(DB.OpenSpaceData.unlockedMotions&(1<<i))!=0);
        Actions("모션 중단");
    }
    public void OpenMenu()
    {
        if(saving){Notify("결과 저장 중입니다. 완료 후 이동할 수 있어요.");return;}
        CancelFishing();Show(OpenSpacePanel.Menu,"광장 메뉴","전투에 참여하려면 로비로 돌아가주세요.");
        Row(0,"계속하기");Row(1,"환경설정 / 광장 키 설정");Row(2,"다른 세션으로 이동");Row(3,"로비로 돌아가기");Row(4,"결과 확인 / 재시도");Row(5,"도움말 / 차단 목록");
    }
    public void OpenHelp()
    {
        Show(OpenSpacePanel.Help,"광장 안내","WASD로 걷고, 우클릭 드래그로 둘러봐요. 휠로 확대할 수 있어요.\n"+
            "["+OpenSpaceControls.Key(OpenSpaceAction.Interact)+"] 고양이 교감 / 낚시 · ["+OpenSpaceControls.Key(OpenSpaceAction.Emote)+"] 감정표현\n"+
            "Enter 채팅 · ESC 닫기\n낚시 입질은 6초예요. 채팅 중에는 챔질 버튼을 누를 수 있어요.\n생선을 도감에 등록하거나 고양이에게 먹여 모션을 발견해보세요.\n낚시 취소와 광장 안전 복귀에는 손실이 없어요.");
        var blocked=DB?.OpenSpaceData.blocked;
        if(blocked!=null)for(int i=0;i<rows.Length&&blockPage*6+i<blocked.Count;i++)Row(i,"차단 해제: "+blocked[blockPage*6+i].nickname);
        Actions("안내 확인","다음 차단 목록");
    }
    public void OpenEmotes()
    {Show(OpenSpacePanel.Emotes,"감정표현","주변 사람에게 인사해보세요. 5초 간격으로 사용할 수 있어요.");Row(0,"안녕!");Row(1,"신나요!");Row(2,"아쉬워요");Row(3,"으으!");}
    public void SelectRow(int index)
    {
        if(saving||confirmation.activeSelf)return;
        if(panel==OpenSpacePanel.Menu)
        {
            if(index==0)ClosePanel();
            if(index==1)OpenSettings();
            if(index==2)Confirm("진행 중 활동을 끝내고 다른 광장으로 이동할까요? 낚시 대기 취소에는 손실이 없어요.",()=>OpenSpaceSession.Instance.ChangeSession());
            if(index==3)Confirm("활동을 끝내고 로비로 돌아갈까요? 확정된 수집품은 유지됩니다.",()=>OpenSpaceSession.Instance.ReturnToLobby());
            if(index==4)Recover();if(index==5)OpenHelp();return;
        }
        if(panel==OpenSpacePanel.Settings){bindKey=index;body.text="변경할 키를 눌러주세요. ESC는 취소입니다.";return;}
        if(panel==OpenSpacePanel.Emotes){Local?.SayEmote(index+1);ClosePanel();return;}
        if(panel==OpenSpacePanel.Motions){Local?.PlayCat(index);return;}
        if(panel==OpenSpacePanel.Help)
        {
            int i=blockPage*6+index;if(i<DB.OpenSpaceData.blocked.Count)
            {var blocked=DB.OpenSpaceData.blocked[i];Confirm(blocked.nickname+"님의 차단을 해제할까요?",()=>Mutate(new OpenSpaceRequest{kind="unblock",target=blocked.id}));}
            return;
        }
        if(panel==OpenSpacePanel.Bag||panel==OpenSpacePanel.Feed||panel==OpenSpacePanel.Collection)
        {
            selectedFish=index;var progress=DB.OpenSpaceData;var fish=settings.fish[index];
            if(panel==OpenSpacePanel.Collection)
            {body.text=string.IsNullOrEmpty(progress.collectedUtc[index])?"아직 등록하지 않은 생선이에요.":fish.name+"\n"+fish.description+"\n최초 등록: "+progress.collectedUtc[index];return;}
            body.text=fish.name+"\n"+fish.description+"\n보유 "+progress.bag[index]+"마리";
            if(panel==OpenSpacePanel.Feed){Actions("1마리 먹이기");primary.interactable=progress.bag[index]>0;}
            else {Actions("도감에 1마리 등록");primary.interactable=progress.bag[index]>0&&string.IsNullOrEmpty(progress.collectedUtc[index]);}
        }
    }
    public void Primary()
    {
        if(saving)return;
        switch(panel)
        {
            case OpenSpacePanel.Target:if(world.Selected!=null)Inspect(world.Selected);break;
            case OpenSpacePanel.Outfit:world.ResetView();break;
            case OpenSpacePanel.Cat:OpenFishList(OpenSpacePanel.Feed);break;
            case OpenSpacePanel.Motions:Local?.StopCat();break;
            case OpenSpacePanel.Help:if(!DB.OpenSpaceData.helpSeen)Mutate(new OpenSpaceRequest{kind="help"});else ClosePanel();break;
            case OpenSpacePanel.Settings:OpenSpaceControls.Reset();OpenSettings();break;
            case OpenSpacePanel.Bag:
                Confirm(settings.fish[selectedFish].name+" 1마리를 도감에 등록할까요? 등록한 생선은 먹이로 다시 꺼낼 수 없어요.",()=>Mutate(new OpenSpaceRequest{kind="collect",fish=selectedFish}));break;
            case OpenSpacePanel.Feed:
                if(Local==null||Local.Activity!=OpenSpaceActivity.Petting){Notify("내 고양이와 교감을 시작한 뒤 먹이를 주세요.");break;}
                string warning=string.IsNullOrEmpty(DB.OpenSpaceData.collectedUtc[selectedFish])?"\n아직 도감에 등록하지 않은 생선이에요.":"";
                Confirm(settings.fish[selectedFish].name+" 1마리를 먹일까요? 남은 수량 "+Mathf.Max(0,DB.OpenSpaceData.bag[selectedFish]-1)+
                    "마리\n이미 해금한 모션이 나와도 생선은 사용됩니다."+warning,()=>Mutate(new OpenSpaceRequest{kind="feed",fish=selectedFish,motion=settings.PickMotion(UnityEngine.Random.value)}));break;
        }
    }
    public void Secondary()
    {
        if(panel==OpenSpacePanel.Cat)OpenMotions();
        else if(panel==OpenSpacePanel.Help){blockPage=DB.OpenSpaceData.blocked.Count==0?0:(blockPage+1)%Mathf.CeilToInt(DB.OpenSpaceData.blocked.Count/6f);OpenHelp();}
        else if(panel==OpenSpacePanel.Target&&world.Selected!=null)Confirm(targetName+"님의 채팅과 감정표현을 숨길까요?",()=>Mutate(new OpenSpaceRequest{kind="block",target=targetId,nickname=targetName}));
    }
    private void Confirm(string text,Action action){confirmed=action;confirmationText.text=text;confirmation.transform.SetAsLastSibling();confirmation.SetActive(true);}
    public void ConfirmYes(){var action=confirmed;confirmed=null;confirmation.SetActive(false);action?.Invoke();}
    public void ConfirmNo(){confirmed=null;confirmation.SetActive(false);}
    public void ClosePanel()
    {
        if(saving)return;
        if(world.Inspecting)world.ClosePreview();
        if(Local!=null&&Local.Activity!=OpenSpaceActivity.Fishing){Local.StopCat();Local.SetActivity(OpenSpaceActivity.Walking);}
        Show(OpenSpacePanel.None,"","");
    }
    public void Back()
    {
        if(CapturingKey){bindKey=-1;OpenSettings();return;}
        if(ChatFocused){chatInput.DeactivateInputField();return;}
        if(confirmation.activeSelf){ConfirmNo();return;}
        if(panel!=OpenSpacePanel.None){ClosePanel();return;}
        if(Local!=null&&Local.Activity==OpenSpaceActivity.Fishing){LeaveFishing();return;}
        OpenMenu();
    }
    private int Seat=>Local!=null&&OpenSpaceHub.Instance!=null?OpenSpaceHub.Instance.SeatOf(Local.Object.InputAuthority):-1;
    public void Interact()
    {
        if(Seat>=0){var phase=OpenSpaceHub.Instance.Seats[Seat].phase;if(phase==OpenSpaceFishingPhase.Bite)Hook();else StartFishing();}
        else if(world.PromptCat)OpenCat();
        else if(world.PromptSeat>=0)OpenSpaceHub.Instance?.RPC_Fish(world.PromptSeat,0);
    }
    public void StartFishing(){if(Seat>=0&&!saving&&!DB.HasOpenSpacePending)OpenSpaceHub.Instance.RPC_Fish(Seat,1);}
    public void Hook(){if(Seat>=0)OpenSpaceHub.Instance.RPC_Fish(Seat,2);}
    public void CancelFishing(){if(Seat>=0)OpenSpaceHub.Instance.RPC_Fish(Seat,3);}
    public void LeaveFishing(){if(Seat>=0)OpenSpaceHub.Instance.RPC_Fish(Seat,4);}
    private async void CommitCatch(OpenSpaceSeat seat)
    {
        saving=true;
        var result=await DB.CommitOpenSpaceAsync(new OpenSpaceRequest{id=seat.attempt.ToString(),kind="catch",fish=seat.fish,
            coins=settings.enableCoinRewards?settings.fish[seat.fish].coins:0});
        if(this==null)return;saving=false;FinishReceipt(result);
    }
    private async void Mutate(OpenSpaceRequest request)
    {
        if(saving)return;request.id=Guid.NewGuid().ToString("N");saving=true;
        var result=await DB.CommitOpenSpaceAsync(request);if(this==null)return;saving=false;FinishReceipt(result);
        if(result!=null && result.kind=="feed")
        {Show(OpenSpacePanel.Cat,"고양이 교감","해금한 모션은 감정표현 목록에서 다시 볼 수 있어요.");Actions("먹이 주기","고양이 감정표현");}
        else if(result!=null)ClosePanel();
    }
    public async void Recover()
    {
        if(saving||DB==null)return;saving=true;
        var result=await DB.CommitOpenSpaceAsync();if(this==null)return;saving=false;FinishReceipt(result);
    }
    private void FinishReceipt(OpenSpaceReceipt receipt)
    {
        Notify(DB.OpenSpaceSaveMessage);
        if(receipt==null)return;
        if(receipt.kind=="catch")
        {
            OpenSpaceHub.Instance?.RPC_Receipt(receipt.id);
            if(settings.catchSound!=null)activityAudio.PlayOneShot(settings.catchSound);
        }
        if(receipt.kind=="feed"&&Local!=null){Local.PlayCat(receipt.motion);Notify(DB.OpenSpaceSaveMessage+" · "+settings.motions[receipt.motion].name);}
        if(receipt.kind=="block"||receipt.kind=="unblock")RefreshChat();
    }
    public void ChatEnter()
    {
        if(Time.frameCount-lastCompositionFrame<=1)return;
        if(ChatFocused)SendChat();else {chatInput.ActivateInputField();chatInput.Select();}
    }
    public void SendChat()
    {
        if(Time.frameCount-lastCompositionFrame<=1||!string.IsNullOrEmpty(Input.compositionString))return;
        if(Time.unscaledTime<nextChat){Notify("채팅은 "+Mathf.CeilToInt(nextChat-Time.unscaledTime)+"초 뒤 보낼 수 있어요.");return;}
        string message=chatInput.text.Replace('\n',' ').Replace('\r',' ').Trim();
        if(message.Length==0)return;if(message.Length>100)message=message.Substring(0,100);
        if(OpenSpaceHub.Instance==null)return;
        OpenSpaceHub.Instance.RPC_Chat(message);nextChat=Time.unscaledTime+settings.chatCooldown;
        chatInput.text="";chatInput.DeactivateInputField();
    }
    public void ReceiveChat(string id,string nickname,string message)
    {
        chat.Add((id,nickname,message));if(chat.Count>50)chat.RemoveAt(0);RefreshChat();
    }
    private void RefreshChat()
    {
        chatText.Clear();foreach(var item in chat)if(DB==null||!DB.OpenSpaceData.IsBlocked(item.id))chatText.Append(item.name).Append("  ").Append(item.text).Append('\n');
        chatHistory.text=chatText.ToString();Canvas.ForceUpdateCanvases();chatScroll.verticalNormalizedPosition=0;
    }
    private void OpenSettings()
    {
        Show(OpenSpacePanel.Settings,"광장 키 설정","전투 키 설정은 변경하지 않습니다. 변경할 항목을 누른 뒤 키를 눌러주세요.");
        string[] names={"앞으로","뒤로","왼쪽","오른쪽","상호작용 / 챔질","감정표현"};
        for(int i=0;i<6;i++)Row(i,names[i]+" · "+OpenSpaceControls.Key((OpenSpaceAction)i));Actions("광장 키 초기화");
    }
    public void CaptureKey()
    {
        if(bindKey<0||!Input.anyKeyDown)return;
        foreach(KeyCode key in Enum.GetValues(typeof(KeyCode)))if(Input.GetKeyDown(key))
        {if(OpenSpaceControls.Assign((OpenSpaceAction)bindKey,key)){bindKey=-1;OpenSettings();}else Notify("다른 동작과 중복되지 않는 키를 선택해주세요.");break;}
    }
    public void RetryConnection()
    {if(OpenSpaceSession.Instance!=null&&!OpenSpaceSession.Instance.Connected)OpenSpaceSession.Instance.Enter();}
    public void ReturnToMain()
    {if(!saving)Confirm("활동을 끝내고 로비로 돌아갈까요? 확정된 수집품은 유지됩니다.",()=>OpenSpaceSession.Instance?.ReturnToLobby());}
}
