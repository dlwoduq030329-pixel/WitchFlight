param([string]$UnityEditorData = 'C:/Program Files/Unity/Hub/Editor/6000.0.77f1/Editor/Data')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$outputDirectory = Join-Path $projectRoot 'tmp'
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$executable = Join-Path $outputDirectory 'LilToonSettingsFileRegression.exe'
$references = Join-Path $UnityEditorData 'UnityReferenceAssemblies/unity-4.8-api'
$compilerArguments = @(
    (Join-Path $UnityEditorData 'DotNetSdkRoslyn/csc.dll'),
    '/nologo', '/target:exe', '/nostdlib+', '/define:UNITY_EDITOR',
    "/out:$executable",
    "/r:$(Join-Path $references 'mscorlib.dll')",
    "/r:$(Join-Path $references 'System.dll')",
    "/r:$(Join-Path $references 'System.Core.dll')",
    (Join-Path $projectRoot 'Packages/jp.lilxyzw.liltoon/Editor/lilSettingFileWriter.cs'),
    (Join-Path $PSScriptRoot 'LilToonSettingsFileRegression.cs')
)
& (Join-Path $UnityEditorData 'NetCoreRuntime/dotnet.exe') @compilerArguments
if ($LASTEXITCODE -ne 0) { throw 'lilToon regression test compilation failed.' }
& (Join-Path $UnityEditorData 'MonoBleedingEdge/bin/mono.exe') $executable
if ($LASTEXITCODE -ne 0) { throw 'lilToon settings regression failed.' }
