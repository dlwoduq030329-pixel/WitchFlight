using System;
using System.Collections.Generic;

public enum TitleCondition { Login, Matches, BotMatches, MagicKills, Underwater, Emotes, BlackDevil, Deaths, DoubleHit, HealthPacks, AltitudeDeaths, MatchChallenge, NoSwapMatches, EverySwapMatches, RaidClear }

[Flags]
public enum TitleChallenge
{
    None=0, PerfectAttack=1, PerfectDefense=2, NoSwap=4, EverySwap=8,
    LastSecond=16, Thriller=32, CatSlayer=64, Polite=128, Bulldozer=256,
    Pacifist=512, Evasive=1024, Saint=2048
}

public sealed class TitleDefinition
{
    public readonly int id, required;
    public readonly string name, description;
    public readonly TitleCondition condition;
    public readonly MagicType magic;
    public readonly bool hidden;
    public TitleDefinition(int id, string name, string description, TitleCondition condition, int required,
        MagicType magic = MagicType.None, bool hidden = false)
    { this.id=id; this.name=name; this.description=description; this.condition=condition; this.required=required; this.magic=magic; this.hidden=hidden; }
}

// IDs are permanent save/network identifiers. Do not renumber when adding titles.
public static class TitleCatalog
{
    public static readonly IReadOnlyList<TitleDefinition> All = Build();
    public static TitleDefinition Find(int id) { foreach(var title in All) if(title.id==id)return title; return null; }
    // Negative IDs are host-assigned bot labels, never unlockable account titles.
    public static bool IsBotTitle(int id) => id >= -3 && id <= -1;
    public static string Name(int id) => id switch
    {
        -1 => "온화한 전투봇",
        -2 => "상냥한 전투봇",
        -3 => "공격적인 전투봇",
        _ => Find(id)?.name ?? string.Empty
    };
    public static int ChooseBotTitle(IEnumerable<int> existing)
    {
        var counts = new int[3];
        foreach (int id in existing) if (IsBotTitle(id)) counts[-id - 1]++;
        int minimum = Math.Min(counts[0], Math.Min(counts[1], counts[2]));
        var candidates = new List<int>(3);
        for (int i = 0; i < 3; i++) if (counts[i] == minimum) candidates.Add(-i - 1);
        return candidates[UnityEngine.Random.Range(0, candidates.Count)];
    }
    private static List<TitleDefinition> Build()
    {
        var a = new List<TitleDefinition> {
            new(1,"먀오녀 집사","첫 로그인 시 획득",TitleCondition.Login,1),
            new(2,"신입 마녀","게임 10회 완료",TitleCondition.Matches,10),
            new(3,"선배 마녀","게임 25회 완료",TitleCondition.Matches,25),
            new(4,"수석 마녀","게임 50회 완료",TitleCondition.Matches,50),
            new(5,"수재 마녀","누적 봇전 10회 완료",TitleCondition.BotMatches,10)
        };
        AddMagic(a,6,MagicType.Fire,"화염","초급 불꽃 마녀","중급 불꽃 마녀","플레임 위자드",30);
        AddMagic(a,9,MagicType.Ice,"얼음","초급 얼음 마녀","중급 얼음 마녀","플레임 메이지",30);
        AddMagic(a,12,MagicType.Vision,"비전","초급 비전 마녀","중급 비전 마녀","아크 메이지",20);
        AddMagic(a,15,MagicType.Thunder,"천둥","초급 천둥 마녀","중급 천둥 마녀","크라우노맨서",20);
        AddMagic(a,18,MagicType.Dark,"암흑","초급 흑 마녀","중급 흑 마녀","어사일럼",20);
        AddMagic(a,21,MagicType.Curse,"저주","초급 저주 마녀","중급 저주 마녀","네크로멘서",20);
        AddMagic(a,24,MagicType.Razier,"광선","초급 백색 마녀","중급 백색 마녀","루미너스",20);
        a.Add(new(27,"크라켄","누적 물속에 머문 시간 1시간 달성",TitleCondition.Underwater,3600,hidden:true));
        a.Add(new(28,"소통해요~","한 게임에서 감정표현 10회",TitleCondition.Emotes,10,hidden:true));
        a.Add(new(29,"검은 악마","검은색 코스튬을 장착하고 암흑 또는 저주 마법을 하나 이상 장착한 상태로 한 게임에서 10킬",TitleCondition.BlackDevil,10,hidden:true));
        a.Add(new(30,"보충반","누적 사망 50회",TitleCondition.Deaths,50,hidden:true));
        a.Add(new(31,"슈퍼스타","1초 내에 적의 공격을 2회 받기",TitleCondition.DoubleHit,1,hidden:true));
        a.Add(new(32,"난 아직 배가 고프다","한 게임에서 힐팩 10개 획득",TitleCondition.HealthPacks,10,hidden:true));
        a.Add(new(33,"자유를 찾아서","한 게임에서 맵 상단 한계를 넘어 사망 3회",TitleCondition.AltitudeDeaths,3,hidden:true));
        a.Add(new(34,"막을 수 없는 지팡이","한 경기에서 공격 마법을 1회 이상 시전하고 모든 시전에서 적에게 1회 이상 적중한 뒤 경기 완료",TitleCondition.MatchChallenge,1,hidden:true));
        a.Add(new(35,"뚫을 수 없는 방패","한 경기에서 적 공격을 1회 이상 방어하고, 방어하지 못한 적 공격 없이 경기 완료",TitleCondition.MatchChallenge,1,hidden:true));
        a.Add(new(36,"고집불통","마법을 한 번도 교체하지 않고 완료한 경기 누적 10회",TitleCondition.NoSwapMatches,10,hidden:true));
        a.Add(new(37,"팔랑귀","매 라운드 사이에 마법을 1개 이상 교체한 경기 누적 10회 완료",TitleCondition.EverySwapMatches,10,hidden:true));
        a.Add(new(38,"끝날때까지 끝난게 아니다","마지막 라운드 종료 5초 이내에 깃발을 획득하고, 보유한 채 경기 승리",TitleCondition.MatchChallenge,1,hidden:true));
        a.Add(new(39,"스릴러","마지막 라운드에서 체력 10 이하로 깃발을 보유한 채 경기 승리",TitleCondition.MatchChallenge,1,hidden:true));
        a.Add(new(40,"하극상","한 경기에서 적 먀오녀 10회 처치 후 경기 완료",TitleCondition.MatchChallenge,1,hidden:true));
        a.Add(new(41,"예의 바른 마녀","먀오녀가 10회 소환되는 동안 먀오녀를 한 번도 공격하지 않고 경기 완료",TitleCondition.MatchChallenge,1,hidden:true));
        a.Add(new(42,"불도저","5초 안에 장애물에 3회 충돌",TitleCondition.MatchChallenge,1,hidden:true));
        a.Add(new(43,"평화주의자","모든 라운드에서 공격 마법을 장착하지 않고 한 경기 완료",TitleCondition.MatchChallenge,1,hidden:true));
        a.Add(new(44,"회피형","모든 라운드에서 플레어와 스모크 조합으로 한 경기 완료",TitleCondition.MatchChallenge,1,hidden:true));
        a.Add(new(45,"성녀","한 경기에서 가한 피해량 0, 실제 마법 회복량 10,000 이상으로 경기 완료",TitleCondition.MatchChallenge,1,hidden:true));
        a.Add(new(46,"마녀 레인저","거대 먀오녀 토벌 성공",TitleCondition.RaidClear,1));
        return a;
    }
    private static void AddMagic(List<TitleDefinition> a,int id,MagicType magic,string label,string first,string second,string third,int middle)
    {
        a.Add(new(id,first,$"누적 {label} 마법으로 10킬",TitleCondition.MagicKills,10,magic));
        a.Add(new(id+1,second,$"누적 {label} 마법으로 {middle}킬",TitleCondition.MagicKills,middle,magic));
        a.Add(new(id+2,third,$"누적 {label} 마법으로 100킬",TitleCondition.MagicKills,100,magic));
    }
}

