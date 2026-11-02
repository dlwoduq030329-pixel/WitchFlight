using System;
using System.Collections.Generic;
using Fusion;
using UnityEngine;

public enum OpenSpaceActivity { Walking, Inspecting, Petting, Fishing }
[RequireComponent(typeof(NetworkCharacterController))]
public sealed class OpenSpaceAvatar : NetworkBehaviour
{
    public static readonly List<OpenSpaceAvatar> All = new();
    public static OpenSpaceAvatar Local {get;private set;}
    [SerializeField] private OpenSpaceCharacterView view;
    [SerializeField] private OpenSpaceSettings settings;
    [Networked] public NetworkString<_32> Nickname {get;set;}
    [Networked] public NetworkString<_64> PublicId {get;set;}
    [Networked] public PlayerConfig Appearance {get;set;}
    [Networked] public int Title {get;set;}
    [Networked] public int CatBody {get;set;}
    [Networked] public int CatEyes {get;set;}
    [Networked] public OpenSpaceActivity Activity {get;set;}
    [Networked] public bool HoldingCat {get;set;}
    [Networked] public bool StrokingCat {get;set;}
    [Networked] public int CatMotion {get;set;}
    [Networked] public int CatSequence {get;set;}
    [Networked] public TickTimer CatTimer {get;set;}
    [Networked] public int Emote {get;set;}
    [Networked] public TickTimer EmoteTimer {get;set;}
    [Networked] private TickTimer EmoteCooldown {get;set;}
    [Networked] private bool Initialized {get;set;}
    private NetworkCharacterController motor;
    private CharacterController collider;
    private bool rendered;
    private int lastCatSequence;
    private string publicId;
    public OpenSpaceCharacterView View => view;
    public override void Spawned()
    {
        motor=GetComponent<NetworkCharacterController>();collider=GetComponent<CharacterController>();
        motor.maxSpeed=settings.walkSpeed;
        foreach(var other in All)if(other!=null && other.collider!=null)Physics.IgnoreCollision(collider,other.collider,true);
        All.Add(this);
        if(Object.HasInputAuthority)
        {
            Local=this;
            var db=DatabaseManager.Instance;
            RPC_Identity(DataConfig.playerName,db.OpenSpacePublicId,DataConfig.GetPlayerConfig(),DataConfig.equippedTitleId,
                db.GetPlayerSetting().catBodyIndex,db.GetPlayerSetting().catEyeIndex);
        }
    }
    [Rpc(RpcSources.InputAuthority,RpcTargets.StateAuthority)]
    private void RPC_Identity(string nickname,string id,PlayerConfig appearance,int title,int body,int eyes)
    {
        if(Initialized)return;
        Nickname=(nickname??"마녀").Substring(0,Math.Min(32,(nickname??"마녀").Length));
        PublicId=id;Appearance=appearance.Sanitized();Title=title;CatBody=Mathf.Clamp(body,1,7);CatEyes=Mathf.Clamp(eyes,1,3);Initialized=true;
    }
    public override void FixedUpdateNetwork()
    {
        // Fusion NCC restores position/velocity before resimulation; local input is predicted.
        if(GetInput(out OpenSpaceInput input))
        {
            bool stopped=input.stopped || Activity!=OpenSpaceActivity.Walking;
            motor.Move(stopped?Vector3.zero:Vector3.ClampMagnitude(input.direction,1));
            if(Object.HasStateAuthority && OpenSpaceWorld.Instance!=null && !OpenSpaceWorld.Instance.SafeBounds.Contains(transform.position))
            {
                motor.Teleport(OpenSpaceWorld.Instance.NearestSpawn(transform.position));
                motor.Velocity=Vector3.zero;OpenSpaceHub.Instance?.Release(Object.InputAuthority);Activity=OpenSpaceActivity.Walking;
            }
        }
        if(Object.HasStateAuthority && CatTimer.Expired(Runner)) {CatTimer=TickTimer.None;CatMotion=-1;}
    }
    public override void Render()
    {
        if(!Initialized || view==null)return;
        if(!rendered)
        {
            view.Apply(Appearance,CatBody,CatEyes);publicId=PublicId.ToString();rendered=true;
            if(view.nicknameLabel!=null)view.nicknameLabel.text=Nickname.ToString();
            if(view.titleLabel!=null)view.titleLabel.text=TitleCatalog.Name(Title);
        }
        bool blocked=DatabaseManager.Instance!=null && DatabaseManager.Instance.OpenSpaceData.IsBlocked(publicId);
        if(lastCatSequence!=CatSequence) {lastCatSequence=CatSequence;view.Cat.PlayMotion(CatMotion);}
        view.Tick(motor.Velocity.magnitude,Activity,HoldingCat,StrokingCat,CatTimer.IsRunning,blocked,
            EmoteTimer.IsRunning && !EmoteTimer.Expired(Runner)?Emote:0);
    }
    public void SetActivity(OpenSpaceActivity state) => RPC_Activity(state);
    [Rpc(RpcSources.InputAuthority,RpcTargets.StateAuthority)]
    private void RPC_Activity(OpenSpaceActivity state)
    {
        if(state==OpenSpaceActivity.Fishing || !Enum.IsDefined(typeof(OpenSpaceActivity),state))return;
        if(OpenSpaceHub.Instance!=null && OpenSpaceHub.Instance.SeatOf(Object.InputAuthority)>=0)return;
        Activity=state;HoldingCat=StrokingCat=false;
    }
    public void Pet(bool holding,bool stroking) => RPC_Pet(holding,stroking);
    [Rpc(RpcSources.InputAuthority,RpcTargets.StateAuthority)]
    private void RPC_Pet(bool holding,bool stroking) {if(Activity==OpenSpaceActivity.Petting){HoldingCat=holding;StrokingCat=holding&&stroking;}}
    public void PlayCat(int motion) => RPC_Cat(motion);
    [Rpc(RpcSources.InputAuthority,RpcTargets.StateAuthority)]
    private void RPC_Cat(int motion)
    {
        if(Activity!=OpenSpaceActivity.Petting || motion<0 || motion>=settings.motions.Length || (CatTimer.IsRunning&&!CatTimer.Expired(Runner)))return;
        CatMotion=motion;CatSequence++;CatTimer=TickTimer.CreateFromSeconds(Runner,settings.motions[motion].duration);
    }
    public void StopCat() => RPC_StopCat();
    [Rpc(RpcSources.InputAuthority,RpcTargets.StateAuthority)] private void RPC_StopCat(){CatTimer=TickTimer.None;CatMotion=-1;}
    public void SayEmote(int emote) => RPC_Emote(emote);
    [Rpc(RpcSources.InputAuthority,RpcTargets.StateAuthority)]
    private void RPC_Emote(int emote)
    {
        if(Activity!=OpenSpaceActivity.Walking || emote<1 || emote>4 || (EmoteCooldown.IsRunning&&!EmoteCooldown.Expired(Runner)))return;
        Emote=emote;EmoteTimer=TickTimer.CreateFromSeconds(Runner,3);EmoteCooldown=TickTimer.CreateFromSeconds(Runner,settings.emoteCooldown);
    }
    public override void Despawned(NetworkRunner runner,bool hasState)
    {All.Remove(this);if(Local==this)Local=null;}
    private void OnDestroy(){All.Remove(this);if(Local==this)Local=null;}
}
