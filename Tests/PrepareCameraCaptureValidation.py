"""Prepare actual production methods for the isolated Unity validation project.

Run from the repository root, then launch that project with
-batchmode -nographics -executeMethod CameraCaptureValidation.Run -quit.
The main project/scene is never opened by this check.
"""
from pathlib import Path
import shutil

root = Path(__file__).resolve().parent.parent
editor = root / ".utmp/battle-sky-unity/Assets/Editor"
if not editor.is_dir():
    raise SystemExit("The isolated URP validation project must exist first.")


def method(source, signature):
    start = source.index(signature)
    cursor = source.index("{", start) + 1
    depth = 1
    while depth:
        depth += (source[cursor] == "{") - (source[cursor] == "}")
        cursor += 1
    return source[start:cursor].replace("private static", "public static").replace("internal static", "public static")


rear = (root / "Assets/Script/Camera/RearThreatCamera.cs").read_text(encoding="utf-8-sig")
effects = (root / "Assets/SUIMONO - WATER SYSTEM 2/SCRIPTS/SuimonoModuleFX.cs").read_text(encoding="utf-8-sig")
methods = "\n".join(method(rear, signature) for signature in (
    "private static void SyncRenderSettings(Camera source, Camera destination,",
    "private static void PreparePostProcessing(",
    "internal static void RemoveThreatBlur(",
))
methods += "\npublic readonly System.Collections.Generic.List<ParticleSystem> screenCaptureParticles = new();\npublic bool screenCaptureParticlesReady;\n"
methods += method(effects, "public bool HasLiveScreenEffects()")
(editor / "CameraCaptureProduction.cs").write_text(
    "using UnityEngine; using UnityEngine.Rendering; using UnityEngine.Rendering.Universal;\n"
    "public class CameraCaptureProduction {\n" + methods + "\n}", encoding="utf-8"
)
shutil.copyfile(root / "Tests/CameraCaptureValidation.cs", editor / "CameraCaptureValidation.cs")
