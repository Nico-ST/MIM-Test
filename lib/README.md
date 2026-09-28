# MIM reference assemblies

The build expects these MIM assemblies in this folder (they are not committed):

- `Microsoft.ResourceManagement.dll` (MIM Service; GAC or `C:\Program Files\Microsoft Forefront Identity Manager\2010\Service`)
- `Microsoft.IdentityManagement.WebUI.Controls.dll` (MIM Portal; GAC)

`deploy/Build-ActorInheritance.ps1` copies them automatically when run on a MIM server.
