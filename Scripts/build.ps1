#!/usr/bin/env pwsh

<#
.SYNOPSIS
    Build and package cascaler for multiple platforms.

.DESCRIPTION
    This script builds the cascaler project for Linux, Windows, and macOS platforms,
    then packages each build into platform-specific archives.

.EXAMPLE
    .\build.ps1
#>

[CmdletBinding()]
param()

# Strict mode - catch errors early
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

# Configuration
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectRoot = Split-Path -Parent $ScriptDir
$ProjectFile = "cascaler.csproj"
$PublishDir = "publish"
$ArchiveDir = "archives"
$BuildConfig = "Release"

# Platforms to build
$Platforms = @("linux-x64", "win-x64", "osx-x64", "osx-arm64")

# Logging functions
function Write-Info {
    param([string]$Message)
    Write-Host "[INFO] $Message" -ForegroundColor Green
}

function Write-Warn {
    param([string]$Message)
    Write-Host "[WARN] $Message" -ForegroundColor Yellow
}

function Write-Error-Message {
    param([string]$Message)
    Write-Host "[ERROR] $Message" -ForegroundColor Red
}

# Check dependencies
function Test-Dependencies {
    Write-Info "Checking dependencies..."

    $missingDeps = @()

    # Check for dotnet
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        $missingDeps += "dotnet"
    }

    # Check for tar (needed for Linux archives)
    if (-not (Get-Command tar -ErrorAction SilentlyContinue)) {
        Write-Warn "tar command not found - will use zip for Linux instead"
    }

    if ($missingDeps.Count -gt 0) {
        Write-Error-Message "Missing required dependencies: $($missingDeps -join ', ')"
        Write-Error-Message "Please install the missing tools and try again."
        exit 1
    }

    Write-Info "All required dependencies found."
}

# Verify project file exists
function Test-ProjectFile {
    Write-Info "Verifying project file..."

    $projectPath = Join-Path $ProjectRoot $ProjectFile

    if (-not (Test-Path $projectPath)) {
        Write-Error-Message "Project file not found: $projectPath"
        exit 1
    }

    Write-Info "Project file found."
}

# Clean previous build artifacts
function Remove-Artifacts {
    Write-Info "Cleaning previous build artifacts..."

    $publishPath = Join-Path $ProjectRoot $PublishDir
    $archivePath = Join-Path $ProjectRoot $ArchiveDir

    if (Test-Path $publishPath) {
        Remove-Item -Path $publishPath -Recurse -Force
        Write-Info "Removed previous publish directory."
    }

    if (Test-Path $archivePath) {
        Remove-Item -Path $archivePath -Recurse -Force
        Write-Info "Removed previous archives directory."
    }
}

# Build for all platforms
function Build-AllPlatforms {
    Write-Info "Building for all platforms..."

    Push-Location $ProjectRoot

    try {
        foreach ($platform in $Platforms) {
            Write-Info "Building for $platform..."

            $outputPath = Join-Path $PublishDir $platform

            $buildArgs = @(
                "publish"
                $ProjectFile
                "-c", $BuildConfig
                "-r", $platform
                "-o", $outputPath
                "--self-contained", "true"
                "-p:PublishSingleFile=true"
                "-p:IncludeNativeLibrariesForSelfExtract=true"
            )

            & dotnet $buildArgs

            if ($LASTEXITCODE -ne 0) {
                throw "Failed to build for $platform"
            }

            Write-Info "Successfully built for $platform."
        }
    }
    finally {
        Pop-Location
    }
}

# Create archives
function New-Archives {
    Write-Info "Creating archives..."

    $publishPath = Join-Path $ProjectRoot $PublishDir
    $archivePath = Join-Path $ProjectRoot $ArchiveDir

    # Create archives directory
    New-Item -Path $archivePath -ItemType Directory -Force | Out-Null

    Push-Location $publishPath

    try {
        # Check if tar is available for Linux archive
        $hasTar = Get-Command tar -ErrorAction SilentlyContinue

        # Create Linux archive (prefer tar.gz, fallback to zip)
        Write-Info "Creating archive for linux-x64..."
        $linuxArchive = Join-Path $archivePath "linux-x64"

        if ($hasTar) {
            # Use tar for better Linux compatibility (preserves permissions)
            & tar -czf "$linuxArchive.tar.gz" -C . "linux-x64"
            if ($LASTEXITCODE -ne 0) {
                throw "Failed to create linux-x64 archive"
            }
        }
        else {
            # Fallback to zip
            $linuxPath = Join-Path $publishPath "linux-x64"
            Compress-Archive -Path $linuxPath -DestinationPath "$linuxArchive.zip" -Force
        }

        # Create zip files for Windows and macOS
        foreach ($platform in @("win-x64", "osx-x64", "osx-arm64")) {
            Write-Info "Creating archive for $platform..."

            $platformPath = Join-Path $publishPath $platform
            $archiveFile = Join-Path $archivePath "$platform.zip"

            Compress-Archive -Path $platformPath -DestinationPath $archiveFile -Force
        }
    }
    finally {
        Pop-Location
    }
}

# Display build summary
function Show-Summary {
    Write-Info "Build completed successfully!"
    Write-Host ""
    Write-Host "Build artifacts:"
    Write-Host "  Binaries: $PublishDir/"
    Write-Host "  Archives: $ArchiveDir/"
    Write-Host ""
    Write-Host "Archive files:"

    $archivePath = Join-Path $ProjectRoot $ArchiveDir
    Get-ChildItem -Path $archivePath | ForEach-Object {
        $sizeKB = [math]::Round($_.Length / 1KB, 2)
        $sizeMB = [math]::Round($_.Length / 1MB, 2)

        if ($sizeMB -gt 1) {
            Write-Host "  $($_.Name) - $sizeMB MB"
        }
        else {
            Write-Host "  $($_.Name) - $sizeKB KB"
        }
    }
}

# Main execution
function Main {
    Write-Info "Starting cascaler multi-platform build..."
    Write-Info "Project root: $ProjectRoot"
    Write-Host ""

    try {
        Test-Dependencies
        Test-ProjectFile
        Remove-Artifacts
        Build-AllPlatforms
        New-Archives
        Show-Summary

        Write-Info "All done!"
    }
    catch {
        Write-Error-Message "Build failed: $_"
        Write-Error-Message $_.ScriptStackTrace
        exit 1
    }
}

# Run main function
Main
