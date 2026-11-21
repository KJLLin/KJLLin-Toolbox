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
  $bundledInstaller = Join-Path $PSScriptRoot 'scoop-install.ps1'
  try {
    if (Test-Path -LiteralPath $bundledInstaller) {
      Say 'Running the Scoop installer bundled with KJLLin Toolbox'
      Invoke-Expression ('& {' + (Get-Content -LiteralPath $bundledInstaller -Raw) + '}')
    } else {
      Say 'Bundled Scoop installer missing, downloading get.scoop.sh'
      Invoke-Expression ('& {' + (Invoke-RestMethod -Uri 'https://get.scoop.sh') + '}')
    }
  } catch { Say ('install failed: ' + $_.Exception.Message) }
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
  if ($have -match ('^' + [regex]::Escape($name))) { Say ('bucket present: ' + $name); continue }

  # 同一个 bucket 可以给多个地址（国内镜像在前，GitHub 兜底），逐个试，成功就停。
  $urls = @($parts[1].Split('|') | Where-Object { $_.Trim() } | ForEach-Object { $_.Trim() })
  $added = $false
  foreach ($url in $urls) {
    Say ('adding bucket ' + $name + ' from ' + $url)
    & $shim bucket add $name $url 2>&1 | ForEach-Object { Say ('  ' + $_) }
    $now = ''
    try { $now = (& $shim bucket list 2>&1) -join "`n" } catch {}
    if ($now -match ('^' + [regex]::Escape($name))) { $added = $true; break }
    # 试下一个地址前，先把这次失败留下的半成品清掉，否则 scoop 会拒绝重复添加
    Say ('  removing failed bucket ' + $name)
    & $shim bucket rm $name 2>&1 | ForEach-Object { Say ('  ' + $_) }
  }
  if ($added) { Say ('bucket ready: ' + $name) } else { Say ('bucket unavailable: ' + $name) }
}

# Curated apps live in the upstream extras bucket; add it once so the store can offer them.
if (-not ($have -match '(?m)^\s*extras\b')) {
  Say 'adding extras bucket (curated apps)'
  & $shim bucket add extras 2>&1 | ForEach-Object { Say ('  ' + $_) }
}

Say '=== bootstrap finished ==='
exit 0
