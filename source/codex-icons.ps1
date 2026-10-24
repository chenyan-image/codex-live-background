function Get-DreamBackgroundCodexIcon {
 param($Codex,[string]$StateRoot)
 if(-not $Codex.PackageRoot){return $null}
 $source=$null
 foreach($name in @('Square44x44Logo.targetsize-256_altform-unplated.png','Square44x44Logo.png','Square150x150Logo.png','icon.png')){
  $candidate=Join-Path $Codex.PackageRoot ('assets\'+$name)
  if(Test-Path -LiteralPath $candidate -PathType Leaf){if((Get-Item -LiteralPath $candidate).Attributes -band [IO.FileAttributes]::ReparsePoint){continue};$source=$candidate;break}
 }
 if(-not $source){return $null}
 $directory=Join-Path $StateRoot 'icons';$null=New-Item -ItemType Directory -Path $directory -Force
 $path=Join-Path $directory 'codex-official.ico';$metadata=Join-Path $directory 'codex-icon-source.json';$sha=(Get-FileHash -LiteralPath $source).Hash
 try{$saved=[IO.File]::ReadAllText($metadata)|ConvertFrom-Json;if($saved.sha256 -ceq $sha -and (Test-Path -LiteralPath $path)){$bytes=[IO.File]::ReadAllBytes($path);if($bytes.Length -gt 22 -and $bytes[2] -eq 1 -and $bytes[4] -eq 7){return $path}}}catch{}
 Add-Type -AssemblyName System.Drawing
 $image=[Drawing.Image]::FromFile($source);$memory=[IO.MemoryStream]::new();$writer=[IO.BinaryWriter]::new($memory)
 try{
  $sizes=@(16,24,32,48,64,128,256);$images=@()
  foreach($size in $sizes){$bitmap=[Drawing.Bitmap]::new($size,$size);$graphics=[Drawing.Graphics]::FromImage($bitmap);$buffer=[IO.MemoryStream]::new();try{$graphics.InterpolationMode=[Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic;$graphics.DrawImage($image,0,0,$size,$size);$bitmap.Save($buffer,[Drawing.Imaging.ImageFormat]::Png);$images+=,@($buffer.ToArray())}finally{$buffer.Dispose();$graphics.Dispose();$bitmap.Dispose()}}
  $writer.Write([int16]0);$writer.Write([int16]1);$writer.Write([int16]$sizes.Count);$offset=6+16*$sizes.Count
  for($i=0;$i -lt $sizes.Count;$i++){$dimension=if($sizes[$i] -eq 256){0}else{$sizes[$i]};$writer.Write([byte]$dimension);$writer.Write([byte]$dimension);$writer.Write([int16]0);$writer.Write([int16]1);$writer.Write([int16]32);$writer.Write([int]$images[$i].Count);$writer.Write([int]$offset);$offset+=$images[$i].Count}
  foreach($bytes in $images){$writer.Write([byte[]]$bytes)};$writer.Flush();$temporary=$path+'.tmp';[IO.File]::WriteAllBytes($temporary,$memory.ToArray());if(Test-Path -LiteralPath $path){[IO.File]::Replace($temporary,$path,$null)}else{[IO.File]::Move($temporary,$path)}
  [IO.File]::WriteAllText($metadata,(@{sha256=$sha;package=$Codex.PackageFullName;source=[IO.Path]::GetFileName($source)}|ConvertTo-Json),[Text.UTF8Encoding]::new($false))
  return $path
 }finally{$writer.Dispose();$memory.Dispose();$image.Dispose()}
}
