$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$logic = Get-Content (Join-Path $repo 'Assets/Script/BroomAnimationStateMachine.cs') -Raw
$tests = @'
public static class BroomAnimationChecks
{
    static int checks;
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
    static BroomAnimationSnapshot Active() => new BroomAnimationSnapshot { Alive = true, Playing = true, WeaponKey = 101 };
    static BroomAnimationStateMachine Create(BroomAnimationSnapshot input)
    {
        var machine = new BroomAnimationStateMachine(_ => 0.1f);
        machine.Step(input, 0);
        return machine;
    }
    static void Steps(BroomAnimationStateMachine m, BroomAnimationSnapshot input, int count)
    { for (int i = 0; i < count; i++) m.Step(input, 0.11f); }
    public static int Run()
    {
        var s = Active(); var m = Create(s);
        Check(m.Motion == BroomMotion.ArmIdle, "Default pose must be armed idle.");
        int revision = m.Revision; Steps(m, s, 60);
        Check(m.Revision == revision, "Stable idle must not cross-fade/restart each frame.");
        s.TopSpeed = true; m.Step(s, 0);
        Check(m.Motion == BroomMotion.Disarm && m.UsesBothHands, "Top speed first disarms.");
        Steps(m, s, 1); Check(m.Motion == BroomMotion.ToFast, "Disarm leads into fast transition.");
        Steps(m, s, 1); Check(m.Motion == BroomMotion.FastIdle, "Transition reaches fast idle.");
        s.TopSpeed = false; m.Step(s, 0);
        Check(m.Motion == BroomMotion.FromFast, "Leaving top speed plays reverse transition.");
        Steps(m, s, 1); Check(m.Motion == BroomMotion.ArmIdle && !m.UsesBothHands, "Returns to default one-hand pose.");
        s.TopSpeed = true; Steps(m, s, 2); s.TopSpeed = false; m.Step(s, 0);
        Check(m.Motion == BroomMotion.FromFast, "Fast transition handles a quick speed reversal.");
        Steps(m, s, 1);
        s.Disarmed = true; s.WeaponKey = 300; Steps(m, s, 2);
        Check(m.Motion == BroomMotion.DisarmedIdle, "Fixed parry/no-weapon slot uses two-hand idle.");
        s.Disarmed = false; s.WeaponKey = 202; m.Step(s, 0);
        Check(m.Motion == BroomMotion.Equip, "Weapon swap plays Equip, not an attack.");
        Steps(m, s, 1);
        s.Attacking = true; m.Step(s, 0);
        Check(m.Motion == BroomMotion.AttackStart, "Accepted lock/channel starts attack animation.");
        Steps(m, s, 1); revision = m.Revision; Steps(m, s, 30);
        Check(m.Motion == BroomMotion.AttackLoop && m.Revision == revision, "Held attack loops without restarts.");
        s.Attacking = false; s.CastSequence++; m.Step(s, 0); Steps(m, s, 2);
        Check(m.Motion == BroomMotion.AttackEnd, "Release fires without restarting the charge; ends attack.");
        Steps(m, s, 1); Check(m.Motion == BroomMotion.ArmIdle, "Attack completion restores idle.");
        s.CastSequence++; m.Step(s, 0);
        Check(m.Motion == BroomMotion.AttackStart, "Instant cast has start animation without held input.");
        Steps(m, s, 1); Check(m.Motion == BroomMotion.AttackLoop, "Instant cast visits loop.");
        Steps(m, s, 2); Check(m.Motion == BroomMotion.ArmIdle, "Instant cast ends without holding pose forever.");
        s.Parrying = true; s.ParryStartSequence++; m.Step(s, 0);
        Check(m.Motion == BroomMotion.ParryStart, "Accepted parry starts the block even without a successful block.");
        Steps(m, s, 1); Check(m.Motion == BroomMotion.ParryLoop, "Block sustains during the actual parry window.");
        s.Parrying = false; m.Step(s, 0); Check(m.Motion == BroomMotion.ParryEnd, "Window expiry ends the block.");
        Steps(m, s, 1);
        s.ParryStartSequence++; m.Step(s, 0);
        Check(m.Motion == BroomMotion.ParryStart, "Missed short timer still shows replicated accepted parry event.");
        Steps(m, s, 3); s.HitSequence++; m.Step(s, 0);
        Check(m.Motion == BroomMotion.Hit, "Damage event plays hit reaction.");
        s.Alive = false; s.DeathSequence++; m.Step(s, 0);
        Check(m.Motion == BroomMotion.DeathStart, "Death interrupts hit reaction.");
        Steps(m, s, 1); revision = m.Revision; Steps(m, s, 30);
        Check(m.Motion == BroomMotion.DeathLoop && m.Revision == revision, "Death loops until real respawn, not clip timeout.");
        s.Alive = true; m.Step(s, 0); Check(m.Motion == BroomMotion.DeathEnd, "Respawn triggers knock-end.");
        Steps(m, s, 1); Check(m.Motion == BroomMotion.ArmIdle, "Recovery restores idle.");
        s = Active(); s.Playing = false; m = Create(s); s.Intro = true; m.Step(s, 0);
        Check(m.Motion == BroomMotion.TakeStick, "VS intro picks up broom.");
        Steps(m, s, 1); Check(m.Motion == BroomMotion.Takeoff, "Pickup leads into takeoff before countdown.");
        Steps(m, s, 1); Check(m.Motion == BroomMotion.ArmIdle, "Takeoff finishes before countdown.");
        s.Intro = false; m.Step(s, 0); s.Intro = true; m.Step(s, 0);
        Check(m.Motion == BroomMotion.TakeStick, "Subsequent round intro can take off again.");
        s.Intro = false; m.Step(s, 0);
        Check(m.Motion == BroomMotion.ArmIdle, "A shortened intro cannot spill into countdown.");
        s = Active(); s.CastSequence = 80; s.HitSequence = 20; s.DeathSequence = 3; s.ParryStartSequence = 10;
        m = Create(s); Check(m.Motion == BroomMotion.ArmIdle, "Late-join snapshot does not replay historical events.");
        s.Alive = false; m = Create(s); Check(m.Motion == BroomMotion.DeathLoop, "Late join observes current death loop.");
        s = Active(); s.Respawning = true; m = Create(s);
        Check(m.Motion == BroomMotion.DeathEnd, "A newly spawned replacement body plays recovery, not initial idle.");
        Steps(m, s, 5); Check(m.Motion == BroomMotion.ArmIdle, "Respawn presentation is one-shot even if timer remains active.");
        s = Active(); m = Create(s); s.Attacking = true; Steps(m, s, 2);
        s.WeaponKey = 202; m.Step(s, 0);
        Check(m.Motion == BroomMotion.AttackLoop, "Swap cannot interrupt sustained combat animation.");
        s.Attacking = false; Steps(m, s, 3);
        Check(m.Motion == BroomMotion.Equip, "Swap deferred by attack still plays afterwards.");
        s.Playing = false; m.Step(s, 0);
        Check(m.Motion == BroomMotion.ArmIdle, "Intermission cancels unfinished action visuals.");
        return checks;
    }
}
'@
Add-Type -TypeDefinition ($logic + $tests)
"Broom animation behavior checks passed: $([BroomAnimationChecks]::Run())"