[Serializable]
public sealed class TitleMatchSnapshot
{
    public string id;
    public bool completed, botMatch, doubleHit;
    public int deaths, emotes, blackKills, healthPacks, altitudeDeaths;
    public float underwater;
    public int[] magicKills = new int[18];
    public int challengeFlags;
}

[Serializable]
public sealed class TitleProgress
{
    public int version=1, matches, botMatches, deaths;
    public int noSwapMatches, everySwapMatches;
    public float underwater;
    public int[] magicKills=new int[18];
    public List<int> unlocked=new();
    public TitleMatchSnapshot lastMatch;
    public bool Owns(int id) => unlocked != null && unlocked.Contains(id);
    public void Normalize()
    {
        unlocked ??= new();
        if(magicKills==null) magicKills=new int[18];
        else if(magicKills.Length<18) Array.Resize(ref magicKills,18);
    }
    public void GrantLogin() { Normalize(); if(!Owns(1))unlocked.Add(1); }
    // Authority snapshots are cumulative within the whole match, across all rounds.
    // Persisting the last applied snapshot makes repeated callbacks/reloads idempotent.
    public bool Apply(TitleMatchSnapshot next)
    {
        Normalize();
        if(next==null || string.IsNullOrEmpty(next.id))return false;
        var prev=lastMatch!=null && lastMatch.id==next.id ? lastMatch : new TitleMatchSnapshot();
        if(prev.completed)return false; // Final snapshot is immutable.
        // Ignore older network snapshots rather than moving the saved watermark backwards.
        if(next.deaths<prev.deaths || next.underwater<prev.underwater || next.emotes<prev.emotes ||
            next.healthPacks<prev.healthPacks || next.altitudeDeaths<prev.altitudeDeaths || next.blackKills<prev.blackKills)return false;
        for(int i=1;i<18;i++)if(At(next.magicKills,i)<At(prev.magicKills,i))return false;
        deaths=Add(deaths,Math.Max(0,next.deaths-prev.deaths));
        underwater=Math.Min(100000000f,underwater+Math.Max(0,next.underwater-prev.underwater));
        for(int i=1;i<18;i++)magicKills[i]=Add(magicKills[i],Math.Max(0,At(next.magicKills,i)-At(prev.magicKills,i)));
        if(next.completed)
        {
            matches=Add(matches,1);if(next.botMatch)botMatches=Add(botMatches,1);
            if((next.challengeFlags&(int)TitleChallenge.NoSwap)!=0)noSwapMatches=Add(noSwapMatches,1);
            if((next.challengeFlags&(int)TitleChallenge.EverySwap)!=0)everySwapMatches=Add(everySwapMatches,1);
        }
        foreach(var t in TitleCatalog.All)
        {
            float progress=t.condition switch {
                TitleCondition.Login=>1, TitleCondition.Matches=>matches, TitleCondition.BotMatches=>botMatches,
                TitleCondition.MagicKills=>At(magicKills,(int)t.magic), TitleCondition.Underwater=>underwater,
                TitleCondition.Deaths=>deaths, TitleCondition.Emotes=>next.emotes, TitleCondition.BlackDevil=>next.blackKills,
                TitleCondition.DoubleHit=>next.doubleHit?1:0, TitleCondition.HealthPacks=>next.healthPacks,
                TitleCondition.AltitudeDeaths=>next.altitudeDeaths,
                TitleCondition.NoSwapMatches=>noSwapMatches,TitleCondition.EverySwapMatches=>everySwapMatches,
                TitleCondition.MatchChallenge=>(next.challengeFlags&(1<<(t.id-34)))!=0?1:0, _=>0 };
            if(progress>=t.required && !Owns(t.id))unlocked.Add(t.id);
        }
        lastMatch=next;
        return true;
    }
    private static int At(int[] values,int index)=>values!=null && index<values.Length?values[index]:0;
    private static int Add(int a,int b)=>(int)Math.Min(100000000L,(long)a+b);
}
