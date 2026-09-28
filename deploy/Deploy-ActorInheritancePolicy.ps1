<#
.SYNOPSIS
    Deploys the MIM Service configuration for actor inheritance (main identity -> sub-identities).

.DESCRIPTION
    Creates or updates (idempotent, safe to re-run):
      - the activity registration (ActivityInformationConfiguration) for the Portal workflow designer
      - Set "<prefix>: Sub-Identities"   (identities that reference a main identity)
      - Set "<prefix>: Main Identities"  (identities referenced as main identity)
      - Action workflow "<prefix>: Inherit Actor Attributes"
      - MPR "<prefix>: Sub-Identity Linked"           (transition-in, sub-identity created or linked)
      - MPR "<prefix>: Sub-Identity Relinked"         (request, main identity reference changed)
      - MPR "<prefix>: Main Identity Actor Changed"   (request, actor attribute of a main identity changed)

    Validates the schema (main identity attribute and inherited attributes must exist and be bound to the
    identity object type) and makes the main identity attribute usable in set filters.

    The activity assembly must be installed in the GAC of the MIM Service first
    (Install-ActorInheritanceActivity.ps1), otherwise the MIM Service rejects the workflow definition.

.PARAMETER ConfigPath
    Path of the configuration file. Default: ActorInheritance.config.psd1 next to this script.

.PARAMETER AssemblyPath
    Path of the built activity assembly; overrides AssemblyPath from the configuration. Used to read the
    assembly's full name, including the PublicKeyToken.

.PARAMETER CreateMainIdentityAttribute
    Creates the main identity attribute (single-valued Reference) and its binding when they are missing.

.EXAMPLE
    .\Deploy-ActorInheritancePolicy.ps1 -WhatIf

    Shows what would be created or changed.

.EXAMPLE
    .\Deploy-ActorInheritancePolicy.ps1 -CreateMainIdentityAttribute

.NOTES
    Author : N.Stillhart
    Date   : 2026-09-28
    Run in Windows PowerShell 5.1 on a MIM Service server, as a member of the MIM "Administrators" set.
#>
#Requires -Version 5.1
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string] $ConfigPath = (Join-Path -Path $PSScriptRoot -ChildPath 'ActorInheritance.config.psd1'),

    [string] $AssemblyPath,

    [switch] $CreateMainIdentityAttribute
)

$ErrorActionPreference = 'Stop'
. (Join-Path -Path $PSScriptRoot -ChildPath 'ActorInheritance.Fim.ps1')

$ActivityTypeName = 'MIM.ActorInheritance.Workflow.ActorInheritanceActivity'
$ActivityUiTypeName = 'MIM.ActorInheritance.Workflow.ActorInheritanceActivitySettingsPart'
$FilterPermissionName = 'Administrator Filter Permission'
$DefaultResourceManagementAssembly = 'Microsoft.ResourceManagement, Version=4.1.3419.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35'
$MprMultiValuedAttributes = @('ActionType', 'ActionParameter', 'ActionWorkflowDefinition')

function Get-AttributeBindingXPath {
    param([string] $ObjectType, [string] $AttributeName)

    return "/BindingDescription[BoundObjectType=/ObjectTypeDescription[Name='$ObjectType'] and BoundAttributeType=/AttributeTypeDescription[Name='$AttributeName']]"
}

function Assert-InheritedAttribute {
    param([string] $ObjectType, [string] $AttributeName)

    if (-not (Get-FimObjectId -XPath "/AttributeTypeDescription[Name='$AttributeName']")) {
        throw "Inherited attribute '$AttributeName' does not exist in the MIM schema."
    }

    if (-not (Get-FimObjectId -XPath (Get-AttributeBindingXPath -ObjectType $ObjectType -AttributeName $AttributeName))) {
        throw "Inherited attribute '$AttributeName' is not bound to '$ObjectType'."
    }
}

