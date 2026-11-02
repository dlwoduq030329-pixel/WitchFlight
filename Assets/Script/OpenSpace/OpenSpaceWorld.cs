using UnityEngine;
using UnityEngine.EventSystems;

public sealed class OpenSpaceWorld : MonoBehaviour
{
    public static OpenSpaceWorld Instance {get;private set;}
    [SerializeField] private OpenSpaceSettings settings;
    [SerializeField] private Camera viewCamera;
    [SerializeField] private OpenSpaceUI ui;
    [SerializeField] private Transform[] spawns, fishingSeats;
    [SerializeField] private OpenSpaceCharacterView outfitPreview;
    [SerializeField] private Bounds safeBounds=new Bounds(Vector3.zero,new Vector3(90,24,90));
    [SerializeField] private LayerMask sceneryMask=1;
    public OpenSpaceUI UI=>ui;
    public Transform[] Spawns=>spawns;
    public Bounds SafeBounds=>safeBounds;
    public OpenSpaceInput Input {get;private set;}
    public Camera Camera=>viewCamera;
    public OpenSpaceAvatar Selected {get;private set;}
    public int PromptSeat {get;private set;}=-1;
    public bool PromptCat {get;private set;}
    private float yaw=0,pitch=22,distance=5.5f,previewYaw,previewDistance=4;
    private float nextPet, lastStroke, lastPointerY;
    private bool preview,wasHolding,firstHelp;
    public bool Inspecting=>preview;
    private void Awake()
    {
        Instance=this;outfitPreview.gameObject.SetActive(false);
        Cursor.lockState=CursorLockMode.None;Cursor.visible=true;Time.timeScale=1;
    }
    public Vector3 NearestSpawn(Vector3 point)
    {Transform best=spawns[0];foreach(var spawn in spawns)if((spawn.position-point).sqrMagnitude<(best.position-point).sqrMagnitude)best=spawn;return best.position;}
    public bool PointerOnUI => EventSystem.current!=null&&EventSystem.current.IsPointerOverGameObject();
    private void Update()
    {
        var local=OpenSpaceAvatar.Local;
        if(local==null||OpenSpaceSession.Instance==null||!OpenSpaceSession.Instance.Connected) {Input=default;return;}
        if(!firstHelp)
        {
            firstHelp=true;
            if(DatabaseManager.Instance.HasOpenSpacePending)ui.Recover();
            else if(!DatabaseManager.Instance.OpenSpaceData.helpSeen)ui.OpenHelp();
        }
        bool typing=ui.ChatFocused;
        if(UnityEngine.Input.GetKeyDown(KeyCode.Escape)) {Input=new OpenSpaceInput{stopped=true};ui.Back();return;}
        if(UnityEngine.Input.GetKeyDown(KeyCode.Return)&&string.IsNullOrEmpty(UnityEngine.Input.compositionString))ui.ChatEnter();
        if(ui.CapturingKey){ui.CaptureKey();Input=new OpenSpaceInput {stopped=true};return;}
        bool blocked=typing||ui.BlocksMovement||preview;
        Vector3 direction=Vector3.zero;
        if(!blocked)
        {
            float horizontal=(OpenSpaceControls.Held(OpenSpaceAction.Right)?1:0)-(OpenSpaceControls.Held(OpenSpaceAction.Left)?1:0);
            float vertical=(OpenSpaceControls.Held(OpenSpaceAction.Forward)?1:0)-(OpenSpaceControls.Held(OpenSpaceAction.Backward)?1:0);
            direction=Quaternion.Euler(0,yaw,0)*new Vector3(horizontal,0,vertical);
        }
        Input=new OpenSpaceInput {direction=direction,stopped=blocked};
        if(!typing&&!ui.BlocksPointer&&!PointerOnUI)
        {
            if(UnityEngine.Input.GetMouseButton(1))
            {
                float turn=UnityEngine.Input.GetAxisRaw("Mouse X")*3*ControlSettings.SensitivityMultiplier;
                if(preview)previewYaw+=turn;else yaw+=turn;
                pitch=Mathf.Clamp(pitch-UnityEngine.Input.GetAxisRaw("Mouse Y")*2,5,65);
            }
            float zoom=UnityEngine.Input.mouseScrollDelta.y*.45f;
            if(preview)previewDistance=Mathf.Clamp(previewDistance-zoom,1.3f,6);
            else distance=Mathf.Clamp(distance-zoom,1.5f,9);
            if(!preview&&local.Activity==OpenSpaceActivity.Walking&&UnityEngine.Input.GetMouseButtonDown(0))
            {
                var ray=viewCamera.ScreenPointToRay(UnityEngine.Input.mousePosition);
                if(Physics.Raycast(ray,out var hit,30,1<<28,QueryTriggerInteraction.Ignore))
                {
                    var target=hit.collider.GetComponentInParent<OpenSpaceAvatar>();
                    if(target!=null&&target!=local){Selected=target;ui.OpenTarget();}
                }
            }
        }
        UpdatePrompt(local);
        if(!typing&&!ui.BlocksMovement&&OpenSpaceControls.Down(OpenSpaceAction.Interact))ui.Interact();
        if(!typing&&!ui.BlocksMovement&&OpenSpaceControls.Down(OpenSpaceAction.Emote)&&local.Activity==OpenSpaceActivity.Walking)ui.OpenEmotes();
        bool holding=local.Activity==OpenSpaceActivity.Petting&&!ui.BlocksPointer&&!typing&&!PointerOnUI&&
            UnityEngine.Input.GetMouseButton(0)&&local.View.Cat.Intersects(viewCamera.ScreenPointToRay(UnityEngine.Input.mousePosition));
        if(holding&&wasHolding&&Mathf.Abs(UnityEngine.Input.mousePosition.y-lastPointerY)>1.5f)lastStroke=Time.unscaledTime;
        lastPointerY=UnityEngine.Input.mousePosition.y;
        bool stroke=holding&&wasHolding&&Time.unscaledTime-lastStroke<.5f;
        if(Time.unscaledTime>=nextPet&&(holding||wasHolding||local.HoldingCat))
        {local.Pet(holding,stroke);nextPet=Time.unscaledTime+.1f;}
        wasHolding=holding;
    }
    private void UpdatePrompt(OpenSpaceAvatar local)
    {
        PromptSeat=-1;PromptCat=false;
        if(local.Activity!=OpenSpaceActivity.Walking)return;
        float best=float.PositiveInfinity;
        for(int i=-1;i<fishingSeats.Length;i++)
        {
            Vector3 at=i<0?local.View.Cat.transform.position:fishingSeats[i].position;
            float d=Vector3.Distance(local.transform.position,at);if(d>settings.interactionDistance)continue;
            Vector3 screen=viewCamera.WorldToViewportPoint(at);if(screen.z<=0)continue;
            float score=new Vector2(screen.x-.5f,screen.y-.5f).sqrMagnitude*100+d*.001f;
            if(score<best){best=score;PromptSeat=i;PromptCat=i<0;}
        }
    }
    private void LateUpdate()
    {
        var local=OpenSpaceAvatar.Local;if(local==null)return;
        bool pet=local.Activity==OpenSpaceActivity.Petting;
        Vector3 center=preview?outfitPreview.transform.position+Vector3.up*1.1f:
            pet?Vector3.Lerp(local.transform.position+Vector3.up,local.View.Cat.transform.position+Vector3.up*.35f,.6f):local.transform.position+Vector3.up*1.2f;
        var rotation=Quaternion.Euler(preview?12:pitch,preview?previewYaw:yaw,0);
        if(preview)center+=rotation*Vector3.right*.55f;
        float reach=preview?previewDistance:pet?2.8f:distance;
        if(!preview&&Physics.SphereCast(center,.2f,-(rotation*Vector3.forward),out var hit,reach,sceneryMask,QueryTriggerInteraction.Ignore))reach=Mathf.Max(.35f,hit.distance-.1f);
        Vector3 target=center-rotation*Vector3.forward*reach;
        viewCamera.transform.position=Vector3.Lerp(viewCamera.transform.position,target,1-Mathf.Exp(-9*Time.unscaledDeltaTime));
        viewCamera.transform.rotation=Quaternion.Slerp(viewCamera.transform.rotation,rotation,1-Mathf.Exp(-12*Time.unscaledDeltaTime));
        foreach(var avatar in OpenSpaceAvatar.All)
        {
            if(avatar==null)continue;
            bool visible=(avatar.transform.position-viewCamera.transform.position).sqrMagnitude<settings.nameDistance*settings.nameDistance;
            Billboard(avatar.View.emoteLabel,visible);Billboard(avatar.View.nicknameLabel,visible);Billboard(avatar.View.titleLabel,visible);
        }
    }
    private void Billboard(TMPro.TMP_Text label,bool visible)
    {if(label==null)return;label.enabled=visible;if(visible)label.transform.rotation=viewCamera.transform.rotation;}
    public void Inspect(OpenSpaceAvatar avatar)
    {
        if(avatar==null)return;
        outfitPreview.Apply(avatar.Appearance,avatar.CatBody,avatar.CatEyes);
        outfitPreview.gameObject.SetActive(true);preview=true;previewDistance=4;previewYaw=180;
        OpenSpaceAvatar.Local.SetActivity(OpenSpaceActivity.Inspecting);
    }
    public void ClosePreview(){preview=false;outfitPreview.gameObject.SetActive(false);if(OpenSpaceAvatar.Local!=null)OpenSpaceAvatar.Local.SetActivity(OpenSpaceActivity.Walking);}
    public void ResetView(){pitch=22;distance=5.5f;previewDistance=4;previewYaw=180;}
    private void OnDestroy(){if(Instance==this)Instance=null;}
}
