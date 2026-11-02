using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable] public sealed class OpenSpaceReceipt
{
    public string id, kind, utc;
    public int fish, motion = -1, coins;
    public bool newMotion;
}
[Serializable] public sealed class OpenSpaceBlockedUser { public string id, nickname; }
[Serializable] public sealed class OpenSpaceRequest
{
    public string id, kind, target, nickname;
    public int fish, motion = -1, coins;
}
[Serializable] public sealed class OpenSpaceProgress
{
    public int version = 1;
    public int[] bag = new int[6];
    public string[] collectedUtc = new string[6];
    public int unlockedMotions;
    public bool helpSeen;
    public List<OpenSpaceBlockedUser> blocked = new();
    public List<OpenSpaceReceipt> receipts = new();
    public static OpenSpaceProgress Parse(string json)
    {
        var p = string.IsNullOrWhiteSpace(json) || json == "{}" ? new OpenSpaceProgress() : JsonUtility.FromJson<OpenSpaceProgress>(json);
        if (p == null || p.version != 1) throw new InvalidOperationException("광장 저장 데이터 형식을 확인해주세요.");
        if (p.bag == null || p.bag.Length != 6 || p.collectedUtc == null || p.collectedUtc.Length != 6)
            throw new InvalidOperationException("광장 생선 가방 데이터가 올바르지 않습니다.");
        foreach (int count in p.bag) if (count < 0) throw new InvalidOperationException("생선 수량이 올바르지 않습니다.");
        p.blocked ??= new(); p.receipts ??= new(); return p;
    }
    public bool IsBlocked(string id)
    { for(int i=0;i<blocked.Count;i++)if(blocked[i].id==id)return true;return false; }
    public OpenSpaceReceipt Find(string id) => receipts.Find(r => r.id == id);
    public OpenSpaceReceipt Apply(OpenSpaceRequest request, string utc)
    {
        if (request == null || string.IsNullOrEmpty(request.id)) throw new InvalidOperationException("처리 요청 ID가 없습니다.");
        var prior = Find(request.id); if (prior != null) return prior;
        var receipt = new OpenSpaceReceipt { id=request.id, kind=request.kind, fish=request.fish, motion=request.motion, utc=utc };
        if (request.kind == "catch" || request.kind == "collect" || request.kind == "feed")
        {
            if (request.fish < 0 || request.fish >= bag.Length) throw new InvalidOperationException("알 수 없는 생선입니다.");
            if (request.kind == "catch")
            {
                if (bag[request.fish] == int.MaxValue) throw new InvalidOperationException("생선 가방 수량 한도입니다.");
                bag[request.fish]++; receipt.coins = Mathf.Max(0, request.coins);
            }
            else
            {
                if (bag[request.fish] == 0) throw new InvalidOperationException("선택한 생선이 없습니다.");
                if (request.kind == "collect")
                {
                    if (!string.IsNullOrEmpty(collectedUtc[request.fish])) throw new InvalidOperationException("이미 등록한 생선입니다.");
                    collectedUtc[request.fish] = utc;
                }
                else
                {
                    if (request.motion < 0 || request.motion >= 6) throw new InvalidOperationException("알 수 없는 고양이 모션입니다.");
                    receipt.newMotion = (unlockedMotions & (1 << request.motion)) == 0;
                    unlockedMotions |= 1 << request.motion;
                }
                bag[request.fish]--;
            }
        }
        else if (request.kind == "help") helpSeen = true;
        else if (request.kind == "block")
        {
            if (string.IsNullOrEmpty(request.target)) throw new InvalidOperationException("차단 대상을 확인해주세요.");
            if (!IsBlocked(request.target)) blocked.Add(new OpenSpaceBlockedUser { id=request.target, nickname=request.nickname });
        }
        else if (request.kind == "unblock") blocked.RemoveAll(b => b.id == request.target);
        else throw new InvalidOperationException("알 수 없는 광장 요청입니다.");
        receipts.Add(receipt); return receipt;
    }
    public static int WeightedIndex(int[] weights, float unit)
    {
        int total=0; foreach (int weight in weights) total += Mathf.Max(0,weight);
        if (total <= 0) throw new InvalidOperationException("추첨 가중치가 비어 있습니다.");
        float at=Mathf.Clamp(unit, 0, .99999994f)*total;
        for (int i=0;i<weights.Length;i++) { at-=Mathf.Max(0,weights[i]); if(at<0)return i; }
        return weights.Length-1;
    }
}
