#!/usr/bin/env pwsh
#
# -----------------------------------------------------------------------------
#
# This file is a part of Clyde.NET project.
# 
# Copyright 2019-2020 Emzi0767
# 
# Licensed under the Apache License, Version 2.0 (the "License");
# you may not use this file except in compliance with the License.
# You may obtain a copy of the License at
# 
#   http://www.apache.org/licenses/LICENSE-2.0
#   
# Unless required by applicable law or agreed to in writing, software
# distributed under the License is distributed on an "AS IS" BASIS,
# WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
# See the License for the specific language governing permissions and
# limitations under the License.
#
# -----------------------------------------------------------------------------
#
# Rebuild-Linux
# 
# A sub-script of Rebuild-Lib, which handles building the library under Linux.
# 
# Author:       Emzi0767
# Version:      2019-01-21T17:39+02:00
# 
# Arguments: 
#  .\build\rebuild-linux.ps1 <output path> [version suffix] [-configuration Debug/Release] [-mono /path/to/mono]
# 
# Run as:
#  .\build\rebuild-linux.ps1 ".\artifacts"
# 
# Or:
#  .\build\rebuild-linux.ps1 ".\artifacts" ci 00420
# 
# Or:
#  .\build\rebuild-linux.ps1 ".\artifacts" -configuration Debug
# 
# Or:
#  .\build\rebuild-linux.ps1 ".\artifacts" -mono /usr/local/bin/mono

param
(
    [parameter(Mandatory = $true)]
    [string] $ArtifactLocation,

    [parameter(Mandatory = $true)]
    [string] $Configuration,

    [parameter(Mandatory = $false)]
    [string] $VersionSuffix,

    [parameter(Mandatory = $false)]
    [string] $BuildNumber,

    [parameter(Mandatory = $false)]
    [string] $Mono
)

# Projects excluded from FX build
$fx_excluded = @()

# Projects excluded from Core build
$fc_excluded = @()

# Projects excluded from any build
$al_excluded = @("Clyde.NET.TestBot")

# Check if configuration is valid
if ($Configuration -ne "Debug" -and $Configuration -ne "Release")
{
    Write-Host "Invalid configuration specified. Must be Release or Debug."
    Exit 1
}

# Gets framework base path
function Get-FxBasePath([string] $mono_location)
{
    # Check if mono location was passed
    $mono = $mono_location
    if ("$mono" -eq "")
    {
        Write-Host "Mono location not specified, attempting autodetection"
        try
        {
            $mono = Get-Command "mono"
            $mono = $mono.Source
        }
        catch
        {
            Write-Host "Mono autodetection failed"
            Return 1
        }
    }

    # Check if we found anything
    if ("$mono" -eq "")
    {
        Write-Host "Mono was not found on this system. Ensure you have mono installed, and that it's in your path. If the path was supplied manually, ensure that it is correct."
        Return 1
    }

    # Check if it's indeed mono, and which version
    try
    {
        $mono_out = & $mono -V
        if ("$mono_out" -eq "")
        {
            Write-Host "Your installation of mono failed to produce any output. Ensure that your mono is installed properly."
            return 1
        }

        $mono_out = ($mono_out -split "\n")[0]
        $mono_out = ($mono_out -split " ")
        if ($mono_out[0] -ne "Mono")
        {
            Write-Host "Your installation of mono failed to produce expected output. Ensure that your mono installation is indeed a mono installation."
            Return 1
        }

        $mono_v = New-Object System.Version($mono_out[4])
        $targ_v = New-Object System.Version("5.0.0")
        if ($mono_v.CompareTo($targ_v) -lt 0)
        {
            Write-Host "Your installation of mono is older than required (5.0.0). Please upgrade your mono to a newer version, then re-try this script."
            Return 1
        }
    }
    catch
    {
        Write-Host "Your installation of mono failed to run ($($_.Exception.Message)). Ensure that your mono is installed properly."
        Return 1
    }

    $mono_pfix = $mono.Substring(0, $mono.IndexOf("/bin/mono"))
    $mono_fxpf = "$mono_pfix/lib/mono"
    Write-Host "Detected FX base: $mono_fxpf"

    return $mono_fxpf
}

