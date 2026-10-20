# Trials of the two report steps of the run of f06412e, the story of visit 1
# (passed) and the executive summary (failed on its wording in all four
# answers), on four models or modes: the task's own profile with thinking
# off, as the run was; the same with thinking on; Gemma 4 26B A4B with
# thinking off; and Gemma 4 with thinking on. Each is tried at temperature 0,
# then at 0.7 with seeds 1 to 3, as the task's recovery would. Run
# Add-Gemma4Profile.ps1 first. Nothing is written to the catalog. Run in an
# ordinary PowerShell with the player closed. It takes some hours; the
# results so far are kept if it is stopped.
$ErrorActionPreference = 'Stop'
Set-Location 'C:\Users\Public\Downloads'
$rev = 'ad487b2'
$task = '01a1566e-3cd9-7651-84e9-936e9059764f'
$folder = ".\wr-$rev"
if (Test-Path $folder) { Remove-Item $folder -Recurse -Force }
Expand-Archive ".\windows-a11y-recorder-$rev-package.zip" $folder
$source = (Resolve-Path "$folder\windows-a11y-recorder-$rev").Path
Copy-Item .\wr-0015da1\windows-a11y-recorder-0015da1\.postgres "$source\.postgres" -Recurse
$dotnet = "$env:USERPROFILE\.dotnet\dotnet.exe"
# The worker is run through this dotnet, as .NET is in the user's folder
# and not registered, so the worker's own launcher does not find it.
$env:DOTNET_ROOT = "$env:USERPROFILE\.dotnet"
& $dotnet build "$source\src\Recorder.AnalysisWorker\Recorder.AnalysisWorker.csproj" --configuration Release
if ($LASTEXITCODE -ne 0) { throw 'The worker did not build.' }
$worker = Get-ChildItem "$source\src\Recorder.AnalysisWorker\bin\Release" -Recurse -Filter 'Recorder.AnalysisWorker.dll' | Select-Object -First 1
$out = "C:\Users\Public\Downloads\trials-$rev"
New-Item -ItemType Directory -Force $out | Out-Null
$summary = "$out\summary.txt"
Set-Content -Path $summary -Encoding UTF8 -Value 'Model or mode | Step | Sampling | Outcome | Time, s | Output tokens | Thinking, characters'

# Thinking holds the thinking and the answer within the output limit, so
# it is raised to 16,384 tokens. Qwen's model card gives top_p 0.95 for
# thinking, 0.80 without; Gemma's profile has Google's top_p 0.95, top_k 64.
$models = @(
    @{ Name = 'qwen-off'; Args = @() },
    @{ Name = 'qwen-on'; Args = @('thinking=on', 'max_tokens=16384', 'top_p=0.95') },
    @{ Name = 'gemma-off'; Args = @('profile=Gemma 4 26B A4B') },
    @{ Name = 'gemma-on'; Args = @('profile=Gemma 4 26B A4B', 'thinking=on', 'max_tokens=16384') })
$steps = @('visit-0001-story', 'executive-summary')
$samplings = @(
    @{ Name = 't0'; Args = @() },
    @{ Name = 'seed1'; Args = @('temperature=0.7', 'seed=1') },
    @{ Name = 'seed2'; Args = @('temperature=0.7', 'seed=2') },
    @{ Name = 'seed3'; Args = @('temperature=0.7', 'seed=3') })
$total = $models.Count * $steps.Count * $samplings.Count
$n = 0
foreach ($model in $models) {
    foreach ($step in $steps) {
        foreach ($sampling in $samplings) {
            $n++
            $file = "$out\$($model.Name)-$step-$($sampling.Name).txt"
            Write-Host "Trial $n of ${total}: $($model.Name), $step, $($sampling.Name)"
            & $dotnet $worker.FullName --trial $task $step $file @($model.Args + $sampling.Args)
            if (-not (Test-Path $file)) {
                Write-Host '  No result was written, so the trials stop here. The message above says why.'
                exit 1
            }
            $outcome = (Select-String -Path $file -Pattern '^Outcome: (.*)$' | Select-Object -First 1).Matches[0].Groups[1].Value
            $seconds = (Select-String -Path $file -Pattern '^Time taken, as the worker measured it: (.*) s$' | Select-Object -First 1).Matches[0].Groups[1].Value
            $tokens = (Select-String -Path $file -Pattern '^Output tokens: (.*)$' | Select-Object -First 1).Matches[0].Groups[1].Value
            $thought = Select-String -Path $file -Pattern '^The model''s thinking before the answer, (\d+) characters' | Select-Object -First 1
            $thinking = if ($thought) { $thought.Matches[0].Groups[1].Value } else { 'none' }
            $short = if ($outcome -like 'passes*') { 'passes' } else { 'fails' }
            Write-Host "  $outcome"
            Add-Content -Path $summary -Encoding UTF8 -Value "$($model.Name) | $step | $($sampling.Name) | $short | $seconds | $tokens | $thinking"
        }
    }
}
Write-Host "Done. The results are in $out, with one line each in $summary"
