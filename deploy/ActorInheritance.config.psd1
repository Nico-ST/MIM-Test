# -----------------------------------------------------------------------------
# Author : N.Stillhart
# Date   : 2026-09-28
# Purpose: Environment configuration for the MIM actor inheritance workflow.
#          Used by Deploy-ActorInheritancePolicy.ps1 and Test-ActorInheritance.ps1.
# -----------------------------------------------------------------------------
@{
    # MIM Service endpoint used by the FIMAutomation cmdlets.
    ServiceUri            = 'http://localhost:5725'

    # Object type of main identities and sub-identities.
    IdentityObjectType    = 'Person'

    # Single-valued reference attribute on the sub-identity that points to its main identity.
    MainIdentityAttribute = 'MainIdentity'

    # Actor attributes the sub-identities inherit from their main identity.
    # Single- and multi-valued attributes of any data type are supported.
    InheritedAttributes   = @(
        'Actor'
    )

    # Prefix for the display names of all MIM objects created by the deployment.
    DisplayNamePrefix     = 'Actor Inheritance'

    # Set whose members may trigger inheritance as requestors (includes the sync engine and FIM service account).
    RequestorSet          = 'All People'

    # When $true, enabling the transition MPR runs the workflow for every existing sub-identity (initial load).
    RunOnPolicyUpdate     = $true

    # Build output of the activity, relative to this folder.
    AssemblyPath          = '..\src\MIM.ActorInheritance.Workflow\bin\Release\MIM.ActorInheritance.Workflow.dll'
}