function Confirm-MainIdentityAttribute {
    <#
    .SYNOPSIS
        Verifies (or creates) the main identity reference attribute and its binding; returns its ObjectIdentifier.
    #>
    param([string] $ObjectType, [string] $AttributeName, [bool] $Create)

    $attributeXPath = "/AttributeTypeDescription[Name='$AttributeName']"
    $attribute = @(Get-FimObject -XPath $attributeXPath) | Select-Object -First 1

    if ($attribute) {
        $dataType = @(Get-FimAttributeValue -ExportObject $attribute -AttributeName 'DataType') | Select-Object -First 1
        $multivalued = @(Get-FimAttributeValue -ExportObject $attribute -AttributeName 'Multivalued') | Select-Object -First 1
        if ($dataType -ne 'Reference' -or (ConvertTo-FimValueString $multivalued) -ne 'False') {
            throw "Attribute '$AttributeName' must be a single-valued Reference attribute (found DataType '$dataType', Multivalued '$multivalued')."
        }

        $attributeId = $attribute.ResourceManagementObject.ObjectIdentifier
    }
    elseif ($Create) {
        $attributeId = Set-FimObject -ObjectType 'AttributeTypeDescription' -XPath $attributeXPath -Attributes @{
            Name        = $AttributeName
            DisplayName = 'Main Identity'
            Description = 'Main identity this sub-identity belongs to. Sub-identities inherit its actor attributes.'
            DataType    = 'Reference'
            Multivalued = $false
        }
    }
    else {
        throw "Attribute '$AttributeName' does not exist. Create it as single-valued Reference attribute or re-run with -CreateMainIdentityAttribute."
    }

    $bindingXPath = Get-AttributeBindingXPath -ObjectType $ObjectType -AttributeName $AttributeName
    if (-not (Get-FimObjectId -XPath $bindingXPath)) {
        if (-not $Create) {
            throw "Attribute '$AttributeName' is not bound to '$ObjectType'. Bind it or re-run with -CreateMainIdentityAttribute."
        }

        Set-FimObject -ObjectType 'BindingDescription' -XPath $bindingXPath -Attributes @{
            DisplayName        = 'Main Identity'
            Description        = 'Main identity this sub-identity belongs to. Sub-identities inherit its actor attributes.'
            BoundObjectType    = Get-FimObjectId -XPath "/ObjectTypeDescription[Name='$ObjectType']" -Required
            BoundAttributeType = $attributeId
            Required           = $false
        } | Out-Null
    }

    return $attributeId
}

function Get-ResourceManagementAssemblyName {
    <#
    .SYNOPSIS
        Reads the Microsoft.ResourceManagement assembly reference from an existing workflow's XOML,
        so the new XOML matches the installed MIM version.
    #>
    $pattern = 'Assembly=(Microsoft\.ResourceManagement, Version=[0-9.]+, Culture=neutral, PublicKeyToken=31bf3856ad364e35)'
    foreach ($workflow in @(Get-FimObject -XPath "/WorkflowDefinition[RequestPhase='Action']")) {
        $xoml = @(Get-FimAttributeValue -ExportObject $workflow -AttributeName 'XOML') | Select-Object -First 1
        if ($xoml -match $pattern) {
            return $Matches[1]
        }
    }

    Write-Warning "No existing workflow found to read the MIM assembly version from; using '$DefaultResourceManagementAssembly'."
    return $DefaultResourceManagementAssembly
}

function New-ActorInheritanceXoml {
    param(
        [string] $ResourceManagementAssembly,
        [string] $ActivityAssembly,
        [string] $ObjectType,
        [string] $MainIdentityAttribute,
        [string[]] $InheritedAttributes
    )

    $escape = { param([string] $Text) [System.Security.SecurityElement]::Escape($Text) }
    $emptyId = '00000000-0000-0000-0000-000000000000'

    return @(
        ('<ns0:SequentialWorkflow x:Name="SequentialWorkflow" ActorId="{0}" WorkflowDefinitionId="{0}" RequestId="{0}" TargetId="{0}" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" xmlns:ns0="clr-namespace:Microsoft.ResourceManagement.Workflow.Activities;Assembly={1}" xmlns="http://schemas.microsoft.com/winfx/2006/xaml/workflow">' -f $emptyId, (& $escape $ResourceManagementAssembly)),
        ('  <ns1:ActorInheritanceActivity x:Name="actorInheritanceActivity1" IdentityObjectType="{0}" MainIdentityAttribute="{1}" InheritedAttributes="{2}" xmlns:ns1="clr-namespace:MIM.ActorInheritance.Workflow;Assembly={3}" />' -f (& $escape $ObjectType), (& $escape $MainIdentityAttribute), (& $escape ($InheritedAttributes -join ';')), (& $escape $ActivityAssembly)),
        '</ns0:SequentialWorkflow>'
    ) -join "`r`n"
}

