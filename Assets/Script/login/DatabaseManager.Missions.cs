using System;
using BackEnd;
using UnityEngine;

public partial class DatabaseManager
{
    private bool claimingMission;
    public bool TryClaimMission(string id, out string message)
    {
        message="";
        if(!CanAutoSave || claimingMission || IsItemPurchaseInProgress || IsPalettePurchaseInProgress || IsAttendanceInProgress) { message="계정 정보를 확인해주세요."; return false; }
        MissionProgression.EnsurePeriods();
        if(IsAutoSavePending) { message="진행 기록 저장 중입니다. 잠시 후 다시 눌러주세요."; return false; }
        var definition=MissionProgression.Find(id);
        if(definition==null) return false;
        claimingMission=true;
        try
        {
            var read=Backend.GameData.GetMyData(TABLE_NAME,new Where());
            if(!read.IsSuccess()) { message="미션 기록 조회 실패. 다시 시도해주세요."; return false; }
            var rows=read.FlattenRows();
            if(rows==null || rows.Count!=1 || ReadString(rows[0],"inDate","")!=currentPlayerSettingInDate)
                throw new InvalidOperationException("계정 데이터 행을 확인할 수 없습니다.");
            var server=ReadPlayerSetting(rows[0]);
            var state=MissionProgression.Parse(server.missionState);
            DateTime now=DateTime.UtcNow;
            bool current=definition.period==MissionPeriod.Daily ? state.dailyPeriodKey==MissionProgression.PeriodDay(now).ToString("yyyy-MM-dd") :
                state.weeklyPeriodKey==MissionProgression.PeriodWeek(now).ToString("yyyy-MM-dd") && state.weeklyMissionId==id;
            var entry=state.entries.Find(e=>e.id==id);
            if(!current || entry==null || entry.progress<definition.target) { message="아직 완료하지 않은 미션입니다."; return false; }
            if(entry.claimed) { ApplyMissionSnapshot(server.money,server.missionState); message="이미 수령한 보상입니다."; return false; }
            if(definition.rewardCurrency<0 || server.money>int.MaxValue-definition.rewardCurrency) { message="재화 보유 한도를 확인해주세요."; return false; }
            entry.claimed=true;
            string json=JsonUtility.ToJson(state); int balance=server.money+definition.rewardCurrency;
            var param=new Param(); param.Add("money",balance); param.Add("missionState",json);
            var saved=Backend.GameData.UpdateV2(TABLE_NAME,currentPlayerSettingInDate,loadedUserInDate,param);
            if(!saved.IsSuccess())throw new InvalidOperationException(saved.GetMessage());
            ApplyMissionSnapshot(balance,json); LastSaveSucceeded=true; LastError="";
            message=$"{definition.rewardCurrency:N0}원 수령 완료"; return true;
        }
        catch(Exception e)
        {
            // An uncertain response must not be followed by a stale profile autosave.
            IsDataConfigReady=false; LastSaveSucceeded=false;
            message="보상 저장 결과를 확인할 수 없습니다. 다시 로그인해주세요. "+e.Message;
            LastError=message; return false;
        }
        finally { claimingMission=false; TryStartAttendance(); }
    }
    private void ApplyMissionSnapshot(int balance,string json)
    {
        currentPlayerSetting.money=balance; currentPlayerSetting.missionState=json;
        using(DataConfig.BeginChangeBatch(false)) { DataConfig.money=balance; DataConfig.missionState=json; }
        MissionProgression.Load();
    }
}
