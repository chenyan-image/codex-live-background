$ErrorActionPreference='Stop'
$source=Join-Path $PSScriptRoot '..\source\control.ps1'
$tokens=$null;$errors=$null;$ast=[Management.Automation.Language.Parser]::ParseFile($source,[ref]$tokens,[ref]$errors)
if($errors.Count){throw $errors[0]}
foreach($name in @('Invoke-YzConnectionAttempt','Connect-YzCodex')){
 $fn=$ast.Find({param($node)$node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name},$true)
 . ([scriptblock]::Create($fn.Extent.Text))
}
$script:connectReal=${function:Connect-YzCodex}
function Reset-Fixture {
 $script:report=@{installed=$true;connected=$false;serviceRunning=$true;autostart=$true}
 $script:running=@([pscustomobject]@{Id=1});$script:ensure=0;$script:connect=0;$script:handshake=0;$script:failure='';$script:verify=$true
}
function Get-YzConnectionReport {return $script:report.Clone()}
function Get-DreamSkinRegisteredCodexInstalls {return [pscustomobject]@{Version='test'}}
function Get-DreamSkinCodexProcesses {param($Codex)return $script:running}
function Ensure-YzService {$script:ensure++}
function Connect-YzCodex {$script:connect++;if($script:failure){throw $script:failure};return [pscustomobject]@{BrowserId='verified'}}
function Invoke-YzConnectionHandshake {$script:handshake++;if($script:verify){$script:report.connected=$true}}
Reset-Fixture;$r=Invoke-YzConnectionAttempt
if(-not $r.connected -or $script:ensure -ne 1 -or $script:connect -ne 1 -or $script:handshake -ne 1){throw 'Manual click did not establish a connection'}
Reset-Fixture;$r=Invoke-YzConnectionAttempt -Automatic
if(-not $r.connected -or $script:connect -ne 1){throw 'Automatic reconnect skipped a running app'}
Reset-Fixture;$script:report.autostart=$false;$r=Invoke-YzConnectionAttempt -Automatic
if($script:ensure -or $script:connect -or $r.message -notmatch '点击'){throw 'Automatic check installed an absent/removed service'}
Reset-Fixture;$script:running=@();$r=Invoke-YzConnectionAttempt -Automatic
if($script:connect -or $r.message -notmatch '等待打开'){throw 'Automatic check launched Codex'}
Reset-Fixture;$script:running=@();$r=Invoke-YzConnectionAttempt
if($script:connect -ne 1 -or -not $r.connected){throw 'Explicit click did not request a connection with Codex closed'}
Reset-Fixture;$script:report.connected=$true;$r=Invoke-YzConnectionAttempt -Automatic
if($script:connect -or $script:handshake -or -not $r.connected){throw 'Healthy connection was rewritten'}
Reset-Fixture;$script:failure='请完全退出 Codex 后重试';$r=Invoke-YzConnectionAttempt
if(-not $r.connectionError -or $r.connected -or $r.message -cne $script:failure){throw 'Actionable failure was lost'}
Reset-Fixture;$script:verify=$false;$r=Invoke-YzConnectionAttempt
if(-not $r.connectionError -or $r.connected){throw 'Unverified connection displayed as ready'}
Reset-Fixture;$script:report.installed=$false;$r=Invoke-YzConnectionAttempt
if($script:ensure -or -not $r.connectionError){throw 'Missing Codex was not reported'}
# Exercise the actual ordinary-instance branch. Any attempted launch/close fails this test.
function Get-DreamSkinCodexInstall {return [pscustomobject]@{Version='test'}}
function Get-DreamSkinVerifiedCdpIdentity {param($Port,$Codex)return $null}
function Get-DreamSkinVerifiedCdpIdentityForAnyRegistered {param($Port)return $null}
function Start-DreamSkinCodexDirect {throw 'Unexpected launch'}
function Stop-Process {throw 'Unexpected process termination'}
Reset-Fixture
try{& $script:connectReal;throw 'Ordinary app accepted'}catch{if($_.Exception.Message -notmatch '手动完全退出'){throw}}
@{pass=$true;cases=10;manualConnect=$true;automaticRetry=$true;ordinaryInstancePreserved=$true;realServicesModified=$false}|ConvertTo-Json -Compress
