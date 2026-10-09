<#
.SYNOPSIS
    Catches identifiers this project has retired — renamed, or a rejected design —
    reappearing outside the documents whose job is to explain the retirement.

.DESCRIPTION
    Why this exists: ADR 0006 retired the single-underscore env prefix, but the old
    name survived in two Answer sections for days. Nothing checked. A reader cannot
    tell a stale name from a live one.

    Scope is deliberately narrow. It checks identifiers with ONE canonical spelling,
    NOT the GLOSSARY's `_Avoid_` lists — those are semantic guidance, and a
    banned-word grep against them is ~0 signal / heavy noise (`MySqlConnector`
    contains "Connector"; `Driver` is itself a defined term; "engine" is ordinary
    domain vocabulary).

.EXAMPLE
    pwsh -File tools/check-retired-identifiers.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

$script:violationCount = 0

$extensions = @('.md', '.cs', '.csproj', '.json', '.slnx')

function Test-RetiredIdentifier {
    param(
        [Parameter(Mandatory = $true)] [string]   $Retired,
        [Parameter(Mandatory = $true)] [string]   $Canonical,
        [string[]] $Allow = @()
    )

    $files = Get-ChildItem -Path $repoRoot -Recurse -File |
        Where-Object {
            $extensions -contains $_.Extension -and
            $_.FullName -notmatch '[\\/](\.git|bin|obj)[\\/]' -and
            $_.Name -ne 'check-retired-identifiers.ps1'
        }

    foreach ($file in $files) {
        $hits = Select-String -Path $file.FullName -Pattern $Retired -SimpleMatch -ErrorAction SilentlyContinue
        if (-not $hits) { continue }

        $relative = $file.FullName.Substring($repoRoot.Length).TrimStart('\', '/') -replace '\\', '/'

        foreach ($hit in $hits) {
            $allowed = $false
            foreach ($entry in $Allow) {
                $parts = $entry -split '\|', 2
                if ($parts.Count -eq 2 -and $parts[0] -eq $relative -and $parts[1] -eq $Retired) {
                    $allowed = $true
                    break
                }
            }

            if (-not $allowed) {
                Write-Host ("  {0}:{1}" -f $relative, $hit.LineNumber)
                Write-Host ("    -> {0} is retired; use {1}" -f $Retired, $Canonical)
                $script:violationCount++
            }
        }
    }
}

Write-Host 'Checking retired identifiers...'

# --- Env-var prefix: ADR 0006 settled on the double underscore. ---------------
# `__` is the .NET configuration hierarchy separator; the single-underscore form
# is a DIFFERENT variable that SILENTLY fails to bind. Every allowlisted path
# below is a document whose job is to explain that very contrast.
Test-RetiredIdentifier -Retired 'SHARPDB_TYPE' -Canonical 'SHARPDB__TYPE' -Allow @(
    'docs/adr/0006-double-underscore-env-prefix.md|SHARPDB_TYPE',
    'docs/research/mcp-sdk-mechanics.md|SHARPDB_TYPE',
    '.scratch/sharpdb-mcp/issues/06-config-startup-and-distribution.md|SHARPDB_TYPE',
    '.scratch/sharpdb-v1/spec.md|SHARPDB_TYPE'
)

Test-RetiredIdentifier -Retired 'SHARPDB_CONNECTION_STRING' -Canonical 'SHARPDB__CONNECTIONSTRING' -Allow @(
    '.scratch/sharpdb-mcp/issues/06-config-startup-and-distribution.md|SHARPDB_CONNECTION_STRING'
)

# --- Rejected interface shape: ADR 0003 chose one deep interface. ------------
# `DbProviderFactory` was never adopted; Core talks to a plain DbConnection.
Test-RetiredIdentifier -Retired 'DbProviderFactory' -Canonical 'a plain DbConnection (ADR 0003)'

if ($script:violationCount -eq 0) {
    Write-Host '  OK — no retired identifiers outside their explanation sites.'
    exit 0
}

Write-Host ("  {0} violation(s)." -f $script:violationCount)
exit 1
