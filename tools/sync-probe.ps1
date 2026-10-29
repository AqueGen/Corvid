$root = Split-Path -Parent $PSScriptRoot
$target = Join-Path $root 'lua/CorvidProbe/Libs/LibCorvid-1.0'
if (Test-Path $target) { Remove-Item -Recurse -Force $target }
New-Item -ItemType Directory -Force $target | Out-Null
Copy-Item (Join-Path $root 'lua/LibCorvid-1.0/*') $target -Recurse
