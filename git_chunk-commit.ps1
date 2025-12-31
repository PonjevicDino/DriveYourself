<#
.SYNOPSIS
    Splits large git changes into chunked commits.
    - AUTOMATICALLY SKIPS faulty batches.
    - AUTOMATICALLY REPAIRS corruption where possible.
#>

param (
    [string]$Prefix = "Bulk Commit",
    [switch]$Execute,
    [switch]$AutoRepair
)

# --- CONFIGURATION ---
$ChunkSizeLimit = 999MB 
$MaxFileSize    = 100MB 
$RemoteName     = "origin"
# ---------------------

$ErrorActionPreference = "Stop"
$Global:ExcludedFiles = @()

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

function Run-GitRepair {
    Write-Host "`n--- STARTING AUTO-REPAIR ---" -ForegroundColor Magenta
    $maxRetries = 5
    $retryCount = 0

    while ($retryCount -lt $maxRetries) {
        Write-Host "Running git gc check ($($retryCount+1)/$maxRetries)..." -ForegroundColor Cyan
        
        $proc = Start-Process git -ArgumentList "gc", "--prune=now" -NoNewWindow -PassThru -RedirectStandardError "gc_error.log" -Wait
        $errorOutput = Get-Content "gc_error.log" -Raw -ErrorAction SilentlyContinue
        
        if ($proc.ExitCode -eq 0) {
            Write-Host "Repository is healthy!" -ForegroundColor Green
            Remove-Item "gc_error.log" -ErrorAction SilentlyContinue
            return
        }

        # Regex to find hash
        $badHash = $null
        if ($errorOutput -match "corrupt loose object '([a-f0-9]+)'") { $badHash = $matches[1] }
        elseif ($errorOutput -match "fatal: object ([a-f0-9]+) cannot be read") { $badHash = $matches[1] }
        elseif ($errorOutput -match "unable to read ([a-f0-9]+)") { $badHash = $matches[1] }

        if ($badHash) {
            $folder = $badHash.Substring(0, 2)
            $file   = $badHash.Substring(2)
            $objPath = ".git/objects/$folder/$file"

            if (Test-Path $objPath) {
                Write-Host "Found corrupt LOOSE object: $badHash" -ForegroundColor Red
                Write-Host "Deleting '$objPath'..." -ForegroundColor Yellow
                Remove-Item $objPath -Force
                $retryCount++
                continue
            } else {
                Write-Host "Object $badHash is PACKED (File not found at $objPath)." -ForegroundColor Red
                Write-Host "Attempting Emergency Repack to isolate corruption..." -ForegroundColor Yellow
                
                $repackProc = Start-Process git -ArgumentList "repack", "-a", "-d", "--window=0", "--depth=1" -NoNewWindow -PassThru -Wait
                
                if ($repackProc.ExitCode -eq 0) {
                    Write-Host "Repack successful. Retrying GC..." -ForegroundColor Green
                    $retryCount++
                    continue
                } else {
                    Write-Error "Repack failed. The repository database is severely damaged."
                    break
                }
            }
        }
        
        # If we get here, it's an unknown error, stop trying to repair to avoid infinite loop
        break 
    }
    Remove-Item "gc_error.log" -ErrorAction SilentlyContinue
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
        if ($attrValue -ne "lfs") { $nonLfsFiles += $fileName }
    }
    return $nonLfsFiles
}

function Extract-FileFromError {
    param ([string]$ErrorText)
    if ($ErrorText -match "for '([^']+)'") { return $matches[1] }
    if ($ErrorText -match "pathspec '([^']+)'") { return $matches[1] }
    if ($ErrorText -match "unable to read .+ '([^']+)'") { return $matches[1] }
    return $null
}

