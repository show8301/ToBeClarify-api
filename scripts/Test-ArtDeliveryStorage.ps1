#Requires -Version 7.2
param(
    [Parameter(Mandatory = $true)][string]$ApiDeployPath,
    [Parameter(Mandatory = $true)][string]$WebDeployPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-CanonicalPath([string]$Path) {
    if (-not [IO.Path]::IsPathFullyQualified($Path)) { throw 'An absolute path is required.' }
    return [IO.Path]::GetFullPath($Path).TrimEnd('\', '/')
}

function Test-Within([string]$Path, [string]$Root) {
    return $Path.Equals($Root, [StringComparison]::OrdinalIgnoreCase) -or
        $Path.StartsWith($Root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)
}

$api = Get-CanonicalPath ((Resolve-Path -LiteralPath $ApiDeployPath).Path)
$web = Get-CanonicalPath ((Resolve-Path -LiteralPath $WebDeployPath).Path)
$parent = Split-Path -Parent $api
if ($parent -ine (Split-Path -Parent $web)) { throw 'API and Web do not share a deployment parent.' }
$root = Get-CanonicalPath (Join-Path $parent 'media')
foreach ($deploy in @($api, $web)) {
    if ((Test-Within $root $deploy) -or (Test-Within $deploy $root)) { throw 'Media overlaps a deployment directory.' }
}

# Read IIS configuration only. Do not create directories, set ACLs, or edit appsettings.
Add-Type -Path "$env:windir\System32\inetsrv\Microsoft.Web.Administration.dll"
$manager = New-Object Microsoft.Web.Administration.ServerManager
try {
    $apiPools = @()
    foreach ($site in $manager.Sites) {
        foreach ($application in $site.Applications) {
            foreach ($directory in $application.VirtualDirectories) {
                $published = Get-CanonicalPath ([Environment]::ExpandEnvironmentVariables($directory.PhysicalPath))
                if ((Test-Within $root $published) -or (Test-Within $published $root)) {
                    throw "Private media overlaps IIS mapping: $($site.Name)$($application.Path)$($directory.Path)"
                }
                if ($published -ieq $api) { $apiPools += $application.ApplicationPoolName }
            }
        }
    }
    $apiPools = @($apiPools | Sort-Object -Unique)
    if ($apiPools.Count -ne 1) { throw 'Expected exactly one API application pool; inspect IIS manually.' }
    $pool = $manager.ApplicationPools[$apiPools[0]]
    $identityType = $pool.ProcessModel.IdentityType.ToString()
    $identity = switch ($identityType) {
        'ApplicationPoolIdentity' { 'IIS AppPool\' + $pool.Name }
        'SpecificUser' { $pool.ProcessModel.UserName }
        'NetworkService' { 'NT AUTHORITY\NETWORK SERVICE' }
        'LocalService' { 'NT AUTHORITY\LOCAL SERVICE' }
        'LocalSystem' { 'NT AUTHORITY\SYSTEM' }
        default { throw 'Unknown API pool identity type.' }
    }
    for ($item = [IO.DirectoryInfo]$root; $null -ne $item; $item = $item.Parent) {
        if ($item.LinkTarget -or ($item.Exists -and ($item.Attributes -band [IO.FileAttributes]::ReparsePoint))) {
            throw 'Media root ancestors must not contain links/reparse points.'
        }
    }
    [pscustomobject]@{
        ApiDeployPath = $api
        WebDeployPath = $web
        PrivateRootPath = $root
        ArtDeliveryPath = Join-Path $root 'art-deliveries'
        ApiPool = $pool.Name
        ApiIdentity = $identity
        ExistingRootAcl = if (Test-Path -LiteralPath $root) { (Get-Acl -LiteralPath $root).AccessToString } else { '(directory not provisioned)' }
    } | Format-List
}
finally { $manager.Dispose() }
