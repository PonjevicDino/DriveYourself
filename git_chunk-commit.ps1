<#
.SYNOPSIS
    Splits large git changes into chunked commits (approx 1GB) and pushes them.
.DESCRIPTION
    DEFAULT MODE IS DRY RUN. 
    Use -Execute to perform actual commits.
#>

param (
    [string]$Prefix = "Bulk Commit",
    [switch]$Execute
)

# --- CONFIGURATION ---
$ChunkSizeLimit = 1GB
$MaxFileSize    = 100MB     # Skip files larger than this
$RemoteName     = "origin"
# ---------------------

$ErrorActionPreference = "Stop"

function Check-GitExitCode {
    param ([string]$CommandName)
    if ($LASTEXITCODE -ne 0) {
        Write-Error "COMMAND FAILED: $CommandName (Exit Code: $LASTEXITCODE)"
        Write-Error "Script aborted to prevent data loss or partial commits."
        exit 1
    }
}

function Clean-PathString {
    param ([string]$Path)
    if (-not $Path) { return "" }
    
    $clean = $Path.Replace('/', '\')
    $clean = $clean -replace '^["'']+|["'']+$', ''
    $clean = $clean -replace '\\r$', ''
    $clean = $clean -replace '\\n$', ''
    $clean = $clean.TrimEnd([char]0x0D).TrimEnd([char]0x0A).TrimEnd([char]0x0D)
    
    return $clean.Trim()
}

function Get-GitFiles {
    Write-Host "Scanning for changed files..." -ForegroundColor Cyan
    $untracked = git ls-files --others --exclude-standard
    $modified = git diff --name-only HEAD
    $allFiles = @($untracked) + @($modified) | Select-Object -Unique
    
    $cleanedList = @()
    foreach ($f in $allFiles) {
        $c = Clean-PathString -Path $f
        if ($c) { $cleanedList += $c }
    }
    return $cleanedList
}

function Get-NonLfsFiles {
    param ($FileList)
    if (-not $FileList) { return @() }
    
    Write-Host "Checking files for Git LFS attributes..." -ForegroundColor Cyan
    
    $queryList = $FileList | ForEach-Object { $_.Replace('\', '/') }
    $lfsOutput = $queryList | git check-attr filter --stdin
    
    $nonLfsFiles = @()
    foreach ($line in $lfsOutput) {
        $parts = $line -split ": ", 3
        if ($parts.Count -lt 3) { continue }
        
        $fileName = Clean-PathString -Path $parts[0]
        $attrValue = Clean-PathString -Path $parts[2]
        
        if ($attrValue -ne "lfs") {
            $nonLfsFiles += $fileName
        }
    }
    return $nonLfsFiles
}

function Main {
    if (-not (Test-Path ".git")) {
        Write-Error "This script must be run from the root of a Git repository."
        exit 1
    }

    $allFiles = Get-GitFiles
    if (-not $allFiles) {
        Write-Host "No changes found to commit." -ForegroundColor Green
        if (-not $Execute) { Read-Host "Press Enter to exit..." }
        exit 0
    }
    
    Write-Host "Found $( $allFiles.Count ) files changed/untracked."

    $filesToCommit = Get-NonLfsFiles -FileList $allFiles
    
    Write-Host "Calculating batches..." -ForegroundColor Cyan
    
    $batches = @()
    $currentBatch = @()
    $currentBatchSize = 0
    $totalSizeBytes = 0
    $skippedHighSizeCount = 0
    
    foreach ($file in $filesToCommit) {
        
        if (Test-Path -LiteralPath $file) {
            try {
                $fItem = Get-Item -LiteralPath $file
                $fSize = $fItem.Length
            }
            catch {
                $fSize = 0
            }
        }
        else { $fSize = 0 }

        if ($fSize -gt $MaxFileSize) {
            Write-Host "Skipping '$file' ($([math]::Round($fSize/1MB, 2)) MB) - Too large." -ForegroundColor Yellow
            $skippedHighSizeCount++
            continue
        }

        if (($currentBatchSize + $fSize) -gt $ChunkSizeLimit) {
            $batches += ,$currentBatch
            $currentBatch = @()
            $currentBatchSize = 0
        }
        
        $currentBatch += $file
        $currentBatchSize += $fSize
        $totalSizeBytes += $fSize
    }
    if ($currentBatch.Count -gt 0) { $batches += ,$currentBatch }
    
    $totalBatches = $batches.Count
    $totalSizeGB = [math]::Round($totalSizeBytes / 1GB, 2)
    
    Write-Host "`n--- Summary ---" -ForegroundColor White
    if ($skippedHighSizeCount -gt 0) {
        Write-Host "Skipped $skippedHighSizeCount files larger than 100MB." -ForegroundColor Yellow
    }
    Write-Host "Total Batch Size: $totalSizeGB GB"
    Write-Host "Total Commits required: $totalBatches"
    Write-Host "Prefix: '$Prefix [n]/$totalBatches'"
    
    if (-not $Execute) {
        Write-Host "`n*** DRY RUN MODE ***" -ForegroundColor Magenta
        Write-Host "No changes will be made."
        Write-Host "To commit these files, run: .\GitChunkCommitter.ps1 -Execute"
    }
    Write-Host "----------------`n"

    if (-not $Execute) {
        Read-Host "Press Enter to exit..."
        exit 0
    }

    $confirm = Read-Host "Ready to COMMIT and PUSH? (y/n)"
    if ($confirm -ne "y") {
        Write-Host "Aborted."
        exit 0
    }

    $i = 1
    foreach ($batch in $batches) {
        $commitMsg = "$Prefix {0:d3}/{1:d3}" -f $i, $totalBatches
        Write-Host "`nProcessing Batch $i/$totalBatches ($( $batch.Count ) files)..." -ForegroundColor Cyan
        
        $tempFile = [System.IO.Path]::GetTempFileName()
        
        try {
            $content = $batch | ForEach-Object { $_.Replace('\', '/') }
            
            [System.IO.File]::WriteAllLines($tempFile, $content)

            git add --pathspec-from-file=$tempFile
            Check-GitExitCode "git add"
            
            Write-Host "Committing: '$commitMsg'"
            git commit -m "$commitMsg"
            Check-GitExitCode "git commit"
            
            Write-Host "Pushing..."
            $currentBranch = git rev-parse --abbrev-ref HEAD
            git push $RemoteName $currentBranch
            Check-GitExitCode "git push"
            
            Write-Host "Batch $i success." -ForegroundColor Green
        }
        catch {
            Write-Error $_
            Remove-Item $tempFile -ErrorAction SilentlyContinue
            exit 1
        }
        finally {
            Remove-Item $tempFile -ErrorAction SilentlyContinue
        }
        $i++
    }

    Write-Host "`nAll chunks processed successfully!" -ForegroundColor Green
    Read-Host "Press Enter to exit..."
}

Main