function Main {
    if (-not (Test-Path ".git")) {
        Write-Error "Must be run from root of Git repo."
        exit 1
    }

    if ($AutoRepair) { Run-GitRepair }

    $allFiles = Get-GitFiles
    if (-not $allFiles) {
        Write-Host "No changes found." -ForegroundColor Green
        if (-not $Execute) { Read-Host "Press Enter..."; exit 0 }
        exit 0
    }
    
    Write-Host "Found $( $allFiles.Count ) files changed/untracked."
    $filesToCommit = Get-NonLfsFiles -FileList $allFiles
    
    # --- BATCH CALCULATION ---
    Write-Host "Calculating batches..." -ForegroundColor Cyan
    $batches = @()
    $currentBatch = @()
    $currentBatchSize = 0
    
    foreach ($file in $filesToCommit) {
        if (Test-Path -LiteralPath $file) {
            try { $fSize = (Get-Item -LiteralPath $file).Length } catch { $fSize = 0 }
        } else { $fSize = 0 }

        if ($fSize -gt $MaxFileSize) {
            Write-Host "Skipping '$file' (>100MB)." -ForegroundColor Yellow
            continue
        }

        if (($currentBatchSize + $fSize) -gt $ChunkSizeLimit) {
            $batches += ,$currentBatch
            $currentBatch = @()
            $currentBatchSize = 0
        }
        $currentBatch += $file
        $currentBatchSize += $fSize
    }
    if ($currentBatch.Count -gt 0) { $batches += ,$currentBatch }
    
    $totalBatches = $batches.Count
    Write-Host "`n--- Summary ---" -ForegroundColor White
    Write-Host "Total Batches: $totalBatches"
    
    if (-not $Execute) {
        Write-Host "`n*** DRY RUN MODE ***" -ForegroundColor Magenta
        Write-Host "Run with -Execute to commit."
        Read-Host "Press Enter to exit..."
        exit 0
    }

    $confirm = Read-Host "Ready to COMMIT? (y/n)"
    if ($confirm -ne "y") { exit 0 }

    $i = 1
    foreach ($batch in $batches) {
        $retryBatch = $true
        $currentBatchFiles = $batch 
        $batchAttempts = 0
        
        while ($retryBatch) {
            $retryBatch = $false
            $batchAttempts++
            
            # --- INFINITE LOOP PROTECTION ---
            if ($batchAttempts -gt 3) {
                Write-Host "Batch $i failed 3 times (persistent error). SKIPPING BATCH AUTOMATICALLY." -ForegroundColor Yellow
                break # Move to next batch
            }

            $activeFiles = $currentBatchFiles | Where-Object { 
                $f = $_
                $isExcluded = $false
                foreach ($ex in $Global:ExcludedFiles) { if ($f -like "*$ex*") { $isExcluded = $true; break } }
                -not $isExcluded
            }
            
            if ($activeFiles.Count -eq 0) {
                Write-Host "Batch $i empty (all files skipped). Moving on." -ForegroundColor Yellow
                break
            }

            $commitMsg = "$Prefix {0:d3}/{1:d3}" -f $i, $totalBatches
            Write-Host "`nProcessing Batch $i/$totalBatches ($( $activeFiles.Count ) files)..." -ForegroundColor Cyan
            
            $tempFile = [System.IO.Path]::GetTempFileName()
            
            try {
                Start-Process git -ArgumentList "reset" -NoNewWindow -Wait
                $content = $activeFiles | ForEach-Object { $_.Replace('\', '/') }
                $utf8NoBom = New-Object System.Text.UTF8Encoding $false
                [System.IO.File]::WriteAllLines($tempFile, $content, $utf8NoBom)

                $output = & git add --pathspec-from-file=$tempFile 2>&1
                if ($LASTEXITCODE -ne 0) { throw $output }

                Write-Host "Committing..."
                $output = & git commit -m "$commitMsg" 2>&1
                
                if ($LASTEXITCODE -ne 0) {
                    if ("$output" -match "no changes added" -or "$output" -match "nothing to commit") {
                        Write-Host "Nothing to commit in this batch." -ForegroundColor Green
                    } else { throw $output }
                } else {
                    Write-Host "Pushing..."
                    $currentBranch = git rev-parse --abbrev-ref HEAD
                    $pushOut = & git push $RemoteName $currentBranch 2>&1
                    
                    if ($LASTEXITCODE -ne 0) { throw $pushOut }
                    Write-Host "Batch $i success." -ForegroundColor Green
                }
            }
            catch {
                $errString = "$_"
                Write-Host "`n!!! BATCH FAILED !!!" -ForegroundColor Red
                
                if ($errString -match "inflate: data stream error" -or $errString -match "unable to read") {
                    Write-Host "CORRUPTION DETECTED. Auto-Repairing..." -ForegroundColor Yellow
                    Run-GitRepair
                    $retryBatch = $true
                    continue
                }

                $badFile = Extract-FileFromError -ErrorText $errString
                
                # --- AUTO-SKIP LOGIC ---
                if (-not $badFile) {
                    Write-Host "Could not identify specific file. Error likely generic or network related." -ForegroundColor Yellow
                    Write-Host "SKIPPING BATCH $i AUTOMATICALLY." -ForegroundColor Yellow
                    break # Break the while loop to move to the next batch
                }

                if ($badFile) {
                    $cleanBadFile = Clean-PathString -Path $badFile
                    Write-Host "Excluding bad file: '$cleanBadFile'" -ForegroundColor Magenta
                    $Global:ExcludedFiles += $cleanBadFile
                    # Reset attempt counter because we modified the batch, so it's a "new" attempt
                    $batchAttempts = 0 
                    $retryBatch = $true 
                }
            }
            finally { Remove-Item $tempFile -ErrorAction SilentlyContinue }
        }
        $i++
    }
    
    if ($Global:ExcludedFiles.Count -gt 0) {
        Write-Host "`n--- SKIPPED FILES ---" -ForegroundColor Yellow
        $Global:ExcludedFiles | ForEach-Object { Write-Host $_ }
    }
    Read-Host "Done. Press Enter..."
}

Main