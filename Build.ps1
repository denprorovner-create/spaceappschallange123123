param([string]$OutputPath = 'Perilune.exe')
$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
$framework = Join-Path $env:windir 'Microsoft.NET/Framework64/v4.0.30319'
$references = @('WindowsBase.dll','PresentationCore.dll','PresentationFramework.dll') | ForEach-Object { '/reference:' + (Join-Path $framework ('WPF/' + $_)) }
& (Join-Path $framework 'csc.exe') /nologo /reference:System.Xaml.dll /target:winexe /platform:x64 /optimize+ ("/out:" + $OutputPath) @references Game.cs Scene.cs SceneDeployment.cs SceneMars.cs Simulation.cs SolarSystem.cs LunarTransfer.cs Ascent.cs LaunchUI.cs AssemblyUI.cs SystemMap.cs AscentChart.cs FlightInstruments.cs MissionAnalysis.cs MissionUI.cs Mars.cs MarsAtmosphere.cs MarsCruise.cs MarsSiteData.cs MarsUI.cs MarsMissionUI.cs MarsSurfaceMap.cs Verification.cs
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed.' }
