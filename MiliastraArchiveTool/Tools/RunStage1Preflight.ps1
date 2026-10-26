param()

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$projectPath = Join-Path $repoRoot "MiliastraArchiveTool\MiliastraArchiveTool.csproj"
$projectDirectory = Join-Path $repoRoot "MiliastraArchiveTool"
$framework = "net8.0-windows10.0.19041.0"
$outputDirectory = Join-Path $repoRoot "runtime-data\MiliastraArchiveTool\logs\diagnostics"
$reportPath = Join-Path $outputDirectory "stage1-preflight-report.json"
$summaryPath = Join-Path $outputDirectory "stage1-preflight-summary.md"
$runnerRoot = Join-Path ([System.IO.Path]::GetTempPath()) "MiliastraArchiveTool\stage1-preflight-runner"

Write-Host "Building MiliastraArchiveTool..."
dotnet build $projectPath --nologo

if (Test-Path $runnerRoot) {
    Remove-Item -LiteralPath $runnerRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $runnerRoot | Out-Null

$runnerProjectPath = Join-Path $runnerRoot "Stage1PreflightRunner.csproj"
$runnerProgramPath = Join-Path $runnerRoot "Program.cs"

@"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>$framework</TargetFramework>
    <UseWPF>true</UseWPF>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="$projectPath" />
  </ItemGroup>
</Project>
"@ | Set-Content -Path $runnerProjectPath -Encoding UTF8

@"
using MiliastraArchiveTool.Automation.Workflows.Diagnostics;

var outputDirectory = @`"$outputDirectory`";
var report = await Stage1PreflightCheckSelfCheck.RunAsync(outputDirectory);

Console.WriteLine($"ReadyForStage1CurrentServerStart: {report.ReadyForStage1CurrentServerStart}");
Console.WriteLine($"RawPage: {report.RawPage}");
Console.WriteLine($"GatedPage: {report.GatedPage}");
Console.WriteLine($"Message: {report.Message}");
Console.WriteLine($"ScreenshotPath: {report.ScreenshotPath ?? "<none>"}");
"@ | Set-Content -Path $runnerProgramPath -Encoding UTF8

Write-Host "Running Stage1 preflight..."
dotnet run --project $runnerProjectPath --nologo

if (!(Test-Path $reportPath)) {
    throw "Preflight report was not generated: $reportPath"
}
if (!(Test-Path $summaryPath)) {
    throw "Preflight summary was not generated: $summaryPath"
}

Write-Host ""
Write-Host "Report:"
Write-Host $reportPath
Write-Host "Summary:"
Write-Host $summaryPath
