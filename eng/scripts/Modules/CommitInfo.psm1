Set-StrictMode -Version Latest

$script:commitDetails = $null
$script:artifactCommitDetails = "##artifactCommitDetails##"

function Initialize-CommitInfo {
    if ($script:artifactCommitDetails -notmatch "artifactCommitDetails") {
        $script:commitDetails = $script:artifactCommitDetails
    } else {
        $script:commitDetails = "unknown"
        $hasGit = $null -ne (Get-Command git -ErrorAction SilentlyContinue)
        if ($hasGit) {
            # We can't call Write-Command here as it would create a circular reference
            Write-Host "##[command]git -C $PSScriptRoot rev-parse --is-inside-work-tree" -ForegroundColor DarkCyan -BackgroundColor Black
            $isGitRepo = git -C $PSScriptRoot rev-parse --is-inside-work-tree *>&1
            if ($isGitRepo -eq $true) {
                Write-Host "##[command]git -C $PSScriptRoot rev-parse --show-toplevel" -ForegroundColor DarkCyan -BackgroundColor Black
                $root = git -C $PSScriptRoot rev-parse --show-toplevel
    
                if ($env:TF_Build -ne "true") {
                    Write-Host "##[command]git -C $root rev-parse --abbrev-ref HEAD" -ForegroundColor DarkCyan -BackgroundColor Black
                    $branch = git -C $root rev-parse --abbrev-ref HEAD
                } else {
                    # The above command will return HEAD on the pipeline
                    $branch = $env:Build_SourceBranch -replace "refs/heads/",""
                }
    
                Write-Host "##[command]git -C $root rev-parse --short HEAD" -ForegroundColor DarkCyan -BackgroundColor Black
                $commitHash = git -C $root rev-parse --short HEAD
    
                Write-Host "##[command]git -C $root show -s --format=%ci HEAD" -ForegroundColor DarkCyan -BackgroundColor Black
                $commitDate = [System.DateTime]::Parse((git -C $root show -s --format=%ci HEAD))
    
                $script:commitDetails = "$($commitDate.Year).$($commitDate.Month.ToString().PadLeft(2,'0')).$($commitDate.Day.ToString().PadLeft(2,'0'))-$commitHash-$branch"
            }
        }
    }
    
    $frame = (Get-PSCallStack)[0]
    $details = @(
        $frame.Position.File,
        ($frame.Position.StartLineNumber + 6),
        $frame.Position.StartColumnNumber
    ) -join ":"
    Write-Host "##[info][$details] Omnichannel version: $script:commitDetails [PS-$($PSVersionTable.PSEdition)-v$($PSVersionTable.PSVersion)] [dotnet$([System.Environment]::Version)]" -ForegroundColor Gray -BackgroundColor Black
    $script:initialized = $true
}
Export-ModuleMember -Function Initialize-CommitInfo

$script:initialized = $false
if ($env:loadingProfile -ne "true") {
    Initialize-CommitInfo
}

# Gets the current commit info
# If running locally, uses the current git commit info
# If running on the pipeline, uses a value generated from the build
function Get-CommitInfo {
    if (-not $script:initialized) {
        Initialize-CommitInfo
    }
    return $script:commitDetails
}
Export-ModuleMember -function Get-CommitInfo