# --- Configuration -----------------------------------------------------------

$config = Import-ActorInheritanceConfig -Path $ConfigPath
$objectType = $config.IdentityObjectType
$mainAttribute = $config.MainIdentityAttribute
$inheritedAttributes = @($config.InheritedAttributes)
$prefix = $config.DisplayNamePrefix

if (-not $AssemblyPath) {
    $AssemblyPath = Join-Path -Path $PSScriptRoot -ChildPath $config.AssemblyPath
}

$activityAssembly = Get-SignedAssemblyName -Path $AssemblyPath
Initialize-FimAutomation -Uri $config.ServiceUri

$names = @{
    SubSet        = '{0}: Sub-Identities' -f $prefix
    MainSet       = '{0}: Main Identities' -f $prefix
    Workflow      = '{0}: Inherit Actor Attributes' -f $prefix
    MprLinked     = '{0}: Sub-Identity Linked' -f $prefix
    MprRelinked   = '{0}: Sub-Identity Relinked' -f $prefix
    MprMainChange = '{0}: Main Identity Actor Changed' -f $prefix
}

Write-Host "Actor inheritance: $objectType.$mainAttribute -> [$($inheritedAttributes -join ', ')]"
Write-Host "Activity assembly: $($activityAssembly.FullName)"

# --- 1. Schema ---------------------------------------------------------------

Write-Host 'Schema'
$mainAttributeId = Confirm-MainIdentityAttribute -ObjectType $objectType -AttributeName $mainAttribute -Create $CreateMainIdentityAttribute.IsPresent
foreach ($attribute in $inheritedAttributes) {
    Assert-InheritedAttribute -ObjectType $objectType -AttributeName $attribute
}

# Set filters may only use attributes listed in the administrator filter permission.
Add-FimAttributeValue -ObjectType 'FilterScope' -XPath "/FilterScope[DisplayName='$FilterPermissionName']" -AttributeName 'AllowedAttributes' -Value $mainAttributeId

# --- 2. Activity registration --------------------------------------------------

Write-Host 'Activity'
Set-FimObject -ObjectType 'ActivityInformationConfiguration' -XPath "/ActivityInformationConfiguration[ActivityName='$ActivityTypeName']" -Attributes @{
    DisplayName              = 'Actor Inheritance'
    Description              = 'Lets sub-identities inherit the actor attributes of their main identity.'
    ActivityName             = $ActivityTypeName
    AssemblyName             = $activityAssembly.FullName
    TypeName                 = $ActivityUiTypeName
    IsActionActivity         = $true
    IsAuthenticationActivity = $false
    IsAuthorizationActivity  = $false
    IsConfigurationType      = $true
} | Out-Null

# --- 3. Sets -------------------------------------------------------------------

Write-Host 'Sets'
$subSetId = Set-FimObject -ObjectType 'Set' -XPath "/Set[DisplayName='$($names.SubSet)']" -Attributes @{
    DisplayName = $names.SubSet
    Description = "All $objectType resources that reference a main identity in '$mainAttribute'. Managed by the actor inheritance deployment."
    Filter      = New-FimSetFilter -XPath ('/{0}[{1} = /{0}]' -f $objectType, $mainAttribute)
}

