#Requires -Version 5.1
<#
.SYNOPSIS
    Tears down the AWSRedrive integration test stack.

.DESCRIPTION
    Removes this stack's containers, its volumes and the images built for it.
    Scoped to the awsredrive-it compose project - nothing else on the machine is
    touched unless -All is given.

.PARAMETER All
    Additionally run `docker system prune -f`, which removes unused Docker data
    machine-wide including the build cache. Asks for confirmation first.

.EXAMPLE
    ./stop.ps1

.EXAMPLE
    ./stop.ps1 -All
#>
[CmdletBinding()]
param(
    [switch]$All
)

# Deliberately NOT 'Stop' - see the note in start.ps1. `docker compose down`
# writes its progress display to stderr, which Windows PowerShell would turn
# into a terminating NativeCommandError.
$ErrorActionPreference = 'Continue'
Set-Location -Path $PSScriptRoot

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    Write-Host "docker was not found on PATH." -ForegroundColor Red
    exit 1
}

$env:COMPOSE_PROJECT_NAME = 'awsredrive-it'

Write-Host "Tearing down the AWSRedrive integration stack..." -ForegroundColor Cyan
& docker compose down -v --rmi local --remove-orphans

if ($All) {
    Write-Host ""
    Write-Host "-All prunes ALL unused Docker data on this machine, not just this" -ForegroundColor Yellow
    Write-Host "stack: stopped containers, unused networks, dangling images and the" -ForegroundColor Yellow
    Write-Host "build cache." -ForegroundColor Yellow
    $answer = Read-Host "Continue? (y/N)"
    if ($answer -eq 'y' -or $answer -eq 'Y') {
        & docker system prune -f
    }
    else {
        Write-Host "Skipped the system prune." -ForegroundColor Cyan
    }
}

Write-Host ""
Write-Host "Done." -ForegroundColor Green
