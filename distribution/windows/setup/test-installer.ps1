[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$InstallerPath,

    [Parameter(Mandatory)]
    [switch]$HostedRunner
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if (!$HostedRunner -or !$IsWindows -or $env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted') {
    throw 'This test is for a fresh GitHub-hosted Windows runner.'
}

$installer = (Resolve-Path -LiteralPath $InstallerPath).Path
$installRoot = Join-Path $env:ProgramW6432 'Bibliophilarr'
$bin = Join-Path $installRoot 'bin'
$app = Join-Path $bin 'Bibliophilarr.Console.exe'
$sharedData = Join-Path $env:ProgramData 'Bibliophilarr'
$scratch = (New-Item -ItemType Directory -Path (Join-Path $env:RUNNER_TEMP ("installer-smoke-" + [guid]::NewGuid()))).FullName
$testData = Join-Path $scratch 'data'
$runningApp = $null

function Assert-NoAutomaticStart {
    if ((Get-Service -Name Bibliophilarr -ErrorAction SilentlyContinue) -or
        (Get-Process -Name Bibliophilarr,Bibliophilarr.Console -ErrorAction SilentlyContinue)) {
        throw 'Found a Bibliophilarr service or process.'
    }
}

function Assert-ProtectedPath([string]$Path) {
    $broadSids = 'S-1-1-0', 'S-1-5-11', 'S-1-5-32-545', 'S-1-5-32-546'
    $right = [System.Security.AccessControl.FileSystemRights]
    $writeRights = $right::WriteData -bor $right::AppendData -bor $right::WriteExtendedAttributes -bor
        $right::WriteAttributes -bor $right::Delete -bor $right::DeleteSubdirectoriesAndFiles -bor
        $right::ChangePermissions -bor $right::TakeOwnership
    $rules = (Get-Acl -LiteralPath $Path).GetAccessRules($true, $true, [System.Security.Principal.SecurityIdentifier])
    foreach ($rule in $rules) {
        if ($rule.AccessControlType -eq 'Allow' -and $rule.IdentityReference.Value -in $broadSids -and
            ($rule.FileSystemRights -band $writeRights)) {
            throw "A broad Windows principal has write access to $Path."
        }
    }
}

function Invoke-Setup([string]$Executable, [string[]]$ExtraArguments) {
    $arguments = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART') + $ExtraArguments
    $process = Start-Process -FilePath $Executable -ArgumentList $arguments -Wait -PassThru
    try {
        return $process.ExitCode
    } finally {
        $process.Dispose()
    }
}

function Get-Uninstaller {
    Get-ChildItem -LiteralPath $installRoot -File -Filter 'unins*.exe' | Select-Object -First 1
}

Assert-NoAutomaticStart
if ((Test-Path -LiteralPath $installRoot) -or (Test-Path -LiteralPath $sharedData)) {
    throw 'An installation or shared data already exists on the runner.'
}

try {
    $redirected = Join-Path $scratch 'unprotected'
    if ((Invoke-Setup $installer @('/SP-', "/DIR=`"$redirected`"")) -ne 7 -or
        (Test-Path -LiteralPath $redirected) -or (Test-Path -LiteralPath $installRoot)) {
        throw 'Setup did not reject a directory outside Program Files.'
    }

    if ((Invoke-Setup $installer @('/SP-', "/LOG=`"$scratch\install.log`"")) -ne 0) {
        throw 'Fresh Windows installation failed.'
    }
    Assert-NoAutomaticStart
    if (!(Test-Path -LiteralPath $app -PathType Leaf) -or (Test-Path -LiteralPath $sharedData)) {
        throw 'Setup did not install the executable in Program Files without touching shared data.'
    }
    foreach ($path in @($installRoot, $bin, $app)) {
        Assert-ProtectedPath $path
    }

    New-Item -ItemType Directory -Path $testData | Out-Null
    $probe = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 0)
    try {
        $probe.Start()
        $port = $probe.LocalEndpoint.Port
    } finally {
        $probe.Stop()
    }
    $apiKey = [guid]::NewGuid().ToString('N')
    $config = Join-Path $testData 'config.xml'
    @"
<Config>
  <BindAddress>127.0.0.1</BindAddress>
  <Port>$port</Port>
  <LaunchBrowser>False</LaunchBrowser>
  <AnalyticsEnabled>False</AnalyticsEnabled>
  <AuthenticationMethod>Forms</AuthenticationMethod>
  <AuthenticationRequired>Enabled</AuthenticationRequired>
  <ApiKey>$apiKey</ApiKey>
</Config>
"@ | Set-Content -LiteralPath $config -Encoding utf8

    $runningApp = Start-Process -FilePath $app -WorkingDirectory $bin `
        -ArgumentList @('/nobrowser', "/data=`"$testData`"") -PassThru `
        -RedirectStandardOutput (Join-Path $scratch 'app.stdout') `
        -RedirectStandardError (Join-Path $scratch 'app.stderr')
    $baseUrl = "http://127.0.0.1:$port"
    $deadline = [DateTime]::UtcNow.AddSeconds(60)
    $ready = $false
    do {
        if ($runningApp.HasExited) {
            throw "Installed application exited with code $($runningApp.ExitCode)."
        }
        try {
            $ping = Invoke-WebRequest -Uri "$baseUrl/ping" -TimeoutSec 2 -SkipHttpErrorCheck -NoProxy
            $ready = $ping.StatusCode -eq 200
        } catch {
            Start-Sleep -Milliseconds 250
        }
    } until ($ready -or [DateTime]::UtcNow -ge $deadline)
    if (!$ready) {
        throw 'Installed application did not become ready within 60 seconds.'
    }

    $listeners = @(Get-NetTCPConnection -State Listen -OwningProcess $runningApp.Id)
    if ($listeners.Count -eq 0 -or @($listeners | Where-Object { $_.LocalAddress -ne '127.0.0.1' -or $_.LocalPort -ne $port }).Count -gt 0) {
        throw 'The installed application is not listening exclusively on its assigned loopback port.'
    }
    $statusUri = "$baseUrl/api/v1/system/status"
    $anonymous = Invoke-WebRequest -Uri $statusUri -TimeoutSec 5 -MaximumRedirection 0 -SkipHttpErrorCheck -NoProxy
    $status = Invoke-RestMethod -Uri $statusUri -Headers @{'X-Api-Key' = $apiKey} -TimeoutSec 5 -NoProxy
    if ($anonymous.StatusCode -ne 401 -or !$status.isWindows -or $status.appData -ne $testData -or $status.startupPath -ne $bin) {
        throw 'Authentication or the isolated paths reported by the installed app did not match expectations.'
    }
    Stop-Process -Id $runningApp.Id
    if (!$runningApp.WaitForExit(10000)) {
        throw 'The disposable application did not stop.'
    }
    $configHash = (Get-FileHash -LiteralPath $config).Hash

    if ((Invoke-Setup $installer @('/SP-', "/LOG=`"$scratch\reinstall.log`"")) -ne 0) {
        throw 'Windows reinstall failed.'
    }
    Assert-NoAutomaticStart
    Assert-ProtectedPath $app

    $uninstall = Get-Uninstaller
    if (!$uninstall -or (Invoke-Setup $uninstall.FullName @()) -ne 0) {
        throw 'Windows uninstall failed.'
    }
    Assert-NoAutomaticStart
    if ((Test-Path -LiteralPath $app) -or (Test-Path -LiteralPath $sharedData) -or
        (Get-FileHash -LiteralPath $config).Hash -ne $configHash) {
        throw 'Uninstall left executable files or modified application data.'
    }

    Write-Host 'Windows installer smoke passed: safe path, ACLs, no automatic start, loopback API, reinstall and uninstall.'
} finally {
    if ($runningApp -and !$runningApp.HasExited) {
        Stop-Process -Id $runningApp.Id -ErrorAction SilentlyContinue
        $null = $runningApp.WaitForExit(10000)
    }
    if (Test-Path -LiteralPath $installRoot) {
        $uninstall = Get-Uninstaller
        if ($uninstall -and (Invoke-Setup $uninstall.FullName @()) -ne 0) {
            Write-Warning 'Cleanup uninstaller failed on the disposable runner.'
        }
    }
}
