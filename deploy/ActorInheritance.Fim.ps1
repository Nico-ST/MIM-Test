# -----------------------------------------------------------------------------
# Author : N.Stillhart
# Date   : 2026-09-28
# Purpose: Helper functions around the FIMAutomation snap-in, shared by the
#          actor inheritance scripts. Dot-source this file (it is not a
#          module, so -WhatIf of the calling script applies to the helpers).
#          Requires Windows PowerShell 5.1 on a MIM Service server.
# -----------------------------------------------------------------------------

$script:FimServiceUri = 'http://localhost:5725'
$script:FimSystemNamePattern = '^[A-Za-z][A-Za-z0-9_]{0,127}$'
$script:FimProtectedAttributes = @('ObjectID', 'ObjectType', 'CreatedTime', 'Creator', 'DeletedTime', 'ResourceTime', 'MVObjectID')
$script:FimWhatIfObjectId = 'urn:uuid:00000000-0000-0000-0000-000000000000'

function Initialize-FimAutomation {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string] $Uri
    )

    if (-not (Get-PSSnapin -Name FIMAutomation -ErrorAction SilentlyContinue)) {
        Add-PSSnapin -Name FIMAutomation -ErrorAction Stop
    }

    $script:FimServiceUri = $Uri
}

function Import-ActorInheritanceConfig {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string] $Path
    )

    $config = Import-PowerShellDataFile -Path $Path
    $inherited = @($config.InheritedAttributes)

    foreach ($name in (@($config.IdentityObjectType, $config.MainIdentityAttribute) + $inherited)) {
        if ($name -cnotmatch $script:FimSystemNamePattern) {
            throw "Invalid MIM system name '$name' in '$Path'."
        }
    }

    if ($inherited.Count -eq 0) {
        throw "InheritedAttributes in '$Path' must contain at least one attribute."
    }

    foreach ($name in $inherited) {
        if ($name -eq $config.MainIdentityAttribute -or $script:FimProtectedAttributes -contains $name) {
            throw "Attribute '$name' cannot be inherited."
        }
    }

    foreach ($text in @($config.DisplayNamePrefix, $config.RequestorSet)) {
        if ([string]::IsNullOrWhiteSpace($text) -or $text.Contains("'")) {
            throw "DisplayNamePrefix and RequestorSet in '$Path' must be set and must not contain single quotes."
        }
    }

    return $config
}

function Get-SignedAssemblyName {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string] $Path
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "Activity assembly '$Path' not found. Build it first with Build-ActorInheritance.ps1."
    }

    $name = [System.Reflection.AssemblyName]::GetAssemblyName((Resolve-Path -LiteralPath $Path).ProviderPath)
    $token = $name.GetPublicKeyToken()
    if ($null -eq $token -or $token.Length -eq 0) {
        throw "Activity assembly '$Path' is not strong-name signed and cannot be installed in the GAC."
    }

    return $name
}

function Get-FimObject {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string] $XPath
    )

    $result = Export-FIMConfig -Uri $script:FimServiceUri -OnlyBaseResources -CustomConfig $XPath -ErrorAction Stop
    if ($null -ne $result) {
        $result
    }
}

function Get-FimObjectId {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string] $XPath,

        [switch] $Required
    )

    $objects = @(Get-FimObject -XPath $XPath)
    if ($objects.Count -gt 1) {
        throw "XPath '$XPath' matches $($objects.Count) resources; expected one."
    }

    if ($objects.Count -eq 0) {
        if ($Required) {
            throw "No resource matches '$XPath'."
        }

        return $null
    }

    return $objects[0].ResourceManagementObject.ObjectIdentifier
}

function Get-FimAttributeValue {
    <#
    .SYNOPSIS
        Writes the value(s) of an attribute of an exported resource to the pipeline (nothing when unset).
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        $ExportObject,

        [Parameter(Mandatory = $true)]
        [string] $AttributeName
    )

    $attribute = $ExportObject.ResourceManagementObject.ResourceManagementAttributes |
        Where-Object { $_.AttributeName -eq $AttributeName } |
        Select-Object -First 1

    if ($null -eq $attribute) {
        return
    }

    if ($attribute.IsMultiValue) {
        $attribute.Values
    }
    elseif ($null -ne $attribute.Value) {
        $attribute.Value
    }
}

