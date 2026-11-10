# Actual tuning-copy method; verify enum mapping and preservation of authored metadata.
$ErrorActionPreference='Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$source=Get-Content Assets/Script/MagicBalanceLabSave.cs -Raw
$method=[regex]::Match($source,'(?ms)^    public static void CopyTuning\([^{}]*?\)\r?\n    \{.*?^    \}').Value
if(!$method){throw 'Missing tuning copy method'}
$code=@'
using System;
namespace BalanceSaveChecks {
public struct MagicStatEntry {
 public int magic; public string icon,description; public object vfx;
 public float projectileSpeed,projectileTurnSpeed,range,projectileLifetime,damage,apCost,cooldownSeconds;
}
public struct BroomStatEntry {
 public int broom;public string displayName;public float maxSpeed,turnSpeed,boostMultiplier,maxHp,ramDamageOverride;
}
public struct HatStatEntry {
 public int hat;public string description;public float maxAp,apRecoveryPerSecond;
}
public class MagicStatTable {
 public bool useIndividualHomingTurnSpeeds;
 public float homingTurnSpeed,homingSpeedLossPerSecond,homingSpeedLossPerDegree,homingTravelDistanceMultiplier,
 homingLifetimeMultiplier,homingLeadSeconds,homingFallThreshold;
 public float homingTurnReferenceSpeed,homingTurnSpeedExponent,homingMaxTurnLossPerSecond;
 public MagicStatEntry[] magics;
 public object unrelatedPrefab;
}
public class EquipmentStatTable {public BroomStatEntry[] brooms;public HatStatEntry[] hats;}
public static class Harness {
 __METHOD__
 static int checks;
 static void Check(bool condition,string name){checks++;if(!condition)throw new Exception(name);}
 public static string Run(){
  var vfx=new object();var unrelated=new object();
  var src=new MagicStatTable{useIndividualHomingTurnSpeeds=true,homingTurnSpeed=411,homingSpeedLossPerSecond=.12f,
   homingSpeedLossPerDegree=.02f,homingTravelDistanceMultiplier=2.7f,homingLifetimeMultiplier=1.8f,
   homingLeadSeconds=4.2f,homingFallThreshold=.23f,
   homingTurnReferenceSpeed=70,homingTurnSpeedExponent=2.3f,homingMaxTurnLossPerSecond=.28f,
   magics=new[]{new MagicStatEntry{magic=1,projectileSpeed=80,projectileTurnSpeed=345,range=142,
    projectileLifetime=6.5f,damage=72,apCost=19,cooldownSeconds=4,icon="old",description="old"},
    new MagicStatEntry{magic=2,damage=91}}};
  var dst=new MagicStatTable{unrelatedPrefab=unrelated,magics=new[]{
   new MagicStatEntry{magic=2,icon="new icon"},
   new MagicStatEntry{magic=1,icon="author icon",description="new description",vfx=vfx},
   new MagicStatEntry{magic=99,damage=777}}};
  var equipment=new EquipmentStatTable{brooms=new[]{new BroomStatEntry{broom=2,maxSpeed=61,turnSpeed=122,boostMultiplier=1.9f,maxHp=234}},
   hats=new[]{new HatStatEntry{hat=3,maxAp=210,apRecoveryPerSecond=15}}};
  var target=new EquipmentStatTable{brooms=new[]{new BroomStatEntry{broom=9,maxSpeed=13},
   new BroomStatEntry{broom=2,displayName="renamed",ramDamageOverride=100}},
   hats=new[]{new HatStatEntry{hat=3,description="author text"},new HatStatEntry{hat=8,maxAp=31}}};
  var originalArray=dst.magics;
  CopyTuning(src,equipment,dst,target);
  Check(ReferenceEquals(originalArray,dst.magics) && dst.magics.Length==3,"No array replacement or dropped new entries");
  Check(dst.magics[0].magic==2 && dst.magics[0].damage==91,"Magic mapping by enum, not array index");
  var m=dst.magics[1];
  Check(m.projectileSpeed==80 && m.projectileTurnSpeed==345 && m.range==142 && m.projectileLifetime==6.5f,"All projectile controls saved");
  Check(m.damage==72 && m.apCost==19 && m.cooldownSeconds==4,"All combat controls saved");
  Check(m.icon=="author icon" && m.description=="new description" && ReferenceEquals(m.vfx,vfx),"Authored icons, descriptions and VFX preserved");
  Check(dst.magics[2].damage==777,"New magic absent from lab preserved");
  Check(dst.useIndividualHomingTurnSpeeds && dst.homingTurnSpeed==411 && dst.homingSpeedLossPerSecond==.12f &&
   dst.homingSpeedLossPerDegree==.02f && dst.homingTravelDistanceMultiplier==2.7f && dst.homingLifetimeMultiplier==1.8f &&
   dst.homingLeadSeconds==4.2f && dst.homingFallThreshold==.23f,"Global guidance controls saved");
  Check(ReferenceEquals(dst.unrelatedPrefab,unrelated),"Unrelated global settings preserved");
  Check(dst.homingTurnReferenceSpeed==70 && dst.homingTurnSpeedExponent==2.3f && dst.homingMaxTurnLossPerSecond==.28f,"Speed steering and rate limit survive save");
  var b=target.brooms[1];
  Check(b.maxSpeed==61 && b.turnSpeed==122 && b.boostMultiplier==1.9f && b.maxHp==234,"Broom controls saved by enum");
  Check(b.ramDamageOverride==100 && b.displayName=="renamed" && target.brooms[0].maxSpeed==13,"Unedited broom fields and new entries preserved");
  Check(target.hats[0].maxAp==210 && target.hats[0].apRecoveryPerSecond==15,"Hat controls saved");
  Check(target.hats[0].description=="author text" && target.hats[1].maxAp==31,"Hat descriptions and new entries preserved");
  src.magics[0].damage=1;Check(dst.magics[1].damage==72,"Later unsaved changes do not modify saved data");
  return checks+" save checks passed";
 }
}
}
'@
Add-Type -TypeDefinition ($code.Replace('__METHOD__',$method))
[BalanceSaveChecks.Harness]::Run()
