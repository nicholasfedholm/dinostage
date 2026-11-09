$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$proj = Join-Path $root 'src\DinoStage\DinoStage.csproj'
dotnet build $proj -c Release
if ($LASTEXITCODE -ne 0) { throw "build failed" }
$bin = Join-Path $root 'src\DinoStage\bin\Release'
$dll = Join-Path $bin 'DinoStage.dll'
if (-not (Test-Path $dll)) { throw "dll missing: $dll" }
$out = Join-Path $root 'out'
New-Item -ItemType Directory -Force -Path $out | Out-Null
$zip = Join-Path $out 'dinostage_1.1.0.zip'
if (Test-Path $zip) { Remove-Item $zip -Force }
$stage = Join-Path $out '_stage'
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force -Path $stage | Out-Null
Copy-Item (Join-Path $root 'src\DinoStage\modinfo.json') $stage
Copy-Item $dll $stage
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip
Remove-Item $stage -Recurse -Force
Add-Type -AssemblyName System.IO.Compression.FileSystem
$z = [System.IO.Compression.ZipFile]::OpenRead($zip)
$z.Entries | ForEach-Object { "{0} {1}" -f $_.FullName, $_.Length }
$z.Dispose()
$f = Get-Item $zip
"zip: $($f.FullName) size $($f.Length) sha256 $((Get-FileHash $zip -Algorithm SHA256).Hash)"