function ConvertTo-FimValueString {
    param($Value)

    if ($Value -is [bool]) {
        if ($Value) { return 'True' }
        return 'False'
    }

    return [string] $Value
}

function New-FimSetFilter {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string] $XPath
    )

    $escaped = $XPath.Replace('&', '&amp;').Replace('<', '&lt;').Replace('>', '&gt;')
    return '<Filter xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:xsd="http://www.w3.org/2001/XMLSchema" Dialect="http://schemas.microsoft.com/2006/11/XPathFilterDialect" xmlns="http://schemas.xmlsoap.org/ws/2004/09/enumeration">{0}</Filter>' -f $escaped
}

function New-FimImportObject {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string] $ObjectType,

        [Parameter(Mandatory = $true)]
        [ValidateSet('Create', 'Put')]
        [string] $State,

        [string] $ObjectIdentifier
    )

    $importObject = New-Object -TypeName Microsoft.ResourceManagement.Automation.ObjectModel.ImportObject
    $importObject.ObjectType = $ObjectType
    $importObject.State = [Microsoft.ResourceManagement.Automation.ObjectModel.ImportState] $State

    if ($State -eq 'Create') {
        $importObject.SourceObjectIdentifier = [guid]::NewGuid().ToString()
    }
    else {
        $importObject.SourceObjectIdentifier = $ObjectIdentifier
        $importObject.TargetObjectIdentifier = $ObjectIdentifier
    }

    return $importObject
}

function Add-FimImportChange {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        $ImportObject,

        [Parameter(Mandatory = $true)]
        [string] $AttributeName,

        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string] $Value,

        [Parameter(Mandatory = $true)]
        [ValidateSet('Add', 'Replace', 'Delete')]
        [string] $Operation
    )

    $change = New-Object -TypeName Microsoft.ResourceManagement.Automation.ObjectModel.ImportChange
    $change.Operation = [Microsoft.ResourceManagement.Automation.ObjectModel.ImportOperation] $Operation
    $change.AttributeName = $AttributeName
    $change.AttributeValue = $Value
    $change.FullyResolved = $true
    $change.Locale = 'Invariant'
    $ImportObject.Changes += $change
}