# Build project for specified framework
function Build-Project([string] $project, [string] $framework, [string] $configuration, [string] $mono, [string] $version_suffix, [string] $build_number)
{
    # base arguments for dotnet
    $dotnet_args = ""

    # check if FX target
    if ($framework.StartsWith("net4"))
    {
        # yes, check if the project qualifies
        if ($fx_excluded.Contains($project) -or $al_excluded.Contains($project))
        {
            Write-Host "Skipping building $project for framework $framework"
            Return 0
        }

        # yes, add the mono override
        # construct the version string
        $fxv = (($framework.Substring(3) -split "") -join ".")
        $fxv = $fxv.Substring(1, $fxv.Length - 2)

        # construct the args
        $dotnet_args = "-p:FrameworkPathOverride=`"$mono/$fxv-api`""
    }
    elseif ($framework.StartsWith("netstandard"))
    {
        # no, check if the project qualifies for core build
        if ($fc_excluded.Contains($project) -or $al_excluded.Contains($project))
        {
            Write-Host "Skipping building $project for framework $framework"
            Return 0
        }
    }
    else 
    {
        # netcoreapp project
        if (-not $al_excluded.Contains($project))
        {
            Write-Host "Skipping building $project for framework $framework"
            Return 0
        }
    }

    # invoke dotnet
    if (-not $version_suffix -or -not $build_number)
    {
        & dotnet build -v minimal -c $configuration -f $framework $dotnet_args | Out-Host
    }
    else 
    {
        & dotnet build -v minimal -c $configuration -f $framework --version-suffix "$version_suffix" -p:BuildNumber="$build_number" $dotnet_args | Out-Host
    }
    if ($LastExitCode -ne 0)
    {
        Write-Host "Build failed"
        Return $LastExitCode
    }

    Return 0
}

# detect mono
$mono_path = Get-FxBasePath $Mono
if ($mono_path -eq 1)
{
    Write-Host "Detecting mono installation failed. Ensure that mono is installed, and is in your PATH, or pass the location of your mono binary via -Mono argument."
    Exit 1
}

# find projects
$projects = Get-ChildItem "src" | ? { $_.Name.StartsWith("Clyde.NET") -and ($_.Attributes -band [System.IO.FileAttributes]::Directory) -eq [System.IO.FileAttributes]::Directory } | Sort-Object
$frameworks = "net45","net46","net47","netstandard1.1","netstandard1.3","netstandard2.0","netcoreapp2.0","netcoreapp2.1","netcoreapp2.2"
$cloc = Get-Location
foreach ($project in $projects)
{
    $pname = $project.Name
    Set-Location $project
    Write-Host "Building project: $pname"

    foreach ($fx in $frameworks)
    {
        $build_result = Build-Project "$pname" "$fx" "$Configuration" "$mono_path" "$VersionSuffix"

        if ($build_result -ne 0)
        {
            Set-Location $cloc
            Write-Host "Building $pname for $fx failed"
            Exit $build_result
        }
    }

    Set-Location $cloc
    Write-Host "Building $pname completed"
}

Write-Host "Build succeeded"
# check if debug
if ($Configuration -eq "Debug")
{
    # exit, we don't package in debug
    Exit 0
}

# construct the framework string array
$pack_fxs = @()
foreach ($fx in $frameworks)
{
    if (-not $fx.StartsWith("net4"))
    {
        # not an FX
        continue
    }

    $fxv = (($fx.Substring(3) -split "") -join ".")
    $fxv = $fxv.Substring(1, $fxv.Length - 2)
    $pack_fxs += "-p:FrameworkPathOverride=`"$mono_path/$fxv-api`""
}

Write-Host "Packaging"
# package all projects
foreach ($project in $projects)
{
    $pname = $project.Name

    if ($al_excluded.Contains($pname))
    {
        # we do not package all-excluded projects
        continue
    }

    Set-Location $project
    Write-Host "Packaging project: $pname"

    if (-not $VersionSuffix -or -not $BuildNumber)
    {
        & dotnet pack -v minimal -c "$bcfg" --no-build -o "$ArtifactLocation" --include-symbols $pack_fxs | Out-Host
    }
    else
    {
        & dotnet pack -v minimal -c "$bcfg" --version-suffix "$VersionSuffix" -p:BuildNumber="$BuildNumber" --no-build -o "$ArtifactLocations" --include-symbols $pack_fxs | Out-Host
    }
    if ($LastExitCode -ne 0)
    {
        Set-Location $cloc
        Write-Host "Packaging failed"
        Return $LastExitCode
    }

    Set-Location $cloc
    Write-Host "Packaging $pname completed"
}

Exit
