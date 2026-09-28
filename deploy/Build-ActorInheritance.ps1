<#
.SYNOPSIS
    Builds the strong-name signed actor inheritance activity assembly.

.DESCRIPTION
    1. Copies the MIM reference assemblies into .\lib when they are missing. They are searched in the GAC
       and the MIM Service folder, so running this on a MIM server needs no manual preparation.
    2. Creates the strong name key ActorInheritance.snk when it is missing.
    3. Builds with the dotnet CLI, or with MSBuild (Visual Studio / Build Tools) when dotnet is not installed.

    Output: src\MIM.ActorInheritance.Workflow\bin\<Configuration>\MIM.ActorInheritance.Workflow.dll

.PARAMETER Configuration
    Build configuration. Default: Release (the install and deploy scripts expect Release).

.PARAMETER MimReferencePath
    Folder with Microsoft.ResourceManagement.dll and Microsoft.IdentityManagement.WebUI.Controls.dll.
    Default: .\lib

.PARAMETER KeyFile
    Strong name key. Default: .\ActorInheritance.snk

.EXAMPLE
    .\Build-ActorInheritance.ps1

.NOTES
    Author : N.Stillhart
    Date   : 2026-09-28
#>
#Requires -Version 5.1
[CmdletBinding()]
param(
    [ValidateSet('Release', 'Debug')]
    [string] $Configuration = 'Release',

    [string] $MimReferencePath,

    [string] $KeyFile
)

$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Path $PSScriptRoot -Parent
$project = Join-Path -Path $repositoryRoot -ChildPath 'src\MIM.ActorInheritance.Workflow\MIM.ActorInheritance.Workflow.csproj'
if (-not $MimReferencePath) {
    $MimReferencePath = Join-Path -Path $repositoryRoot -ChildPath 'lib'
}

if (-not $KeyFile) {
    $KeyFile = Join-Path -Path $repositoryRoot -ChildPath 'ActorInheritance.snk'
}

function Copy-MimReference {
    param([string] $FileName, [string[]] $SearchFolders, [string] $Destination)

    $target = Join-Path -Path $Destination -ChildPath $FileName
    if (Test-Path -LiteralPath $target) {
        return
    }

    foreach ($folder in $SearchFolders) {
        if (-not (Test-Path -LiteralPath $folder)) {
            continue
        }

        $found = Get-ChildItem -LiteralPath $folder -Filter $FileName -Recurse -File -ErrorAction SilentlyContinue |
            Sort-Object -Property FullName |
            Select-Object -Last 1
        if ($found) {
            New-Item -Path $Destination -ItemType Directory -Force | Out-Null
            Copy-Item -LiteralPath $found.FullName -Destination $target
            Write-Host "Copied $FileName from $($found.DirectoryName)"
            return
        }
    }

    throw "$FileName not found. Copy it from a MIM Service / MIM Portal server into '$Destination'."
}

function Find-MSBuild {
    $vswhere = Join-Path -Path ${env:ProgramFiles(x86)} -ChildPath 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (Test-Path -LiteralPath $vswhere) {
        $path = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
        if ($path) {
            return $path
        }
    }

    throw 'Neither the dotnet CLI nor MSBuild (Visual Studio 2017 or later / Build Tools) was found.'
}

# 1. MIM reference assemblies
$gac = Join-Path -Path $env:windir -ChildPath 'Microsoft.NET\assembly\GAC_MSIL'
Copy-MimReference -FileName 'Microsoft.ResourceManagement.dll' -Destination $MimReferencePath -SearchFolders @(
    (Join-Path -Path $gac -ChildPath 'Microsoft.ResourceManagement'),
    (Join-Path -Path $env:ProgramFiles -ChildPath 'Microsoft Forefront Identity Manager\2010\Service')
)
Copy-MimReference -FileName 'Microsoft.IdentityManagement.WebUI.Controls.dll' -Destination $MimReferencePath -SearchFolders @(
    (Join-Path -Path $gac -ChildPath 'Microsoft.IdentityManagement.WebUI.Controls')
)

# 2. Strong name key (an .snk file is a CryptoAPI RSA private key blob)
if (-not (Test-Path -LiteralPath $KeyFile)) {
    $rsa = New-Object -TypeName System.Security.Cryptography.RSACryptoServiceProvider -ArgumentList 1024
    try {
        [System.IO.File]::WriteAllBytes($KeyFile, $rsa.ExportCspBlob($true))
    }
    finally {
        $rsa.Dispose()
    }

    Write-Warning "Created strong name key '$KeyFile'. Keep it: the PublicKeyToken derived from it is registered in MIM, a new key requires re-running the deployment."
}

# 3. Build
$properties = @("-p:Configuration=$Configuration", "-p:MimReferencePath=$MimReferencePath", "-p:AssemblyOriginatorKeyFile=$KeyFile")
$dotnet = Get-Command -Name 'dotnet' -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
if ($dotnet) {
    & $dotnet.Path build $project --nologo @properties
}
else {
    & (Find-MSBuild) $project -restore -nologo -verbosity:minimal @properties
}

if ($LASTEXITCODE -ne 0) {
    throw "Build failed with exit code $LASTEXITCODE."
}

$output = Join-Path -Path $repositoryRoot -ChildPath "src\MIM.ActorInheritance.Workflow\bin\$Configuration\MIM.ActorInheritance.Workflow.dll"
Write-Host "Built $([System.Reflection.AssemblyName]::GetAssemblyName($output).FullName)"
Write-Host "      $output"
