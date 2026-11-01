using System;
using System.Collections.Generic;
using Fusion;
using UnityEngine;

public enum OpenSpaceFishingPhase { Empty, Ready, Casting, Waiting, Bite, Settling, Result, Missed }
public struct OpenSpaceSeat : INetworkStruct
{
    public PlayerRef owner;
    public OpenSpaceFishingPhase phase;
    public TickTimer timer;
    public NetworkString<_64> attempt;
    public int fish;
}
public sealed class OpenSpaceHub : NetworkBehaviour
{
    public static OpenSpaceHub Instance {get;private set;}
    [SerializeField] private OpenSpaceSettings settings;
    [SerializeField] private Transform[] seats;
    [Networked,Capacity(OpenSpaceSettings.SeatCount)] public NetworkArray<OpenSpaceSeat> Seats => default;
    private readonly Dictionary<PlayerRef,float> nextChat=new();
    public override void Spawned(){Instance=this;}
    public int SeatOf(PlayerRef owner){for(int i=0;i<Seats.Length;i++)if(Seats[i].owner==owner)return i;return -1;}
    public override void FixedUpdateNetwork()
    {
        if(!Object.HasStateAuthority)return;
        for(int i=0;i<Seats.Length;i++)
        {
            var seat=Seats[i];if(seat.owner==PlayerRef.None)continue;
            if(!Runner.TryGetPlayerObject(seat.owner,out var player)||player==null){Seats.Set(i,default);continue;}
            if(seat.timer.Expired(Runner))
            {
                if(seat.phase==OpenSpaceFishingPhase.Casting)
                {seat.phase=OpenSpaceFishingPhase.Waiting;seat.timer=TickTimer.CreateFromSeconds(Runner,UnityEngine.Random.Range(settings.waitSeconds.x,settings.waitSeconds.y));}
                else if(seat.phase==OpenSpaceFishingPhase.Waiting)
                {seat.phase=OpenSpaceFishingPhase.Bite;seat.timer=TickTimer.CreateFromSeconds(Runner,settings.biteSeconds);}
                else if(seat.phase==OpenSpaceFishingPhase.Bite)
                {seat.phase=OpenSpaceFishingPhase.Missed;seat.timer=TickTimer.None;}
                Seats.Set(i,seat);
            }
        }
    }
    [Rpc(RpcSources.All,RpcTargets.StateAuthority)]
    public void RPC_Fish(int index,int action,RpcInfo info=default)
    {
        PlayerRef sender=info.Source;
        if(sender==PlayerRef.None)sender=Runner.LocalPlayer;
        if(index<0||index>=seats.Length||!Runner.TryGetPlayerObject(sender,out var obj))return;
        var avatar=obj.GetComponent<OpenSpaceAvatar>();if(avatar==null)return;
        var seat=Seats[index];
        if(action==0)
        {
            if(seat.owner!=PlayerRef.None||SeatOf(sender)>=0||avatar.Activity!=OpenSpaceActivity.Walking||
                Vector3.Distance(avatar.transform.position,seats[index].position)>settings.interactionDistance+.3f)return;
            seat.owner=sender;seat.phase=OpenSpaceFishingPhase.Ready;
            avatar.GetComponent<NetworkCharacterController>().Teleport(seats[index].position,seats[index].rotation);
            avatar.Activity=OpenSpaceActivity.Fishing;
        }
        else
        {
            if(seat.owner!=sender)return;
            if(action==1 && (seat.phase==OpenSpaceFishingPhase.Ready||seat.phase==OpenSpaceFishingPhase.Result||seat.phase==OpenSpaceFishingPhase.Missed))
            {seat.phase=OpenSpaceFishingPhase.Casting;seat.timer=TickTimer.CreateFromSeconds(Runner,settings.castSeconds);seat.attempt=Guid.NewGuid().ToString("N");}
            else if(action==2 && seat.phase==OpenSpaceFishingPhase.Bite && !seat.timer.Expired(Runner))
            {seat.fish=settings.PickFish(UnityEngine.Random.value);seat.phase=OpenSpaceFishingPhase.Settling;seat.timer=TickTimer.None;}
            else if(action==3 && seat.phase!=OpenSpaceFishingPhase.Settling)
            {seat.phase=OpenSpaceFishingPhase.Ready;seat.timer=TickTimer.None;}
            else if(action==4 && seat.phase!=OpenSpaceFishingPhase.Settling) {Release(sender);return;}
            else return;
        }
        Seats.Set(index,seat);
    }
    [Rpc(RpcSources.All,RpcTargets.StateAuthority)]
    public void RPC_Receipt(string requestId,RpcInfo info=default)
    {
        var sender=info.Source==PlayerRef.None?Runner.LocalPlayer:info.Source;int index=SeatOf(sender);if(index<0)return;
        var seat=Seats[index];if(seat.phase!=OpenSpaceFishingPhase.Settling||seat.attempt.ToString()!=requestId)return;
        seat.phase=OpenSpaceFishingPhase.Result;seat.timer=TickTimer.None;Seats.Set(index,seat);
    }
    public void Release(PlayerRef owner)
    {
        if(!Object.HasStateAuthority)return;
        int index=SeatOf(owner);if(index>=0)Seats.Set(index,default);
        if(Runner.TryGetPlayerObject(owner,out var obj)&&obj!=null)
        {var avatar=obj.GetComponent<OpenSpaceAvatar>();if(avatar!=null)avatar.Activity=OpenSpaceActivity.Walking;}
        nextChat.Remove(owner);
    }
    [Rpc(RpcSources.All,RpcTargets.StateAuthority)]
    public void RPC_Chat(string text,RpcInfo info=default)
    {
        var sender=info.Source==PlayerRef.None?Runner.LocalPlayer:info.Source;
        if(string.IsNullOrWhiteSpace(text)||text.Length>100||!Runner.TryGetPlayerObject(sender,out var obj)||obj==null)return;
        float now=(float)Runner.SimulationTime;
        if(nextChat.TryGetValue(sender,out float next)&&now<next)return;
        nextChat[sender]=now+settings.chatCooldown;
        var avatar=obj.GetComponent<OpenSpaceAvatar>();
        RPC_ChatReceived(avatar.PublicId.ToString(),avatar.Nickname.ToString(),text.Trim());
    }
    [Rpc(RpcSources.StateAuthority,RpcTargets.All)]
    private void RPC_ChatReceived(string id,string nickname,string message)
    {OpenSpaceWorld.Instance?.UI.ReceiveChat(id,nickname,message);}
    public override void Despawned(NetworkRunner runner,bool hasState){if(Instance==this)Instance=null;}
    private void OnDestroy(){if(Instance==this)Instance=null;}
}