function Set-FimObject {
    <#
    .SYNOPSIS
        Creates a MIM Service resource, or updates it when the XPath already finds it (idempotent).
    .DESCRIPTION
        Attributes maps attribute names to values; an array sets a multi-valued attribute (list those
        in MultiValuedAttributes). Multi-valued attributes are made equal to the given values.
        Returns the ObjectIdentifier (urn:uuid:...) of the resource.
    #>
    [CmdletBinding(SupportsShouldProcess = $true)]
    param(
        [Parameter(Mandatory = $true)]
        [string] $ObjectType,

        [Parameter(Mandatory = $true)]
        [string] $XPath,

        [Parameter(Mandatory = $true)]
        [hashtable] $Attributes,

        [string[]] $MultiValuedAttributes = @()
    )

    $label = "$ObjectType '$($Attributes['DisplayName'])'"
    $existing = @(Get-FimObject -XPath $XPath)
    if ($existing.Count -gt 1) {
        throw "XPath '$XPath' matches $($existing.Count) resources; expected at most one."
    }

    if ($existing.Count -eq 0) {
        $importObject = New-FimImportObject -ObjectType $ObjectType -State Create
        foreach ($name in $Attributes.Keys) {
            $operation = 'Replace'
            if ($MultiValuedAttributes -contains $name) {
                $operation = 'Add'
            }

            foreach ($value in @($Attributes[$name])) {
                if ($null -ne $value) {
                    Add-FimImportChange -ImportObject $importObject -AttributeName $name -Value (ConvertTo-FimValueString $value) -Operation $operation
                }
            }
        }

        if (-not $PSCmdlet.ShouldProcess($label, 'Create')) {
            return $script:FimWhatIfObjectId
        }

        Import-FIMConfig -Uri $script:FimServiceUri -ImportObject $importObject -ErrorAction Stop | Out-Null
        Write-Host "  created  $label"
        return (Get-FimObjectId -XPath $XPath -Required)
    }

    $current = $existing[0]
    $objectId = $current.ResourceManagementObject.ObjectIdentifier
    $importObject = New-FimImportObject -ObjectType $ObjectType -State Put -ObjectIdentifier $objectId
    $changed = New-Object -TypeName System.Collections.Generic.List[string]

    foreach ($name in $Attributes.Keys) {
        $desired = @(@($Attributes[$name]) | Where-Object { $null -ne $_ } | ForEach-Object { ConvertTo-FimValueString $_ })
        $actual = @(Get-FimAttributeValue -ExportObject $current -AttributeName $name | ForEach-Object { ConvertTo-FimValueString $_ })

        if ($MultiValuedAttributes -contains $name) {
            foreach ($value in $desired) {
                if ($actual -notcontains $value) {
                    Add-FimImportChange -ImportObject $importObject -AttributeName $name -Value $value -Operation Add
                    $changed.Add($name)
                }
            }

            foreach ($value in $actual) {
                if ($desired -notcontains $value) {
                    Add-FimImportChange -ImportObject $importObject -AttributeName $name -Value $value -Operation Delete
                    $changed.Add($name)
                }
            }
        }
        elseif ($desired.Count -eq 0) {
            if ($actual.Count -gt 0) {
                Add-FimImportChange -ImportObject $importObject -AttributeName $name -Value $actual[0] -Operation Delete
                $changed.Add($name)
            }
        }
        elseif ($actual.Count -eq 0 -or $actual[0] -cne $desired[0]) {
            Add-FimImportChange -ImportObject $importObject -AttributeName $name -Value $desired[0] -Operation Replace
            $changed.Add($name)
        }
    }

    if ($changed.Count -eq 0) {
        Write-Host "  current  $label"
        return $objectId
    }

    $changedList = ($changed | Select-Object -Unique) -join ', '
    if ($PSCmdlet.ShouldProcess($label, "Update $changedList")) {
        Import-FIMConfig -Uri $script:FimServiceUri -ImportObject $importObject -ErrorAction Stop | Out-Null
        Write-Host "  updated  $label ($changedList)"
    }

    return $objectId
}

function Add-FimAttributeValue {
    <#
    .SYNOPSIS
        Adds a value to a multi-valued attribute of an existing resource unless it is already present.
    #>
    [CmdletBinding(SupportsShouldProcess = $true)]
    param(
        [Parameter(Mandatory = $true)]
        [string] $ObjectType,

        [Parameter(Mandatory = $true)]
        [string] $XPath,

        [Parameter(Mandatory = $true)]
        [string] $AttributeName,

        [Parameter(Mandatory = $true)]
        [string] $Value
    )

    $objects = @(Get-FimObject -XPath $XPath)
    if ($objects.Count -ne 1) {
        throw "Expected exactly one resource for '$XPath', found $($objects.Count)."
    }

    $current = @(Get-FimAttributeValue -ExportObject $objects[0] -AttributeName $AttributeName)
    if ($current -contains $Value) {
        Write-Host "  current  $ObjectType $XPath ($AttributeName)"
        return
    }

    if ($PSCmdlet.ShouldProcess("$ObjectType $XPath", "Add $AttributeName value $Value")) {
        $importObject = New-FimImportObject -ObjectType $ObjectType -State Put -ObjectIdentifier $objects[0].ResourceManagementObject.ObjectIdentifier
        Add-FimImportChange -ImportObject $importObject -AttributeName $AttributeName -Value $Value -Operation Add
        Import-FIMConfig -Uri $script:FimServiceUri -ImportObject $importObject -ErrorAction Stop | Out-Null
        Write-Host "  updated  $ObjectType $XPath ($AttributeName)"
    }
}
