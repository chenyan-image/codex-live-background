param([Parameter(Mandatory=$true)][string]$RequestPath)
$ErrorActionPreference='Stop'
[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false)
try{
 $info=Get-Item -LiteralPath $RequestPath -ErrorAction Stop
 if($info.Length -gt 1MB -or $info.Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Invalid script request file.'}
 $request=[IO.File]::ReadAllText($info.FullName)|ConvertFrom-Json
 $scriptPath=[IO.Path]::GetFullPath($request.ScriptPath)
 if([IO.Path]::GetDirectoryName($scriptPath) -ine $PSScriptRoot -or [IO.Path]::GetExtension($scriptPath) -ine '.ps1'){throw 'Script request must target this engine directory.'}
 $parameters=@{};foreach($property in $request.Parameters.PSObject.Properties){if($property.Name -cnotmatch '^[A-Za-z][A-Za-z0-9]*$'){throw 'Invalid request parameter.'};$parameters[$property.Name]=$property.Value}
 & $scriptPath @parameters
 if(-not $?){exit 1}
}catch{[Console]::Error.WriteLine($_.Exception.Message);exit 1}
