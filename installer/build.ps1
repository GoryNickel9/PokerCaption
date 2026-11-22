param(
    [Parameter(Mandatory)]
    [ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+$')]
    [string]$Version
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$output = Join-Path $repo 'out\installer'
$publish = Join-Path $output ('publish-' + [guid]::NewGuid().ToString('N'))
$project = Join-Path $repo 'src\PokerCaption\PokerCaption.csproj'
$icon = Join-Path $repo 'src\PokerCaption\obj\PokerCaption.ico'
$localSdk = Join-Path $env:USERPROFILE '.dotnet\dotnet.exe'
$dotnet = if (Test-Path -LiteralPath $localSdk) { $localSdk } else { (Get-Command dotnet.exe).Source }
$compiler = (Get-Command ISCC.exe).Source

& (Join-Path $repo 'native\build.ps1')
if ($LASTEXITCODE -ne 0) { throw 'Native build failed.' }

& $dotnet publish $project -c Release -r win-x86 --self-contained true -p:DebugType=None "-p:Version=$Version" -o $publish
if ($LASTEXITCODE -ne 0) { throw 'Application publish failed.' }

foreach ($required in 'PokerCaption.exe', 'coreclr.dll', 'native\x86\captionHook.dll',
    'native\x64\captionHook.dll', 'native\MinHook-LICENSE.txt', 'it\PokerCaption.resources.dll',
    'tools\dump_uia.ps1') {
    if (!(Test-Path -LiteralPath (Join-Path $publish $required) -PathType Leaf)) {
        throw "The installer payload is missing $required."
    }
}

$dependencies = Get-Content -LiteralPath (Join-Path $publish 'PokerCaption.deps.json') -Raw | ConvertFrom-Json
$assets = Get-Content -LiteralPath (Join-Path $repo 'src\PokerCaption\obj\project.assets.json') -Raw | ConvertFrom-Json
foreach ($runtime in $dependencies.libraries.PSObject.Properties.Name | Where-Object { $_ -like 'runtimepack.*' }) {
    $package = $runtime.Substring('runtimepack.'.Length).ToLowerInvariant()
    $packageDir = $assets.packageFolders.PSObject.Properties.Name | ForEach-Object { Join-Path $_ $package } |
        Where-Object { Test-Path -LiteralPath $_ -PathType Container } | Select-Object -First 1
    if (!$packageDir) { throw "Missing runtime package $package." }
    $notices = Get-ChildItem -LiteralPath $packageDir -File |
        Where-Object { $_.Name -in 'LICENSE', 'LICENSE.TXT', 'THIRD-PARTY-NOTICES.TXT' }
    if (!$notices) { throw "Missing license for $package." }
    $licenses = Join-Path $publish ('licenses\' + ($package -split '/')[0])
    New-Item -ItemType Directory -Path $licenses -Force | Out-Null
    foreach ($notice in $notices) { Copy-Item -LiteralPath $notice.FullName -Destination $licenses }
}

$name = "PokerCaption-Setup-$Version-win-x86"
& $compiler '/Qp' "/O$output" "/F$name" "/DAppVersion=$Version" "/DPublishDir=$publish" "/DProgramIcon=$icon" (Join-Path $PSScriptRoot 'PokerCaption.iss')
if ($LASTEXITCODE -ne 0) { throw 'Installer build failed.' }

$setup = Join-Path $output "$name.exe"
$hash = (Get-FileHash -LiteralPath $setup -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $name.exe" | Set-Content -LiteralPath "$setup.sha256" -Encoding Ascii
Write-Output $setup
