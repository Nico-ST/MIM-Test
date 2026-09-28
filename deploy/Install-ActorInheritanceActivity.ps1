<#
.SYNOPSIS
    Installs the actor inheritance activity assembly into the GAC of a MIM Service or MIM Portal server.

.DESCRIPTION
    Run elevated on every MIM Service server and every MIM Portal (SharePoint) server. Afterwards the
    MIM Service (FIMService) and IIS are restarted so that the new assembly is loaded.

    Re-run it after every rebuild: the assembly version stays 1.0.0.0, so the GAC copy is replaced in place.

.PARAMETER ConfigPath
    Path of the configuration file. Default: ActorInheritance.config.psd1 next to this script.

.PARAMETER AssemblyPath
    Path of the built activity assembly; overrides AssemblyPath from the configuration.

.PARAMETER NoRestart
    Skips restarting FIMService and IIS (restart them manually before the activity is used).

.EXAMPLE
    .\Install-ActorInheritanceActivity.ps1

.NOTES
    Author : N.Stillhart
    Date   : 2026-09-28
#>
#Requires -Version 5.1
#Requires -RunAsAdministrator
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string] $ConfigPath = (Join-Path -Path $PSScriptRoot -ChildPath 'ActorInheritance.config.psd1'),

    [string] $AssemblyPath,

    [switch] $NoRestart
)

$ErrorActionPreference = 'Stop'
. (Join-Path -Path $PSScriptRoot -ChildPath 'ActorInheritance.Fim.ps1')

if (-not $AssemblyPath) {
    $config = Import-ActorInheritanceConfig -Path $ConfigPath
    $AssemblyPath = Join-Path -Path $PSScriptRoot -ChildPath $config.AssemblyPath
}

$assemblyName = Get-SignedAssemblyName -Path $AssemblyPath
$fullPath = (Resolve-Path -LiteralPath $AssemblyPath).ProviderPath
$publicKeyToken = ($assemblyName.GetPublicKeyToken() | ForEach-Object { $_.ToString('x2') }) -join ''
$gacFile = Join-Path -Path $env:windir -ChildPath ('Microsoft.NET\assembly\GAC_MSIL\{0}\v4.0_{1}__{2}\{0}.dll' -f $assemblyName.Name, $assemblyName.Version, $publicKeyToken)

if ($PSCmdlet.ShouldProcess($assemblyName.FullName, 'Install in GAC')) {
    Add-Type -AssemblyName System.EnterpriseServices
    $publish = New-Object -TypeName System.EnterpriseServices.Internal.Publish
    $publish.GacInstall($fullPath)

    # GacInstall does not report failures, so verify the result.
    if (-not (Test-Path -LiteralPath $gacFile)) {
        throw "GAC installation failed: '$gacFile' not found."
    }

    if ((Get-FileHash -LiteralPath $gacFile).Hash -ne (Get-FileHash -LiteralPath $fullPath).Hash) {
        throw "GAC installation failed: '$gacFile' differs from '$fullPath'."
    }

    Write-Host "Installed $($assemblyName.FullName)"
}

if ($NoRestart) {
    Write-Warning 'Restart FIMService and run iisreset before using the activity.'
    return
}

if ((Get-Service -Name 'FIMService' -ErrorAction SilentlyContinue) -and $PSCmdlet.ShouldProcess('FIMService', 'Restart')) {
    Restart-Service -Name 'FIMService' -Force
    Write-Host 'Restarted FIMService'
}

if ((Get-Service -Name 'W3SVC' -ErrorAction SilentlyContinue) -and $PSCmdlet.ShouldProcess('IIS', 'Reset')) {
    & (Join-Path -Path $env:windir -ChildPath 'System32\iisreset.exe')
    if ($LASTEXITCODE -ne 0) {
        throw "iisreset failed with exit code $LASTEXITCODE."
    }
}
