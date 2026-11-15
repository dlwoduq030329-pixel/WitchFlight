# Execute the production display/cache paths with TMP doubles, without Unity rendering.
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$hud = Get-Content Assets/Script/BattleHud.cs -Raw
function Method([string]$name) {
    $m = [regex]::Match($hud, '(?ms)^    private (?:static )?void ' + $name + '\([^{}]*?\)\r?\n    \{.*?^    \}')
    if (!$m.Success) { throw "Missing method $name" }
    $m.Value -replace '^    private ', '    public '
}
$source = @'
using System;
using System.Collections.Generic;
namespace HudMagicChecks {
public class TMP_Text {
 public string text; public int writes;
 public void SetCharArray(char[] buffer,int start,int length){text=new string(buffer,start,length);writes++;}
 public void SetText(string format,float a,float b){text=string.Format(format,a,b);writes++;}
}
public class Player {
 public bool IsRainCharging,RainFocused;
 public int CurrentMagicSlot=1,BladeCharges=3,RainTargetCount=2;
 public float RainManaSpent=25,RainChargeBudget=100;
 public string magic="화염 마법"; public string GetSelectedMagic()=>magic;
}
public class Probe {
 private sealed class NumericDisplay {public char[] characters=Array.Empty<char>();public int length=-1;}
 private readonly Dictionary<TMP_Text,NumericDisplay> numericDisplays=new();
 private char[] numericBuffer=new char[256];
 public TMP_Text selectedMagicText=new(); public Player owner=new();
 private string MagicName(string magic)=>magic;
 private static void SetText(TMP_Text label,string value){if(label!=null)label.text=value;}
'@
$source += (Method 'SetNumericText') + (Method 'BindSelectedMagicText')
$source += @'
}
public static class Checks {
 static int n;static void Check(bool ok,string message){if(!ok)throw new Exception(message);n++;}
 public static int Run(){
  var p=new Probe();p.BindSelectedMagicText(true);
  Check(p.selectedMagicText.text=="칼날 마법 · 3회","initial blade count");
  int writes=p.selectedMagicText.writes;
  for(int i=0;i<300;i++)p.BindSelectedMagicText(true);
  Check(p.selectedMagicText.writes==writes,"steady blade count does not rewrite TMP");
  p.owner.CurrentMagicSlot=3;p.BindSelectedMagicText(true);
  Check(p.selectedMagicText.text=="Parry","parry display");
  p.owner.CurrentMagicSlot=1;p.BindSelectedMagicText(true);
  Check(p.selectedMagicText.text=="칼날 마법 · 3회","same count restored after parry");
  p.owner.IsRainCharging=true;p.BindSelectedMagicText(true);
  Check(p.selectedMagicText.text=="칼날비 · 2명 · 충전 마나 25.0","rain charging display");
  p.owner.IsRainCharging=false;p.BindSelectedMagicText(true);
  Check(p.selectedMagicText.text=="칼날 마법 · 3회","same count restored after rain charging");
  p.owner.IsRainCharging=true;p.owner.RainFocused=true;p.BindSelectedMagicText(true);
  Check(p.selectedMagicText.text=="칼날 집중 · 충전 마나 25.0 / 100.0","focused rain display");
  p.owner.IsRainCharging=false;p.BindSelectedMagicText(true);
  Check(p.selectedMagicText.text=="칼날 마법 · 3회","same count restored after focused rain");
  var owner=p.owner;p.owner=null;p.BindSelectedMagicText(false);
  Check(p.selectedMagicText.text=="","despawn clears text without reading player");
  p.owner=owner;p.BindSelectedMagicText(true);
  Check(p.selectedMagicText.text=="칼날 마법 · 3회","same count restored after respawn");
  p.owner.BladeCharges=0;p.owner.magic="마법 {0}";p.BindSelectedMagicText(true);
  Check(p.selectedMagicText.text=="마법 {0}","authored names preserve literal braces");
  p.owner.BladeCharges=3;p.BindSelectedMagicText(true);
  Check(p.selectedMagicText.text=="칼날 마법 · 3회","same count restored after plain name");
  p.owner.BladeCharges=2;p.BindSelectedMagicText(true);
  Check(p.selectedMagicText.text=="칼날 마법 · 2회","charge use updates count");
  p.selectedMagicText=null;p.BindSelectedMagicText(true);Check(true,"missing optional label is safe");
  return n;
 }
}}
'@
Add-Type -TypeDefinition $source
"HUD magic text regression checks passed: $([HudMagicChecks.Checks]::Run())"
