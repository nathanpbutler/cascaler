#!/usr/bin/env bash

# Exit on error, undefined variables, and pipe failures
set -euo pipefail

# Configuration
readonly SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
readonly PROJECT_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
readonly PROJECT_FILE="cascaler.csproj"
readonly PUBLISH_DIR="publish"
readonly ARCHIVE_DIR="archives"
readonly BUILD_CONFIG="Release"

# Color output
readonly RED='\033[0;31m'
readonly GREEN='\033[0;32m'
readonly YELLOW='\033[1;33m'
readonly NC='\033[0m' # No Color

# Platforms to build
readonly PLATFORMS=("linux-x64" "win-x64" "osx-x64" "osx-arm64")

# Logging functions
log_info() {
    echo -e "${GREEN}[INFO]${NC} $*"
}

log_warn() {
    echo -e "${YELLOW}[WARN]${NC} $*"
}

log_error() {
    echo -e "${RED}[ERROR]${NC} $*" >&2
}

# Cleanup function for error handling
cleanup_on_error() {
    log_error "Build failed. Cleaning up partial artifacts..."
    exit 1
}

trap cleanup_on_error ERR

# Check dependencies
check_dependencies() {
    log_info "Checking dependencies..."

    local missing_deps=()

    if ! command -v dotnet &> /dev/null; then
        missing_deps+=("dotnet")
    fi

    if ! command -v tar &> /dev/null; then
        missing_deps+=("tar")
    fi

    if ! command -v zip &> /dev/null; then
        missing_deps+=("zip")
    fi

    if [ ${#missing_deps[@]} -ne 0 ]; then
        log_error "Missing required dependencies: ${missing_deps[*]}"
        log_error "Please install the missing tools and try again."
        exit 1
    fi

    log_info "All dependencies found."
}

# Verify project file exists
verify_project() {
    log_info "Verifying project file..."

    if [ ! -f "${PROJECT_ROOT}/${PROJECT_FILE}" ]; then
        log_error "Project file not found: ${PROJECT_ROOT}/${PROJECT_FILE}"
        exit 1
    fi

    log_info "Project file found."
}

# Clean previous build artifacts
clean_artifacts() {
    log_info "Cleaning previous build artifacts..."

    if [ -d "${PROJECT_ROOT}/${PUBLISH_DIR}" ]; then
        rm -rf "${PROJECT_ROOT}/${PUBLISH_DIR}"
        log_info "Removed previous publish directory."
    fi

    if [ -d "${PROJECT_ROOT}/${ARCHIVE_DIR}" ]; then
        rm -rf "${PROJECT_ROOT}/${ARCHIVE_DIR}"
        log_info "Removed previous archives directory."
    fi
}

# Build for all platforms
build_all_platforms() {
    log_info "Building for all platforms..."

    cd "${PROJECT_ROOT}"

    for platform in "${PLATFORMS[@]}"; do
        log_info "Building for ${platform}..."

        if ! dotnet publish "${PROJECT_FILE}" \
            -c "${BUILD_CONFIG}" \
            -r "${platform}" \
            -o "${PUBLISH_DIR}/${platform}" \
            --self-contained true \
            -p:PublishSingleFile=true \
            -p:IncludeNativeLibrariesForSelfExtract=true; then
            log_error "Failed to build for ${platform}"
            exit 1
        fi

        log_info "Successfully built for ${platform}."
    done
}

# Create archives
create_archives() {
    log_info "Creating archives..."

    mkdir -p "${PROJECT_ROOT}/${ARCHIVE_DIR}"
    cd "${PROJECT_ROOT}/${PUBLISH_DIR}"

    # Create tar.gz for Linux (preserves file permissions)
    log_info "Creating archive for linux-x64..."
    if ! tar -czf "${PROJECT_ROOT}/${ARCHIVE_DIR}/linux-x64.tar.gz" -C . linux-x64; then
        log_error "Failed to create linux-x64 archive"
        exit 1
    fi

    # Create zip files for Windows and macOS
    for platform in "win-x64" "osx-x64" "osx-arm64"; do
        log_info "Creating archive for ${platform}..."
        if ! zip -r -q "${PROJECT_ROOT}/${ARCHIVE_DIR}/${platform}.zip" "${platform}"; then
            log_error "Failed to create ${platform} archive"
            exit 1
        fi
    done

    cd "${PROJECT_ROOT}"
}

# Display build summary
show_summary() {
    log_info "Build completed successfully!"
    echo ""
    echo "Build artifacts:"
    echo "  Binaries: ${PUBLISH_DIR}/"
    echo "  Archives: ${ARCHIVE_DIR}/"
    echo ""
    echo "Archive files:"
    ls -lh "${ARCHIVE_DIR}/"
}

# Main execution
main() {
    log_info "Starting cascaler multi-platform build..."
    log_info "Project root: ${PROJECT_ROOT}"
    echo ""

    check_dependencies
    verify_project
    clean_artifacts
    build_all_platforms
    create_archives
    show_summary

    log_info "All done!"
}

# Run main function
main "$@"