<#
.SYNOPSIS
    Reports sub-identities whose actor attributes differ from their main identity (read-only).

.DESCRIPTION
    Compares every inherited attribute of every sub-identity with its main identity and writes one object per
    deviating attribute. Use it after the deployment (to confirm the initial load) and for periodic checks.

.PARAMETER ConfigPath
    Path of the configuration file. Default: ActorInheritance.config.psd1 next to this script.

.PARAMETER CsvPath
    Optional path of a CSV file the deviations are written to.

.EXAMPLE
    .\Test-ActorInheritance.ps1 | Format-Table -AutoSize

.EXAMPLE
    .\Test-ActorInheritance.ps1 -CsvPath .\actor-inheritance-deviations.csv

.NOTES
    Author : N.Stillhart
    Date   : 2026-09-28
#>
#Requires -Version 5.1
[CmdletBinding()]
param(
    [string] $ConfigPath = (Join-Path -Path $PSScriptRoot -ChildPath 'ActorInheritance.config.psd1'),

    [string] $CsvPath
)

$ErrorActionPreference = 'Stop'
. (Join-Path -Path $PSScriptRoot -ChildPath 'ActorInheritance.Fim.ps1')

$config = Import-ActorInheritanceConfig -Path $ConfigPath
$objectType = $config.IdentityObjectType
$mainAttribute = $config.MainIdentityAttribute
$inheritedAttributes = @($config.InheritedAttributes)
Initialize-FimAutomation -Uri $config.ServiceUri

function Get-DisplayName($ExportObject) {
    $name = @(Get-FimAttributeValue -ExportObject $ExportObject -AttributeName 'DisplayName') | Select-Object -First 1
    if ($name) {
        return $name
    }

    return $ExportObject.ResourceManagementObject.ObjectIdentifier
}

function Get-ComparableValue($ExportObject, [string] $AttributeName) {
    Get-FimAttributeValue -ExportObject $ExportObject -AttributeName $AttributeName |
        ForEach-Object { ConvertTo-FimValueString $_ } |
        Sort-Object -CaseSensitive
}

$subIdentities = @(Get-FimObject -XPath ('/{0}[{1} = /{0}]' -f $objectType, $mainAttribute))
$mainIdentities = @{}
foreach ($identity in @(Get-FimObject -XPath ('/{0}[ObjectID = /{0}/{1}]' -f $objectType, $mainAttribute))) {
    $mainIdentities[$identity.ResourceManagementObject.ObjectIdentifier] = $identity
}

$deviations = @(
    foreach ($subIdentity in $subIdentities) {
        $mainIdentityId = @(Get-FimAttributeValue -ExportObject $subIdentity -AttributeName $mainAttribute) | Select-Object -First 1
        $mainIdentity = $mainIdentities[$mainIdentityId]
        if ($null -eq $mainIdentity) {
            continue
        }

        foreach ($attribute in $inheritedAttributes) {
            $expected = @(Get-ComparableValue -ExportObject $mainIdentity -AttributeName $attribute)
            $actual = @(Get-ComparableValue -ExportObject $subIdentity -AttributeName $attribute)
            if (($expected -join "`n") -cne ($actual -join "`n")) {
                [pscustomobject]@{
                    SubIdentity   = Get-DisplayName $subIdentity
                    SubIdentityId = $subIdentity.ResourceManagementObject.ObjectIdentifier
                    MainIdentity  = Get-DisplayName $mainIdentity
                    Attribute     = $attribute
                    Expected      = $expected -join '; '
                    Actual        = $actual -join '; '
                }
            }
        }
    }
)

Write-Host ('{0} sub-identities checked, {1} deviating attribute values.' -f $subIdentities.Count, $deviations.Count)

if ($CsvPath) {
    $deviations | Export-Csv -LiteralPath $CsvPath -NoTypeInformation -Encoding UTF8
}

$deviations