$mainSetId = Set-FimObject -ObjectType 'Set' -XPath "/Set[DisplayName='$($names.MainSet)']" -Attributes @{
    DisplayName = $names.MainSet
    Description = "All $objectType resources that are the main identity of at least one sub-identity. Managed by the actor inheritance deployment."
    Filter      = New-FimSetFilter -XPath ('/{0}[ObjectID = /{0}/{1}]' -f $objectType, $mainAttribute)
}

# --- 4. Workflow ---------------------------------------------------------------

Write-Host 'Workflow'
$xoml = New-ActorInheritanceXoml `
    -ResourceManagementAssembly (Get-ResourceManagementAssemblyName) `
    -ActivityAssembly $activityAssembly.FullName `
    -ObjectType $objectType `
    -MainIdentityAttribute $mainAttribute `
    -InheritedAttributes $inheritedAttributes

$workflowId = Set-FimObject -ObjectType 'WorkflowDefinition' -XPath "/WorkflowDefinition[DisplayName='$($names.Workflow)']" -Attributes @{
    DisplayName       = $names.Workflow
    Description       = "Sub-identities inherit [$($inheritedAttributes -join ', ')] from the main identity referenced in '$mainAttribute'."
    RequestPhase      = 'Action'
    RunOnPolicyUpdate = [bool] $config.RunOnPolicyUpdate
    XOML              = $xoml
}

# --- 5. Management policy rules ------------------------------------------------

Write-Host 'Management policy rules'
$requestorSetId = Get-FimObjectId -XPath "/Set[DisplayName='$($config.RequestorSet)']" -Required

Set-FimObject -ObjectType 'ManagementPolicyRule' -XPath "/ManagementPolicyRule[DisplayName='$($names.MprRelinked)']" -MultiValuedAttributes $MprMultiValuedAttributes -Attributes @{
    DisplayName              = $names.MprRelinked
    Description              = "A sub-identity is assigned to another main identity: it inherits the actor attributes of the new main identity."
    ManagementPolicyRuleType = 'Request'
    ActionType               = @('Modify')
    ActionParameter          = @($mainAttribute)
    PrincipalSet             = $requestorSetId
    ResourceCurrentSet       = $subSetId
    ResourceFinalSet         = $subSetId
    ActionWorkflowDefinition = @($workflowId)
    GrantRight               = $false
    Disabled                 = $false
} | Out-Null

Set-FimObject -ObjectType 'ManagementPolicyRule' -XPath "/ManagementPolicyRule[DisplayName='$($names.MprMainChange)']" -MultiValuedAttributes $MprMultiValuedAttributes -Attributes @{
    DisplayName              = $names.MprMainChange
    Description              = "An actor attribute of a main identity changes: all its sub-identities inherit the new value."
    ManagementPolicyRuleType = 'Request'
    ActionType               = @('Add', 'Remove', 'Modify')
    ActionParameter          = $inheritedAttributes
    PrincipalSet             = $requestorSetId
    ResourceCurrentSet       = $mainSetId
    ResourceFinalSet         = $mainSetId
    ActionWorkflowDefinition = @($workflowId)
    GrantRight               = $false
    Disabled                 = $false
} | Out-Null

# Created last: with RunOnPolicyUpdate the workflow runs for every existing sub-identity (initial load).
Set-FimObject -ObjectType 'ManagementPolicyRule' -XPath "/ManagementPolicyRule[DisplayName='$($names.MprLinked)']" -MultiValuedAttributes $MprMultiValuedAttributes -Attributes @{
    DisplayName              = $names.MprLinked
    Description              = "A sub-identity is created or linked to a main identity: it inherits the actor attributes of the main identity."
    ManagementPolicyRuleType = 'SetTransition'
    ActionType               = @('TransitionIn')
    ActionParameter          = @('*')
    ResourceFinalSet         = $subSetId
    ActionWorkflowDefinition = @($workflowId)
    GrantRight               = $false
    Disabled                 = $false
} | Out-Null

Write-Host 'Done. Verify with .\Test-ActorInheritance.ps1 once the MIM Service has processed the requests.'
