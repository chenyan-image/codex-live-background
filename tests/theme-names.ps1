param([Parameter(Mandatory=$true)][string]$FixtureRoot)
$ErrorActionPreference='Stop';[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false);$env:PSModulePath=Join-Path $PSHOME Modules
$repo=Split-Path -Parent $PSScriptRoot;$engine=Join-Path $repo engine
. (Join-Path $engine 'scripts\common-windows.ps1')
. (Join-Path $engine 'scripts\theme-windows.ps1')
$name=([char]::ConvertFromUtf32(0x1F319))*4096
if((Assert-DreamSkinThemeName $name) -cne $name){throw 'Emoji count or content mismatch'}
foreach($bad in @($name+'x',"line`nbreak","line`tbreak",('line'+[char]127+'break'),([string][char]0xD800),123)){
 $rejected=$false;try{$null=Assert-DreamSkinThemeName -Value $bad}catch{$rejected=$true};if(-not $rejected){throw 'Invalid name accepted'}
}
$state=Join-Path $FixtureRoot active-state;$image=Join-Path $FixtureRoot background.png
$null=Set-DreamSkinActiveTheme -ImagePath $image -Name $name -StateRoot $state -SkipImageArchive
$active=Read-DreamSkinTheme -ThemeDirectory (Join-Path $state active-theme)
if($active.Theme.name -cne $name){throw 'Active theme lost long name'}
$saved=Save-DreamSkinCurrentTheme -Name $name -StateRoot $state
if($saved.Theme.name -cne $name){throw 'Saved theme lost long name'}
$before=[IO.File]::ReadAllText($active.ThemePath);$rejected=$false
try{$null=Set-DreamSkinActiveTheme -ImagePath $image -Name ($name+'x') -StateRoot $state}catch{$rejected=$true}
if(-not $rejected -or [IO.File]::ReadAllText($active.ThemePath) -cne $before){throw 'Invalid apply changed current theme'}
$manager=Join-Path $engine 'scripts\manager-actions.ps1';$importState=Join-Path $FixtureRoot imported-state
$lines=@(& $manager -Action ImportTheme -SkillRoot $engine -StateRoot $importState -ImagePath $image -Name $name -KeepCurrent)
$imported=($lines -join "`n")|ConvertFrom-Json
$importedTheme=Read-DreamSkinTheme -ThemeDirectory $imported.themeDirectory
if($importedTheme.Theme.name -cne $name){throw 'Manager import lost name'}
$requests=Join-Path $importState requests;$null=New-Item -ItemType Directory -Path $requests -Force
$request=Join-Path $requests batch.json
[IO.File]::WriteAllText($request,(@{schemaVersion=1;items=@(@{name=$name;imagePath=$image;appearance='dark';tags=@()},@{name=($name+'x');imagePath=$image;appearance='dark';tags=@()})}|ConvertTo-Json -Depth 5),[Text.UTF8Encoding]::new($false))
$batch=(@(& $manager -Action ImportBatch -SkillRoot $engine -StateRoot $importState -RequestPath $request) -join "`n")|ConvertFrom-Json
if($batch.imported -ne 1 -or $batch.failed -ne 1){throw ('Batch name boundary mismatch: '+(@{imported=$batch.imported;skipped=$batch.skipped;failed=$batch.failed;messages=@($batch.results|ForEach-Object{$_.message})}|ConvertTo-Json -Compress))}
$harness=Join-Path $FixtureRoot ThemeNameLibrary.exe
& (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe') /nologo /target:exe /codepage:65001 ("/out:$harness") /r:System.Web.Extensions.dll (Join-Path $repo 'source\WallpaperLibrary.cs') (Join-Path $PSScriptRoot ThemeNameLibrary.cs)
if($LASTEXITCODE -ne 0){throw 'Library harness compilation failed'}
& $harness $FixtureRoot
if($LASTEXITCODE -ne 0){throw 'Library long-name checks failed'}
@{pass=$true;maxNameCodePoints=$script:DreamSkinMaxThemeNameCodePoints;emojiCountCorrect=$true;activeAndSaved=$true;invalidApplyPreservedCurrent=$true;managerAndBatchImport=$true;steamAndLibraryPersistence=$true;importedDirectory=$imported.themeDirectory;savedDirectory=$saved.Directory;realCodexTouched=$false}|ConvertTo-Json -Compress
