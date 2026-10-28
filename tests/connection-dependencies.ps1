param(
 [Parameter(Mandatory=$true)][string]$PackageRoot,
 [Parameter(Mandatory=$true)][string]$OutputDirectory,
 [string]$ControlScript
)
$ErrorActionPreference='Stop'
if([string]::IsNullOrWhiteSpace($ControlScript)){$ControlScript=Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) '..\source\control.ps1'}
$tokens=$null;$errors=$null
$ast=[Management.Automation.Language.Parser]::ParseFile([IO.Path]::GetFullPath($ControlScript),[ref]$tokens,[ref]$errors)
if($errors.Count){throw $errors[0]}
$firstFunction=$ast.Find({param($node)$node -is [Management.Automation.Language.FunctionDefinitionAst]},$true)
$limit=$firstFunction.Extent.StartOffset
$imports=@($ast.FindAll({param($node)$node -is [Management.Automation.Language.CommandAst] -and $node.InvocationOperator -eq [Management.Automation.Language.TokenKind]::Dot -and $node.Extent.StartOffset -lt $limit -and $node.Extent.Text -match '\$engine'},$true))
$root=[IO.Path]::GetFullPath($PackageRoot);$engine=Join-Path $root 'engine'
# Load the production entry point's actual imports in a clean PowerShell host.
# Do not stub the missing function or execute any install/connection actions.
foreach($import in $imports){. ([scriptblock]::Create($import.Extent.Text))}
$out=[IO.Path]::GetFullPath($OutputDirectory);$null=New-Item -ItemType Directory -Path $out -Force
$state=Join-Path $out 'isolated-state'
if(Test-DreamSkinPaused -StateRoot $state){throw 'Absent pause marker reported as paused'}
$null=Set-DreamSkinPaused -Paused $true -StateRoot $state
if(-not (Test-DreamSkinPaused -StateRoot $state)){throw 'Real pause marker was not detected'}
$null=Set-DreamSkinPaused -Paused $false -StateRoot $state
if(Test-DreamSkinPaused -StateRoot $state){throw 'Resume did not clear the real pause marker'}
$result=@{pass=$true;cases=3;realEntryPointImports=$true;pauseFunctionMocked=$false;realServicesModified=$false;liveSettingsChanged=$false}
$result|ConvertTo-Json -Compress|Set-Content -LiteralPath (Join-Path $out 'result.json') -Encoding UTF8
$result|ConvertTo-Json -Compress