# Verify real Animator states, clip links, loop flags and per-state speed binding.
$driver = Get-Content (Join-Path $repo 'Assets/Script/PlayerBroomAnimation.cs') -Raw
$controller = Get-Content (Join-Path $repo 'Assets/Animations/MainAnimator.controller') -Raw
$names = @([regex]::Matches($driver, '=> "(AS_Broom_[^"]+)"') | ForEach-Object { $_.Groups[1].Value })
if ($names.Count -ne 19) { throw 'Expected 19 explicitly mapped broom motions.' }
$states = [regex]::Matches($controller, '(?ms)^--- !u!1102 &(-?\d+)\r?\n.*?(?=^--- !u!|\z)')
$layer = [regex]::Match($controller, '(?ms)^--- !u!1107 &-1928226159083787227\r?\n.*?(?=^--- !u!|\z)').Value
$clips = Get-ChildItem (Join-Path $repo 'Assets/Animations/Art/BroomAnimation') -Recurse -Filter '*.anim'
foreach ($name in $names) {
    $matching = @($states | Where-Object { $_.Value -match "(?m)^  m_Name: $([regex]::Escape($name))\r?$" })
    if ($matching.Count -ne 1) { throw "Missing/duplicate Animator state: $name" }
    $state = $matching[0]
    if (!$layer.Contains('m_State: {fileID: ' + $state.Groups[1].Value + '}')) { throw "State is not on the Ingame layer: $name" }
    if ($state.Value -notmatch 'm_SpeedParameterActive: 1' -or $state.Value -notmatch 'm_SpeedParameter: BroomActionSpeed') { throw "Missing action speed binding: $name" }
    $clip = $clips | Where-Object BaseName -eq $name | Select-Object -First 1
    if (!$clip) { throw "Missing clip: $name" }
    $guid = [regex]::Match((Get-Content ($clip.FullName + '.meta') -Raw), 'guid: (\w+)').Groups[1].Value
    if (!$state.Value.Contains('guid: ' + $guid)) { throw "Wrong clip assigned to $name" }
    $clipText = Get-Content $clip.FullName -Raw
    $loop = $name.EndsWith('_Idle') -or $name.EndsWith('_Loop')
    if ($clipText -notmatch ('m_LoopTime: ' + [int]$loop)) { throw "Wrong loop flag: $name" }
}
"Broom animation asset checks passed: $($names.Count) mapped states/clips"

$player = Get-Content (Join-Path $repo 'Assets/Script/Player.cs') -Raw
if ($player -notmatch 'broomAnimation.Render\(animator, snapshot' -or $player -notmatch 'ParryStartSequence\+\+') { throw 'Missing Player integration.' }
if ($player -match 'PlayAnimatorState\(') { throw 'Legacy animator writer still conflicts with the new presentation.' }
if ($driver -notmatch 'animator.applyRootMotion = false') { throw 'Root motion must not compete with network movement.' }
$battle = Get-Content (Join-Path $repo 'Assets/Script/vibe/BattleManager.cs') -Raw
if ($battle -notmatch 'SpawnBattlePlayer\(pending.PlayerRef, playerData, playerPrefab, true\)' -or
    $player -notmatch 'Respawning = TimerIsActive\(RespawnAnimationTimer\)') { throw 'New respawn body is not wired for the recovery animation.' }
if ($controller -notmatch 'm_Name: BroomActionSpeed\r?\n    m_Type: 1\r?\n    m_DefaultFloat: 1') { throw 'Missing speed parameter or incorrect default.' }
'Broom animation integration checks passed'
