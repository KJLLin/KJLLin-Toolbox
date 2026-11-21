param([string]$BucketUrls = '')

# Run by KJLLin Toolbox setup as the end user, hidden, before the client first starts.
# Ensures Scoop exists and registers the KJLLin Toolbox buckets, so the client's only
# source has something to list on the very first launch. Never fails the installation.

$ErrorActionPreference = 'Continue'
$log = Join-Path $env:TEMP 'kjllin-toolbox-scoop.log'

function Say([string]$m) {
  try { Add-Content -LiteralPath $log -Value ((Get-Date -Format 's') + '  ' + $m) -Encoding UTF8 } catch {}
}

Say '=== KJLLin Toolbox scoop bootstrap ==='
try { [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12 } catch {}

$shim = Join-Path $env:USERPROFILE 'scoop\shims\scoop.cmd'
if (-not (Test-Path -LiteralPath $shim)) {
  Say 'Installing Scoop for the current user'
  try { Set-ExecutionPolicy -Scope CurrentUser -ExecutionPolicy RemoteSigned -Force -ErrorAction Stop } catch { Say ('policy: ' + $_.Exception.Message) }
  try { Invoke-Expression ('& {' + (Invoke-RestMethod -Uri 'https://get.scoop.sh') + '}') } catch { Say ('install failed: ' + $_.Exception.Message) }
}
if (-not (Test-Path -LiteralPath $shim)) { Say 'Scoop unavailable; leaving the install untouched'; exit 0 }

$env:PATH = (Split-Path -Parent $shim) + ';' + $env:PATH
Say ('Scoop: ' + $shim)

$git = Join-Path $env:USERPROFILE 'scoop\shims\git.exe'
if (-not (Test-Path -LiteralPath $git)) {
  Say 'Installing git (Scoop needs it to add buckets)'
  & $shim install git 2>&1 | ForEach-Object { Say ('  ' + $_) }
}

$have = ''
try { $have = (& $shim bucket list 2>&1) -join "`n" } catch {}
foreach ($pair in ($BucketUrls -split ';')) {
  if (-not $pair) { continue }
  $parts = $pair -split '=', 2
  if ($parts.Count -lt 2) { continue }
  $name = $parts[0].Trim()
  $url = $parts[1].Trim()
  if ($have -match ('^' + [regex]::Escape($name))) { Say ('bucket present: ' + $name); continue }
  Say ('adding bucket ' + $name)
  & $shim bucket add $name $url 2>&1 | ForEach-Object { Say ('  ' + $_) }
}

# Curated apps live in the upstream extras bucket; add it once so the store can offer them.
if (-not ($have -match '(?m)^\s*extras\b')) {
  Say 'adding extras bucket (curated apps)'
  & $shim bucket add extras 2>&1 | ForEach-Object { Say ('  ' + $_) }
}

Say '=== bootstrap finished ==='
exit 0
