# Exercises the production aim-point expression with System.Numerics transforms.
# This checks geometry, not Unity's live collider synchronization or networking.
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$player = Get-Content Assets/Script/Player.cs -Raw
$property = [regex]::Match($player, 'public Vector3 LockAimPoint => [^;]+;').Value
if (!$property) { throw 'Missing production LockAimPoint property' }
$property = $property.Replace('Vector3.up', 'Vector3.UnitY')
$source = @'
using System;
using System.Numerics;
namespace LockAimRotationChecks {
public class BodyTransform {
 public Vector3 position = new Vector3(11, 23, -17);
 public Vector3 scale = Vector3.One;
 public Quaternion rotation = Quaternion.Identity;
 public Vector3 TransformPoint(Vector3 point) => position + Vector3.Transform(point * scale, rotation);
}
public class Aim {
 public BodyTransform transform = new BodyTransform();
 public float lockAimHeight = .8f;
'@
$source += $property
$source += @'
}
public static class Checks {
 static int count;
 static void Near(Vector3 actual, Vector3 expected, string message) {
  if (Vector3.Distance(actual, expected) > .0001f) throw new Exception(message);
  count++;
 }
 public static int Run() {
  var aim = new Aim(); var body = aim.transform;
  Near(aim.LockAimPoint, body.position + new Vector3(0,.8f,0), "upright behavior preserved");
  body.rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, (float)Math.PI);
  Near(aim.LockAimPoint, body.position + new Vector3(0,-.8f,0), "inverted target stays below root with body");
  body.rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, (float)Math.PI/2);
  Near(aim.LockAimPoint, body.position + new Vector3(-.8f,0,0), "sideways roll follows body");
  body.rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitX, (float)Math.PI/2);
  Near(aim.LockAimPoint, body.position + new Vector3(0,0,.8f), "vertical flight follows pitch");
  body.scale = new Vector3(2,3,4);
  Near(aim.LockAimPoint, body.position + new Vector3(0,0,2.4f), "authored local height scales with character");
  aim.lockAimHeight = 0;
  Near(aim.LockAimPoint, body.position, "zero height stays at root in all orientations");
  return count;
 }
}
}
'@
Add-Type -TypeDefinition $source
"Lock aim rotation geometry checks passed: $([LockAimRotationChecks.Checks]::Run())"
