#!/usr/bin/env pwsh
# Fixes INSERT INTO statements to include explicit column lists.
# Extracts column names from the CREATE TABLE statement in the same file.

param(
    [switch]$DryRun,
    [string]$Filter = "*"
)

$testDir = Join-Path $PSScriptRoot "..\tests\InMemoryEmulator.BigQuery.Tests.Integration"
$files = Get-ChildItem "$testDir\$Filter.cs" | Where-Object {
    Select-String -Path $_.FullName -Pattern "INSERT INTO.*VALUES" -Quiet
}

$totalFixed = 0
$totalFiles = 0

foreach ($file in $files) {
    $content = Get-Content $file.FullName -Raw
    $originalContent = $content
    
    # Extract table definitions: table name -> column names
    # Pattern: CREATE TABLE `{var}.tableName` (col1 TYPE, col2 TYPE, ...)
    # Also handle: CREATE TABLE `{var}.tableName` (col1 TYPE, col2 STRUCT<...>, ...)
    $tableColumns = @{}
    
    # Match CREATE TABLE with balanced parentheses for column defs
    $createMatches = [regex]::Matches($content, 'CREATE\s+(?:OR\s+REPLACE\s+)?(?:TEMP\s+)?TABLE\s+(?:IF\s+NOT\s+EXISTS\s+)?`[^`]+\.(\w+)`\s*\((.+?)(?:\)\s*(?:OPTIONS|PARTITION|CLUSTER|AS\s|;|"|\$|$))', [System.Text.RegularExpressions.RegexOptions]::Singleline)
    
    foreach ($m in $createMatches) {
        $tableName = $m.Groups[1].Value
        $colDefs = $m.Groups[2].Value
        
        # Parse column names from definitions, handling nested types like STRUCT<...>, ARRAY<...>
        $cols = @()
        $depth = 0
        $current = ""
        foreach ($char in $colDefs.ToCharArray()) {
            if ($char -eq '<' -or $char -eq '(') { $depth++ }
            elseif ($char -eq '>' -or $char -eq ')') { $depth-- }
            elseif ($char -eq ',' -and $depth -eq 0) {
                $trimmed = $current.Trim()
                if ($trimmed -match '^\s*(\w+)\s+') {
                    $cols += $Matches[1]
                }
                $current = ""
                continue
            }
            $current += $char
        }
        # Last column
        $trimmed = $current.Trim()
        if ($trimmed -match '^\s*(\w+)\s+') {
            $cols += $Matches[1]
        }
        
        if ($cols.Count -gt 0) {
            $tableColumns[$tableName] = $cols
        }
    }
    
    if ($tableColumns.Count -eq 0) { continue }
    
    # Fix INSERT INTO statements without column lists
    # Pattern: INSERT INTO `{var}.tableName` VALUES  (no column list between table name and VALUES)
    $fixCount = 0
    $content = [regex]::Replace($content, '(INSERT\s+INTO\s+`[^`]+\.(\w+)`)\s+(VALUES)', {
        param($match)
        $tableName = $match.Groups[2].Value
        if ($tableColumns.ContainsKey($tableName)) {
            $cols = $tableColumns[$tableName] -join ', '
            $fixCount++
            return "$($match.Groups[1].Value) ($cols) $($match.Groups[3].Value)"
        }
        return $match.Value
    })
    
    # Count actual replacements
    if ($content -ne $originalContent) {
        $diffLines = Compare-Object ($originalContent -split "`n") ($content -split "`n")
        $actualFixes = ($diffLines | Where-Object { $_.SideIndicator -eq '=>' }).Count
        $totalFixed += $actualFixes
        $totalFiles++
        
        if ($DryRun) {
            Write-Host "$($file.Name): $actualFixes fix(es)" -ForegroundColor Yellow
            # Show first diff
            $diffLines | Select-Object -First 2 | ForEach-Object {
                Write-Host "  $($_.SideIndicator) $($_.InputObject.Trim())" -ForegroundColor Gray
            }
        } else {
            Set-Content $file.FullName $content -NoNewline
            Write-Host "$($file.Name): $actualFixes fix(es)" -ForegroundColor Green
        }
    }
}

Write-Host "`nTotal: $totalFixed fixes in $totalFiles files" -ForegroundColor Cyan
if ($DryRun) { Write-Host "(DRY RUN - no files modified)" -ForegroundColor Yellow }
