param(
    [Parameter(Mandatory = $true)]
    [string]$ArtifactPath,

    [Parameter(Mandatory = $true)]
    [string]$DeployPath,

    [Parameter(Mandatory = $true)]
    [string]$HealthCheckUrl
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Test-PreservedItem {
    param([System.IO.FileSystemInfo]$Item)

    if ($Item.Name -ilike 'appsettings*.json') {
        return $true
    }

    return $Item.Name -iin @(
        'web.config',
        'app_offline.htm',
        'media',
        'Logs',
        'registerKey.txt',
        'registerKey.txt.lock',
        'oneTimeToken.txt',
        'oneTimeToken.txt.lock'
    )
}

function Test-ArtifactExcludedItem {
    param([System.IO.FileSystemInfo]$Item)

    return $Item.Name -ieq 'web.config' -or $Item.Name -ilike 'appsettings*.json'
}

$artifactRoot = (Resolve-Path -LiteralPath $ArtifactPath).Path
$deployRoot = (Resolve-Path -LiteralPath $DeployPath).Path
$deployRoot = $deployRoot.TrimEnd(
    [System.IO.Path]::DirectorySeparatorChar,
    [System.IO.Path]::AltDirectorySeparatorChar)

if ((Split-Path -Leaf $deployRoot) -ine 'ToBeClarify_API') {
    throw "Refusing to deploy to unexpected directory: $deployRoot"
}

if (-not (Test-Path -LiteralPath (Join-Path $artifactRoot 'ToBeClarify.Api.dll') -PathType Leaf)) {
    throw "The API artifact does not contain ToBeClarify.Api.dll: $artifactRoot"
}

$webConfigPath = Join-Path $deployRoot 'web.config'
if (-not (Test-Path -LiteralPath $webConfigPath -PathType Leaf)) {
    throw "The IIS web.config file was not found: $webConfigPath"
}

if (-not (Get-ChildItem -LiteralPath $deployRoot -Filter 'appsettings*.json' -File)) {
    throw "No server appsettings file was found in: $deployRoot"
}

if ([string]::IsNullOrWhiteSpace($HealthCheckUrl)) {
    throw 'API_HEALTHCHECK_URL is not configured.'
}

$offlinePath = Join-Path $deployRoot 'app_offline.htm'
if (Test-Path -LiteralPath $offlinePath) {
    throw "An app_offline.htm file already exists. Remove it manually before deploying: $offlinePath"
}

$backupRoot = Join-Path $env:RUNNER_TEMP 'tobeclarify-api-rollback'
if (Test-Path -LiteralPath $backupRoot) {
    Remove-Item -LiteralPath $backupRoot -Recurse -Force
}
New-Item -Path $backupRoot -ItemType Directory | Out-Null

$existingApplicationItems = Get-ChildItem -LiteralPath $deployRoot -Force |
    Where-Object { -not (Test-PreservedItem $_) }

foreach ($item in $existingApplicationItems) {
    Copy-Item -LiteralPath $item.FullName -Destination $backupRoot -Recurse -Force
}

$offlineCreated = $false

try {
    Set-Content -LiteralPath $offlinePath -Value 'API deployment in progress.' -Encoding UTF8
    $offlineCreated = $true
    Start-Sleep -Seconds 5

    foreach ($item in $existingApplicationItems) {
        Remove-Item -LiteralPath $item.FullName -Recurse -Force
    }

    foreach ($item in (Get-ChildItem -LiteralPath $artifactRoot -Force)) {
        if (-not (Test-ArtifactExcludedItem $item)) {
            Copy-Item -LiteralPath $item.FullName -Destination $deployRoot -Recurse -Force
        }
    }

    if (-not (Test-Path -LiteralPath (Join-Path $deployRoot 'ToBeClarify.Api.dll') -PathType Leaf)) {
        throw 'Deployment verification failed because ToBeClarify.Api.dll is missing.'
    }

    Remove-Item -LiteralPath $offlinePath -Force
    $offlineCreated = $false

    $healthCheckSucceeded = $false
    for ($attempt = 1; $attempt -le 10; $attempt++) {
        try {
            $separator = if ($HealthCheckUrl.Contains('?')) { '&' } else { '?' }
            $cacheBuster = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
            $checkUrl = "$HealthCheckUrl${separator}deploymentCheck=$cacheBuster-$attempt"
            $response = Invoke-WebRequest -Uri $checkUrl -UseBasicParsing -TimeoutSec 15

            if ($response.StatusCode -ge 200 -and $response.StatusCode -lt 400) {
                $healthCheckSucceeded = $true
                break
            }
        }
        catch {
            if ($attempt -eq 10) {
                throw
            }
        }

        Start-Sleep -Seconds 3
    }

    if (-not $healthCheckSucceeded) {
        throw "API health check failed: $HealthCheckUrl"
    }

    Write-Host "API deployment and health check completed: $deployRoot"
}
catch {
    Write-Warning 'API deployment failed. Restoring the previous files.'

    if (-not (Test-Path -LiteralPath $offlinePath)) {
        Set-Content -LiteralPath $offlinePath -Value 'API rollback in progress.' -Encoding UTF8
        $offlineCreated = $true
        Start-Sleep -Seconds 5
    }

    Get-ChildItem -LiteralPath $deployRoot -Force |
        Where-Object { -not (Test-PreservedItem $_) } |
        ForEach-Object { Remove-Item -LiteralPath $_.FullName -Recurse -Force }

    foreach ($item in (Get-ChildItem -LiteralPath $backupRoot -Force)) {
        Copy-Item -LiteralPath $item.FullName -Destination $deployRoot -Recurse -Force
    }

    throw
}
finally {
    if ($offlineCreated -and (Test-Path -LiteralPath $offlinePath)) {
        Remove-Item -LiteralPath $offlinePath -Force
    }
}
