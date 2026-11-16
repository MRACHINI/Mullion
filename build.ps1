# Builds the two release versions of Mullion into the "dist" folder:
#
#   dist\Mullion.exe        small; needs the .NET 10 Desktop Runtime installed
#   dist\Mullion-full.exe   large; needs nothing installed
#
#   powershell -ExecutionPolicy Bypass -File build.ps1

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$project = Join-Path $root 'src\Mullion\Mullion.csproj'
$dist = Join-Path $root 'dist'
$work = Join-Path $root 'dist\_work'

# Windows will not replace an executable that is running.
$running = Get-Process Mullion, Mullion-full -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -and $_.Path.StartsWith($dist, [StringComparison]::OrdinalIgnoreCase) }
if ($running) {
    throw 'Mullion is running from the dist folder. Exit it from its tray icon and run this script again.'
}

# Only the executables are replaced. settings.json and mullion.log in dist belong
# to whoever has been running Mullion from there and must survive a rebuild.
New-Item -ItemType Directory -Force $dist | Out-Null
if (Test-Path $work) { Remove-Item $work -Recurse -Force }

dotnet test (Join-Path $root 'tests\Mullion.Core.Tests') --nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'Tests failed; nothing was built.' }

$common = @(
    '-c', 'Release', '-r', 'win-x64', '--nologo', '-v', 'q',
    '-p:PublishSingleFile=true',
    '-p:DebugType=none'
)

# Small version: the .NET runtime is expected on the PC.
dotnet publish $project @common --self-contained false -o (Join-Path $work 'small')
if ($LASTEXITCODE -ne 0) { throw 'Publishing the small version failed.' }
Copy-Item (Join-Path $work 'small\Mullion.exe') (Join-Path $dist 'Mullion.exe') -Force

# Full version: the runtime is packed inside the executable.
dotnet publish $project @common --self-contained true `
    '-p:IncludeNativeLibrariesForSelfExtract=true' `
    '-p:EnableCompressionInSingleFile=true' `
    -o (Join-Path $work 'full')
if ($LASTEXITCODE -ne 0) { throw 'Publishing the full version failed.' }
Copy-Item (Join-Path $work 'full\Mullion.exe') (Join-Path $dist 'Mullion-full.exe') -Force

Remove-Item $work -Recurse -Force

Get-ChildItem $dist -File -Filter *.exe | ForEach-Object {
    '{0,-18} {1,8:N1} MB' -f $_.Name, ($_.Length / 1MB)
}
