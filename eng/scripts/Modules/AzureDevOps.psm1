# See https://github.com/microsoft/azure-pipelines-tasks/blob/master/docs/authoring/commands.md
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# See https://stackoverflow.com/questions/9738535/powershell-test-for-noninteractive-mode
# Test each Arg for match of abbreviated '-NonInteractive' command.
$script:interactiveSession = [Environment]::UserInteractive -and (-not ([Environment]::GetCommandLineArgs() | Where-Object{ $_ -like '-NonI*' }))

@("System.IO.Compression", "System.IO.Compression.FileSystem") | ForEach-Object {
    try {
        [Reflection.Assembly]::LoadWithPartialName($_)
    } catch {
    }
}

Import-Module "$PSScriptRoot/CommitInfo.psm1"

# Gets the ado organization from its url
function Get-OrganizationFromAdoUrl {
    [CmdletBinding()]
    param(
        [string]$url,
        [string]$fallback
    )

    if ([string]::IsNullOrWhiteSpace($url)) {
        return $fallback
    }
    if ($url -match "https://([^\.]+)\.visualstudio\.com") {
        return $matches[1]
    }
    if ($url -match "https://(.*\.)?dev.azure.com/([^/]+)") {
        return $matches[2]
    }
    return $fallback
}
Export-ModuleMember -Function Get-OrganizationFromAdoUrl

# See https://learn.microsoft.com/azure/devops/pipelines/release/variables?view=azure-devops&tabs=batch
$script:defaultOrganization = Get-OrganizationFromAdoUrl $env:SYSTEM_TEAMFOUNDATIONCOLLECTIONURI "dynamicscrm"

$script:defaultProjectId = "b276c3e1-2902-46bd-a686-484157b97f48"
if (-not [string]::IsNullOrWhiteSpace($env:Build_ProjectId)) {
    $script:defaultProject = $env:Build_ProjectId
}
$script:defaultProject = "OneCRM"
if (-not [string]::IsNullOrWhiteSpace($env:SYSTEM_TEAMPROJECTID)) {
    $script:defaultProject = $env:SYSTEM_TEAMPROJECTID
}
$script:defaultReleaseId = -1
if (-not [string]::IsNullOrWhiteSpace($env:Release_ReleaseId)) {
    try {
        $script:defaultReleaseId = [int]::Parse($env:Release_ReleaseId)
    } catch {
        # Do nothing
    }
}

# Sets a pipeline variable
function Set-PipelineVariable {
    [CmdletBinding()]
    param(
        # The variable name
        [string]$name,

        # The variable value
        [string]$value,
        
        # Whether the variable should be created as a secret
        [switch]$secret,
        
        # Whether the variable should be accessible in yaml pipelines via $[ dependencies.JOBNAME.outputs['TASKNAME.name'] ]
        # See https://learn.microsoft.com/azure/devops/pipelines/process/variables?view=azure-devops&tabs=yaml%2Cbatch
        [switch]$output,
        
        # Whether the value is safe to log
        [switch]$safeToLog
    )

    if ([string]::IsNullOrWhitespace($name)) { throw "The `$name must not be null or whitespace" }

    if ($psBoundParameters.ContainsKey("isSecret")) {
        $secret = $isSecret
    }
    
    if ($safeToLog) {
        if ($output) {
            Write-Info "Setting pipeline output variable [$name] to [$value]"
        } else {
            Write-Info "Setting pipeline variable [$name] to [$value]"
        }
    } else {
        if ($output) {
            Write-Info "Setting pipeline output variable [$name]"
        } else {
            Write-Info "Setting pipeline variable [$name]"
        }
    }
    if ($env:TF_BUILD -eq "true") {
        if ($secret) {
            if ($output) {
                Write-Host "##vso[task.setvariable variable=$name;issecret=true;isOutput=true]$value"
            }
            Write-Host "##vso[task.setvariable variable=$name;issecret=true]$value"
        } else {
            if ($output) {
                Write-Host "##vso[task.setvariable variable=$name;isOutput=true]$value"
            }
            Write-Host "##vso[task.setvariable variable=$name]$value"
        }
    }
    
    [Environment]::SetEnvironmentVariable($name, $value)
}
Export-ModuleMember -Function Set-PipelineVariable

# Tests whether the specified exception is a [Microsoft.Rest.Azure.CloudException]
function Test-AzureRestException {
    [CmdletBinding()]
    param($exception)

    [array]$initialErrors = $global:error.Clone()
    try {
        return $exception -is [Microsoft.Rest.Azure.CloudException]
    } catch { # This error isn't valuable to keep in $global:error
        $global:error.Clear()
        $global:error.AddRange($initialErrors)
        return $false
    }
}
# Tests whether the specified exception is a [System.Net.WebException]
function Test-WebException {
    [CmdletBinding()]
    param($exception)

    [array]$initialErrors = $global:error.Clone()
    try {
        return $exception -is [System.Net.WebException]
    } catch { # This error isn't valuable to keep in $global:error
        $global:error.Clear()
        $global:error.AddRange($initialErrors)
        return $false
    }
}

<#
.SYNOPSIS
Writes a pipeline issue to Azure DevOps
.EXAMPLE
Write-PipelineIssue -errorMessage "The code failed"
.EXAMPLE
Write-PipelineIssue -message "The code failed" -type "error"
.EXAMPLE
Write-PipelineIssue -warningMessage "The code kind-of failed"
.EXAMPLE
Write-PipelineIssue -message "The code kind-of failed" -type "warning"
.EXAMPLE
try { throw "exception" } catch { Write-PipelineIssue -errorMessage "The code failed" -exception $_ }
.EXAMPLE
Write-PipelineIssue -message "Verbose content" -type "error" -sourcePath "c:/code/myFile.ps1" -lineNumber "13" -columnNumber "37" -code "4" -exception $_
.EXAMPLE
$message = Write-PipelineIssue -errorMessage "The code failed"
.EXAMPLE
throw Write-PipelineIssue -errorMessage "The code failed"
#>
function Write-PipelineIssue
{
    [CmdletBinding(DefaultParameterSetName="Error")]
    param(
        # The error message - simplification of -errorMessage $message
        [Alias("e")]
        [Parameter(Mandatory=$true,Position=0,ParameterSetName="Error")][string] $errorMessage,
        
        # The warning message - simplification of -warningMessage $message
        [Alias("w")]
        [Parameter(Mandatory=$true,Position=0,ParameterSetName="Warning")][string] $warningMessage,

        # The message
        [Parameter(Mandatory=$true,Position=0,ParameterSetName="Type")][string] $message,
        
        # The issue type - either warning or error
        [ValidateSet("error","warning")]
        [Parameter(Mandatory=$false,Position=1,ParameterSetName="Type")][string] $type = "error", 
        
        # The source of the file for which the issue is being logged. Defaults to the source file from the exception or calling stack.
        [Parameter(Mandatory=$false)][string] $sourcePath = $null,
        
        # The line number for which the issue is being logged. Defaults to the line number from the exception or calling stack.
        [Parameter(Mandatory=$false)][int] $lineNumber = -1,
        
        # The column number for which the issue is being logged. Defaults to the column number from the exception or calling stack.
        [Parameter(Mandatory=$false)][int] $columnNumber = -1,
        
        # The error code
        [Parameter(Mandatory=$false)][int] $code = -1,
        
        # The exception which will be logged with the exception
        [Alias("ex")]
        [Parameter(Mandatory=$false)] $exception,

        # Whether to return the message (for throwing)
        [Parameter(Mandatory=$false)][switch] $return,

        [Parameter(Mandatory=$false)][System.Management.Automation.CallStackFrame]$callingFrame
    )

    switch ($PsCmdlet.ParameterSetName) {
        "Error" {
            if ([string]::IsNullOrWhitespace($errorMessage)) { throw "-errorMessage must not be null or whitespace" }
            $message = $errorMessage
            $type = "error"
        }
        "Warning" {
            if ([string]::IsNullOrWhitespace($warningMessage)) { throw "-warning must not be null or whitespace" }
            $message = $warningMessage
            $type = "warning"
        }
    }
    if ([string]::IsNullOrWhitespace($message)) { throw "-message must not be null or whitespace" }
    if ([string]::IsNullOrWhitespace($type)) { throw "-type must not be null or whitespace" }
    
    $callStack = Get-PSCallStack
    if ($null -ne $exception) {
        $exc = if (($exception -is [System.Management.Automation.ErrorRecord]) -and ($null -ne $exception.Exception)) {
            $exception.Exception
        } elseif ($exception -is [System.Exception]) {
            $exception
        }

        if ((Test-WebException $exc) -and ($null -ne $exc.Response)) {
            try {
                $s = $exc.Response.GetResponseStream()
                $content = [System.IO.StreamReader]::new($s).ReadToEnd()
                $s.Position = 0
                $message += "`n| $($exc.Response.Method) $($exc.Response.ResponseUri) => $($exc.Response.StatusCode.value__): $($exc.Response.StatusDescription)"
                if (-not $message.Contains($content)) {
                    $message += "`n| $content"
                }
                $message += "`n| Headers:`n$(($exc.Response.Headers | ForEach-Object { "    $_`: $($exc.Response.Headers[$_])" }) -join "`n")"
            } catch {
                # Don't throw here
            }
        } elseif (Test-AzureRestException -exception $exc) {
            if (($null -ne ($exc.PSObject.Properties | Where-Object { $_.Name -eq "Request" })) -and ($null -ne $exc.Request)) {
                $message += " Request: $($exc.Request.Method) $($exc.Request.RequestUri) $($exc.Request.Content)"
            }
            if (($null -ne ($exc.PSObject.Properties | Where-Object { $_.Name -eq "Response" })) -and ($null -ne $exc.Response)) {
                $message += " |`nResponse: $($exc.Response.StatusCode) $($exc.Response.ReasonPhrase) $($exc.Response.Content) |`nHeaders: $($exc.Response.Headers | ConvertTo-Json -depth 100 -compress)"
            }
        } else {
            $exceptionString = "$exception"
            if ($exceptionString.StartsWith("[")) {
                $message = "$message |`n$exceptionString`n--- End of inner error ---"
            } else {
                $message = "$message | $exceptionString"
            }
        }

        # Exclude sub-properties that duplicate the exception details when thrown as a string
        if (($exception -isnot [System.Management.Automation.ErrorRecord]) -or ($exception.Exception -isnot [System.Management.Automation.RuntimeException]) -or (-not $exception.Exception.WasThrownFromThrowStatement)) {
            $skip = @("ScriptStackTrace", "InvocationInfo", "PipelineIterationInfo")
            $exceptionMessage = ""
            if (($exception -is [System.Management.Automation.ErrorRecord]) -and ($null -ne $exception.Exception)) {
                $exceptionMessage = $exception.Exception.Message
                if ("$($exception.CategoryInfo)" -eq "OperationStopped: (:) [], InvalidOperationException") {
                    $skip += "CategoryInfo"
                }
            }
            $exception.PSObject.Properties | Where-Object {
                ($null -eq $_.Value) -or
                ($_.Value -eq $exceptionMessage)
            } | ForEach-Object {
                $skip += $_.Name
            }
            $exceptionProperties = $exception | Select-Object * -ExcludeProperty $skip
            [array]$propertyValues = $exceptionProperties.PSObject.Properties | Where-Object { $_.Name -ne "*" }
            if (($null -ne $propertyValues) -and ($propertyValues.Count -gt 0)) {
                if (($propertyValues.Count -eq 1) -and ($propertyValues.Name -eq "Exception")) {
                    $message = "$message Exception: $($exception.Exception)"
                } else {
                    $message = "$message Exception: $($exceptionProperties | Format-List * -Force | Out-String -Width ([int]::MaxValue))"
                }
            }
        }
        
        $scriptStackTrace = if (([bool]($exception.PSObject.Properties.Name -ceq "ScriptStackTrace")) -and ($exception.ScriptStackTrace -is [string])) {
            $exception.ScriptStackTrace
        }
        if (-not [string]::IsNullOrWhiteSpace($scriptStackTrace)) {
            $message = "$message`nScriptStackTrace:`n$scriptStackTrace"
        }
        
        $sourcePathNotSet = [string]::IsNullOrWhitespace($sourcePath)
        if (([bool]($exception.PSObject.Properties.Name -ceq "InvocationInfo")) -and ($exception.InvocationInfo -is [System.Management.Automation.InvocationInfo])) {
            $message = "$message`nPositionMessages:`n$(@($exception.InvocationInfo.PositionMessage) -join "`n")"
            if ($sourcePathNotSet) {
                $sourcePath = $exception.InvocationInfo.PSCommandPath
                $lineNumber = $exception.InvocationInfo.ScriptLineNumber
                $columnNumber = $exception.InvocationInfo.OffsetInLine
            }
        }

        # When using Invoke-Command, the InvocationInfo sometimes has the caller stack than the exception stack
        if ($sourcePathNotSet -and (-not [string]::IsNullOrWhiteSpace($scriptStackTrace))) {
            $split = $scriptStackTrace -split "`n"
            if ($split[0] -match "At (<ScriptBlock>, )*(.*): line ([0-9]*)") {
                if (($sourcePath -ne $sourcePath) -or ($lineNumber -ne [int]"$($matches[3])")) {
                    $sourcePath = "$($matches[2])"
                    $lineNumber = [int]"$($matches[3])"
                    $columnNumber = -1
                }
            }
        }
    }
    $message = $message -replace "`r","" -replace "`n`n`n*","`n`n"

    # Pull the callsite information from the call stack
    if ([string]::IsNullOrWhitespace($sourcePath) -and ($lineNumber -eq -1) -and ($columnNumber -eq -1)) {
        try {
            if ($null -eq $callingFrame) {
                $callingFrame = $callStack[1]
            }
            $sourcePath = $callingFrame.Position.File
            $lineNumber = $callingFrame.Position.StartLineNumber
            $columnNumber = $callingFrame.Position.StartColumnNumber
        } catch {
            # Do nothing
        }
    }

    if (
        ([string]::IsNullOrWhitespace($sourcePath) -or ($sourcePath -eq "<No file>")) -and
        (-not [string]::IsNullOrWhitespace($env:scriptBlockSourcePath))
    ) {
        $sourcePath = $env:scriptBlockSourcePath
        $lineNumber += [int]::Parse($env:scriptBlockOffset)
    }

    # Detect implicit set/throw usage if not explicitly set
    if (-not $psBoundParameters.ContainsKey("return")) {
        $thisCommand = $callStack[0].Command
        $position = $callStack[1].Position.StartScriptPosition.Line
        if (($position -notmatch $thisCommand) -and ($position -match "wpi")) {
            $position = $position -replace "wpi",$thisCommand
        }
        if ($position -match $thisCommand) {
            $precedent = "$($position -split $thisCommand | Select-Object -First 1)".Trim().ToLowerInvariant()
            if ($precedent.EndsWith("=") -or $precedent.EndsWith("throw")) {
                $return = $true
            }
        }
        if (-not $return) {
            $line = $callStack[1].Position.StartScriptPosition.Line
            if ($line -match $thisCommand) {
                $precedent = "$($line -split $thisCommand | Select-Object -First 1)".Trim().ToLowerInvariant()
                if ($precedent.EndsWith("=") -or $precedent.EndsWith("throw")) {
                    $return = $true
                }
            }
        }
    }

    if ($env:TF_BUILD -ne "true" -or $return) {
        $details = @()
        if (-not [string]::IsNullOrWhitespace($sourcePath)) {
            try {
                $details += Split-path $sourcePath -leaf
            } catch {
                # Handle errors like "Exception calling "GetFileName" with "1" argument(s): "Illegal characters in path."
                # From values like <ScriptBlock>
                $details += $sourcePath
            }
        }
        if ($lineNumber -gt -1) { $details += $lineNumber }
        if ($columnNumber -gt -1) { $details += $columnNumber }
        if ($code -gt -1) { $altDedetailstails += $code }
        
        $localDetails = if ($details.Length -gt 0) { "[$($details -join ":")($(Get-CommitInfo))] " } else { "" }
    }

    # Redact authorization headers
    $message = $message -replace "(?i)(authorization: (?:bearer|basic) )([^ \n]+)","`$1[REDACTED]"

    # Redact PATs
    $message = $message -replace "(?i)pat_token\"": ?""[^""]+""", "pat_token"":""[REDACTED]"""

    if ($env:TF_BUILD -eq "true") {
        $details = "type=$type"
        if (-not [string]::IsNullOrWhitespace($sourcePath)) { $details = "$details;sourcepath=$sourcePath" }
        if ($lineNumber -gt -1) { $details = "$details;linenumber=$lineNumber" }
        if ($columnNumber -gt -1) { $details = "$details;columnnumber=$columnNumber" }
        if ($code -gt -1) { $details = "$details;code=$code" }
        # See https://stackoverflow.com/a/58504290
        $multiLineMessage = $message -replace "`r*`n","%0D%0A"
        Write-Host "##vso[task.logissue $details]$multiLineMessage"
    } else {
        if ($type -eq "error") {
            Write-Host "$localDetails$message" -ForegroundColor "Red" -BackgroundColor "Black"
        } else {
            Write-Host "$localDetails$message" -ForegroundColor "Yellow" -BackgroundColor "Black"
        }
    }

    if ($return) {
        return "$localDetails$message"
    }
}
Export-ModuleMember -Function Write-PipelineIssue
New-Alias "wpi" Write-PipelineIssue -Force -Scope Global

# Sets the completion status of the current step in Azure DevOps
function Set-TaskCompletion {
    [CmdletBinding()]
    param(
        [ValidateSet("Succeeded", "SucceededWithIssues", "Failed")]
        [Parameter(Mandatory=$false,Position=1,ParameterSetName="Type")]
        [string]$status = "Succeeded",
        [System.Management.Automation.CallStackFrame]$callingFrame = $null
    )
    if ($env:TF_BUILD -eq "true") {
        if ($null -eq $callingFrame) { $callingFrame = (Get-PSCallStack)[1] }
        Write-Info "Setting task.complete to $status" -callingFrame:$callingFrame
        Write-Host "##vso[task.complete result=$status;]"
    }
}
Export-ModuleMember -Function Set-TaskCompletion

function Add-PathToPipeline {
    [CmdletBinding()]
    param(
        [string]$path
    )
    Write-Command "vso[task.prependpath]$path"
    Write-Host "##vso[task.prependpath]$path"
    $env:Path = "$path;$env:Path"
}
Export-ModuleMember -Function Add-PathToPipeline
New-Alias "prependPath" Add-PathToPipeline -Force -Scope Global

$script:timingData = $false # Set this to true to output time data - useful for debugging where time is spent locally
function Get-PrintableIdentifierFromStack {
    [CmdletBinding()]
    param(
        [System.Management.Automation.CallStackFrame]$callingFrame
    )

    $sourcePath = $callingFrame.Position.File
    $lineNumber = $callingFrame.Position.StartLineNumber
    $columnNumber = $callingFrame.Position.StartColumnNumber
    if ([string]::IsNullOrWhitespace($sourcePath) -and (-not [string]::IsNullOrWhitespace($env:scriptBlockSourcePath))) {
        $sourcePath = $env:scriptBlockSourcePath
        $lineNumber += [int]::Parse($env:scriptBlockOffset)
    }

    $details = @()
    try {
        $details += [System.IO.Path]::GetFileName($sourcePath)
    } catch {
        # Handle errors like "Exception calling "GetFileName" with "1" argument(s): "Illegal characters in path."
        # From values like <ScriptBlock>
        $details += $sourcePath
    }
    $details += $lineNumber
    $details += $columnNumber
    $maybeTimingData = if ($script:timingData) {
        "[$((Get-Date).ToUniversalTime().ToString("o"))] "
    }
    "$maybeTimingData[$($details -join ":")($(Get-CommitInfo))]"
}

function Write-Info {
    [CmdletBinding()]
    param(
        [string]$message,
        [System.Management.Automation.CallStackFrame]$callingFrame = $null,
        
        [string]$sourcePath = $null,
        [int]$lineNumber = -1,
        [int]$columnNumber = -1,
        
        $foregroundColor = "Gray",
        $backgroundColor,
        [switch]$noNewline
    )

    if ([string]::IsNullOrWhiteSpace($sourcePath)) {
        $callingFrame = if ($null -eq $callingFrame) { (Get-PSCallStack)[1] } else { $callingFrame }
        $details = Get-PrintableIdentifierFromStack $callingFrame
    } else {
        $details = @("$sourcePath")
        if ($PsBoundParameters.ContainsKey("lineNumber")) {
            $details += "$lineNumber"
            if ($PsBoundParameters.ContainsKey("columnNumber")) {
                $details += "$columnNumber"
            }
        }
        $maybeTimingData = if ($script:timingData) {
            "[$((Get-Date).ToUniversalTime().ToString("o"))] "
        }
        $details = "$maybeTimingData[$($details -join ":")($(Get-CommitInfo))]"
    }
    $additionalArguments = @{}
    if ($env:TF_BUILD -ne "true") {
        $additionalArguments.ForegroundColor = $foregroundColor
    }
    if ($null -ne $backgroundColor) {
        $additionalArguments.BackgroundColor = $backgroundColor
    }
    Write-Host "##[info] $details $message" @additionalArguments -noNewline:$noNewline
}
Export-ModuleMember -Function Write-Info

function Invoke-Section {
    [CmdletBinding()]
    param(
        [string]$message,
        [ScriptBlock]$scriptBlock,
        [switch]$writePipelineIssueOnException,
        [object[]]$argumentList,
        [ScriptBlock]$suffix = { "" },
        [System.Management.Automation.CallStackFrame]$callingFrame = $null
    )

    if ($null -eq $callingFrame) { $callingFrame = (Get-PSCallStack)[1] }
    $succeeded = $null
    $then = Get-Date

    $wrappedSuffix = { try { & $suffix } catch { } }

    Write-Section -message $message -start -callingFrame $callingFrame
    try {
        Invoke-Command -ScriptBlock $scriptBlock -NoNewScope -argumentList $argumentList
        $succeeded = $true
    } catch {
        $succeeded = $false
        if ($writePipelineIssueOnException) {
            throw Write-PipelineIssue -errorMessage "$message failed" -exception $_
        }
        throw
    } finally {
        $elapsed = (Get-Date) - $then
        if ($succeeded) {
            Write-Section -message "$message completed successfully after $elapsed$(& $wrappedSuffix)" -end -callingFrame $callingFrame
        } elseif ($null -eq $succeeded) {
            Write-Section -message "$message canceled after $elapsed$(& $wrappedSuffix)" -end -callingFrame $callingFrame
        } else {
            Write-Section -message "$message failed after $elapsed$(& $wrappedSuffix)" -end -callingFrame $callingFrame
        }
    }
}
Export-ModuleMember -Function Invoke-Section

# Invokes a section for each item passed in
function Invoke-ForEachSection {
    [CmdletBinding()]
    param(
        [scriptBlock]$message,
        [ScriptBlock]$scriptBlock,
        [switch]$writePipelineIssueOnException,
        
        [Parameter(Mandatory = $true, ValueFromPipeline=$true)]
        $inputObject
    )
    begin {
        $items = @()
    }
    process {
        $items += $inputObject
    }
    end {
        $invokeSection_i = 0
        $count = $items.Count
        $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
        ForEach ($item in $items) {
            ++$invokeSection_i
            $name = if ($null -eq $message) { "$item" } else { & $message $item }
            Invoke-Section -message "[$($invokeSection_i)/$count] $name" -scriptBlock $scriptBlock -argumentList $item -writePipelineIssueOnException:$writePipelineIssueOnException -suffix { " ~ $([TimeSpan]::FromMilliseconds($stopwatch.Elapsed.TotalMilliseconds / $invokeSection_i * ($count - $invokeSection_i))) remaining" } -callingFrame (Get-PSCallStack)[1]
        }
    }
}
Export-ModuleMember -Function Invoke-ForEachSection

function Write-Section {
    [CmdletBinding()]
    param(
        [string]$message,
        [switch]$start,
        [switch]$end,
        [System.Management.Automation.CallStackFrame]$callingFrame = $null,
        
        [string]$sourcePath = $null,
        [int]$lineNumber = -1,
        [int]$columnNumber = -1,

        [switch]$noNewline
    )

    if ([string]::IsNullOrWhiteSpace($sourcePath)) {
        $callingFrame = if ($null -eq $callingFrame) { (Get-PSCallStack)[1] } else { $callingFrame }
        $details = Get-PrintableIdentifierFromStack $callingFrame
    } else {
        $details = @("$sourcePath")
        if ($PsBoundParameters.ContainsKey("lineNumber")) {
            $details += "$lineNumber"
            if ($PsBoundParameters.ContainsKey("columnNumber")) {
                $details += "$columnNumber"
            }
        }
        $maybeTimingData = if ($script:timingData) {
            "[$((Get-Date).ToUniversalTime().ToString("o"))] "
        }
        $details = "$maybeTimingData[$($details -join ":")($(Get-CommitInfo))]"
    }

    if ($start) {
        $message = "Starting: $message"
    } elseif ($end) {
        $message = "Finishing: $message"
    }
    $arguments = @{
        Object = "##[section] $details $message";
    }
    if ($env:TF_BUILD -ne "true") {
        $arguments.ForegroundColor = [System.ConsoleColor]::DarkGreen
    }
    Write-Host @arguments -NoNewline:$noNewline
}
Export-ModuleMember -Function Write-Section

function Invoke-Group {
    [CmdletBinding()]
    param(
        [string]$message,
        [ScriptBlock]$scriptBlock,
        [switch]$failureAsWarning
    )

    $callingFrame = (Get-PSCallStack)[1]
    try {
        Write-Group -message $message -start -callingFrame $callingFrame
        Invoke-Command -ScriptBlock $scriptBlock -NoNewScope
    } catch {
        if ($failureAsWarning) {
            Write-PipelineIssue -warningMessage "$message failed" -exception $_
        } else {
            throw
        }
    } finally {
        Write-Group -end
    }
}
Export-ModuleMember -Function Invoke-Group

function Write-Group {
    [CmdletBinding()]
    param(
        [string]$message,
        [switch]$start,
        [switch]$end,
        [System.Management.Automation.CallStackFrame]$callingFrame = $null
    )

    if ($end) {
        $message = "##[endgroup]"
    } else {
        $message = "##[group]$message"
    }
    Write-Host $message
}
Export-ModuleMember -Function Write-Group

function Write-Command {
    [CmdletBinding()]
    param(
        [string]$message,
        [System.Management.Automation.CallStackFrame]$callingFrame = $null,
        
        [string]$sourcePath = $null,
        [int]$lineNumber = -1,
        [int]$columnNumber = -1,
        [switch]$noNewline
    )

    if ([string]::IsNullOrWhiteSpace($sourcePath)) {
        $callingFrame = if ($null -eq $callingFrame) { (Get-PSCallStack)[1] } else { $callingFrame }
        $details = Get-PrintableIdentifierFromStack $callingFrame
    } else {
        $details = @("$sourcePath")
        if ($PsBoundParameters.ContainsKey("lineNumber")) {
            $details += "$lineNumber"
            if ($PsBoundParameters.ContainsKey("columnNumber")) {
                $details += "$columnNumber"
            }
        }
        $maybeTimingData = if ($script:timingData) {
            "[$((Get-Date).ToUniversalTime().ToString("o"))] "
        }
        $details = "$maybeTimingData[$($details -join ":")($(Get-CommitInfo))]"
    }
    $arguments = @{
        Object = "##[command] $details $message";
    }
    if ($env:TF_BUILD -ne "true") {
        $arguments.ForegroundColor = [System.ConsoleColor]::DarkBlue
        if ($host.UI.RawUI.BackgroundColor -eq [System.ConsoleColor]::DarkMagenta) { # Powershell blue for some reason...
            $arguments.ForegroundColor = [System.ConsoleColor]::DarkCyan
        }
    }
    Write-Host @arguments -NoNewline:$noNewline
}
Export-ModuleMember -Function Write-Command

# Invokes the command with Invoke-Expression and logs
function Invoke-CommandAndLog {
    [CmdletBinding()]
    param(
        # Command to run
        $command,
        # Log-safe command - if not specified, defaults to the value of $command
        $logCommand = $command,
        
        $callingFrame = $null
    )

    if ($null -eq $callingFrame) {
        $callingFrame = (Get-PSCallStack)[1]
    }

    $lastExitCode = 0 # Not set if we're running a cmdlet instead of an external program
    $output = @()
    Write-Command $logCommand -callingFrame $callingFrame
    try {
        $global:ado_ical_returningResult = $false
        & { # Wrap call in a script block to simplify differenciating between results and log output
            Invoke-Expression $command | ForEach-Object {
                $global:ado_ical_returningResult = $true
                $_
                $global:ado_ical_returningResult = $false
            }
        } *>&1 | ForEach-Object {
            if ($global:ado_ical_returningResult) {
                $_
            } else {
                $output += $_
                Write-Host $_
            }
        }
    } catch {
        throw Write-PipelineIssue -errorMessage "[$logCommand] failed | $($output -join "`n")" -exception $_ -callingFrame $callingFrame 6>$null
    }
    if ($lastExitCode -ne 0) {
        throw Write-PipelineIssue -errorMessage "[$logCommand] failed $lastExitCode | $($output -join "`n")" -callingFrame $callingFrame 6>$null
    }
}
Export-ModuleMember -Function Invoke-CommandAndLog

function Write-DebugInformation {
    [CmdletBinding()]
    param(
        [string]$message,
        [System.Management.Automation.CallStackFrame]$callingFrame = $null,
        
        [string]$sourcePath = $null,
        [int]$lineNumber = -1,
        [int]$columnNumber = -1,
        [switch]$noNewline
    )

    if ([string]::IsNullOrWhiteSpace($sourcePath)) {
        $callingFrame = if ($null -eq $callingFrame) { (Get-PSCallStack)[1] } else { $callingFrame }
        $details = Get-PrintableIdentifierFromStack $callingFrame
    } else {
        $details = @("$sourcePath")
        if ($PsBoundParameters.ContainsKey("lineNumber")) {
            $details += "$lineNumber"
            if ($PsBoundParameters.ContainsKey("columnNumber")) {
                $details += "$columnNumber"
            }
        }
        $maybeTimingData = if ($script:timingData) {
            "[$((Get-Date).ToUniversalTime().ToString("o"))] "
        }
        $details = "$maybeTimingData[$($details -join ":")($(Get-CommitInfo))]"
    }
    $arguments = @{
        Object = "##[debug] $details $message";
    }
    if ($env:TF_BUILD -ne "true") {
        $arguments.ForegroundColor = [System.ConsoleColor]::DarkMagenta
        if ($host.UI.RawUI.BackgroundColor -eq [System.ConsoleColor]::DarkMagenta) {
            $arguments.ForegroundColor = [system.ConsoleColor]::Magenta
        }
        Write-Host @arguments -NoNewLine:$noNewline
    }
}
Export-ModuleMember -Function Write-DebugInformation

function Invoke-ScriptBlockLoggingHttpRequests {
    [CmdletBinding()]
    param(
        $scriptBlock
    )

    $oldDebugPreference = $global:DebugPreference
    $global:DebugPreference = "Continue"
    try {
        $lastUrl = ""
        $lastStart = [DateTime]::UtcNow
        & $scriptBlock 5>&1 | ForEach-Object {
            if ($_ -isnot [System.Management.Automation.DebugRecord]) {
                $_
            } else {
                if ($_.Message -match "============================ HTTP REQUEST ============================`r?`n[.`r`n]*HTTP Method:`r?`n([^`r`n]*)`r?`n[.`r`n]*Absolute Uri:`r?`n(.*)`r?`n") {
                    $method = $matches[1]
                    $url = $matches[2]
                    $lastUrl = "$method $url"
                    $global:l = $lastUrl
                    $debugArguments = @{}
                    if (($null -ne $_.InvocationInfo) -and (-not [string]::IsNullOrWhiteSpace($_.InvocationInfo.ScriptName))) {
                        $debugArguments.sourcePath = $_.InvocationInfo.ScriptName
                        if ($null -ne $_.InvocationInfo.ScriptLineNumber) {
                            $debugArguments.lineNumber = $_.InvocationInfo.ScriptLineNumber
                        }
                        if ($null -ne $_.InvocationInfo.ScriptLineNumber) {
                            $debugArguments.columnNumber = $_.InvocationInfo.OffsetInLine
                        }
                    }
                    Write-Command -message $lastUrl @debugArguments
                    $lastStart = [DateTime]::UtcNow
                } elseif ($_.Message -match "============================ HTTP RESPONSE ============================`r?`n[.`r`n]*Status Code:`r?`n([^`r`n]*)`r?`n") {
                    $elapsed = [DateTime]::UtcNow - $lastStart
                    $status = $matches[1]

                    $debugArguments = @{}
                    if (($null -ne $_.InvocationInfo) -and (-not [string]::IsNullOrWhiteSpace($_.InvocationInfo.ScriptName))) {
                        $debugArguments.sourcePath = $_.InvocationInfo.ScriptName
                        if ($null -ne $_.InvocationInfo.ScriptLineNumber) {
                            $debugArguments.lineNumber = $_.InvocationInfo.ScriptLineNumber
                        }
                        if ($null -ne $_.InvocationInfo.ScriptLineNumber) {
                            $debugArguments.columnNumber = $_.InvocationInfo.OffsetInLine
                        }
                    }

                    $statusCode = [System.Net.HttpStatusCode]::Unused
                    if ([system.net.httpStatusCode]::TryParse($status, [ref]$statusCode)) {
                        if (($statusCode.value__ -ge 200) -and ($statusCode.value__ -lt 300)) {
                            Write-Info "[$($statusCode.value__): $status after $elapsed] | $lastUrl" -foregroundColor "Green" @debugArguments
                        } else {
                            Write-Info "[$($statusCode.value__): $status after $elapsed] | $lastUrl" -foregroundColor "Red" -backgroundColor "Black" @debugArguments
                        }
                    } else {
                        Write-Info "[$status after $elapsed] | $lastUrl " @debugArguments
                    }
                }
            }
        }
    } finally {
        $global:DebugPreference = $oldDebugPreference
    }
}
Export-ModuleMember -Function Invoke-ScriptBlockLoggingHttpRequests

function Set-PipelineTaskPercentage {
    [CmdletBinding()]
    param(
        [int]$percentage
    )

    if (($percentage -lt 0) -or ($percentage -gt 100)) { throw "The `$percentage must be between 0 and 100: $percentage" }
    
    if ($env:TF_BUILD -eq "true") {
        Write-Host "##vso[task.setprogress value=$percentage;]Task"
    }
}
Export-ModuleMember -Function Set-PipelineTaskPercentage

function Add-BuildLog {
    [CmdletBinding()]
    param(
        [string]$path
    )

    if ($env:TF_BUILD -eq "true") {
        Write-Host "##vso[build.uploadlog]$path"
    }
}
Export-ModuleMember -Function Add-BuildLog

function Set-BuildNumber {
    [CmdletBinding()]
    param(
        [string]$buildNumber
    )
    
    if ([string]::IsNullOrWhitespace($buildNumber)) { throw "The `$buildNumber must not be null or whitespace" }
    if ($env:TF_BUILD -eq "true") {
        # [error]TF209010: The build number format "" contains invalid character(s), is too long, or ends with '.'. The maximum length of a build number is 255 characters. Characters which are not allowed include '"', '/', ':', '<', '>', '\', '|', '?', '@', and '*'.
        $buildNumber = $buildNumber -replace "[`"/:<>\\\|\?@\*]",""
        $buildNumber = $buildNumber.Substring(0, [Math]::Min($buildNumber.Length, 255))
        Write-Info "Setting build number from [$env:Build_BuildNumber] to [$buildNumber]"
        Write-Host "##vso[build.updatebuildnumber]$buildNumber"
    }
}
Export-ModuleMember -Function Set-BuildNumber

function Add-BuildTag {
    [CmdletBinding()]
    param(
        [string[]]$tag,

        # for a specific build
        [string]$organization = $script:defaultOrganization,
        [string]$project = $script:defaultProject,
        [nullable[int]]$buildId
    )

    if ($null -eq $tag) { throw "The `$tag must be set" }

    if ($env:TF_BUILD -ne "true") {
        return
    }

    if ($null -eq $buildId) { # Logging command for adding a tag isn't allowed on OneBranch, but we can do it from the api
        $buildId = [int]::Parse($env:BUILD_BUILDID)
    }
    if ([string]::IsNullOrWhitespace($organization)) { throw "The `$organization must not be null or whitespace" }
    if ([string]::IsNullOrWhitespace($project)) { throw "The `$project must not be null or whitespace" }
    if ($buildId -le 0) { throw "The `$buildId must be a positive number" }

    Write-Host "Setting build tag [$tag] to build $buildId"

    $null = Invoke-RestMethodWithRetry -method "POST" -uri "https://dev.azure.com/$organization/$project/_apis/build/builds/$buildId/tags`?api-version=5.1" -body $tag
}
Export-ModuleMember -Function Add-BuildTag

function Set-ReleaseName {
    [CmdletBinding()]
    param(
        [string]$releaseName
    )

    if ([string]::IsNullOrWhitespace($releaseName)) { throw "The `$releaseName must not be null or whitespace" }
    if ($env:TF_BUILD -eq "true") {
        Write-Host "##vso[release.updatereleasename]$releaseName"
    }
}
Export-ModuleMember -Function Set-ReleaseName

# Upload a file that can be downloaded with task logs
# Use this to provide additional data that would be a lot to add to script output
# See https://learn.microsoft.com/azure/devops/pipelines/scripts/logging-commands?view=azure-devops&tabs=bash#uploadfile-upload-a-file-that-can-be-downloaded-with-task-logs
function Publish-TaskFile {
    [CmdletBinding()]
    param(
        # The file path
        [string]$path
    )

    if ([string]::IsNullOrWhiteSpace($path)) { throw "-path must not be null or whitespace" }
    if (-not (Test-Path $path)) { throw "-path $path does not exist" }
    
    Write-Command "vso[task.uploadfile]$path"
    Write-Host  "##vso[task.uploadfile]$path"
}
Export-ModuleMember -Function Publish-TaskFile

# Uploads a file to the build artifacts
# See https://learn.microsoft.com/azure/devops/pipelines/scripts/logging-commands?view=azure-devops&tabs=bash#upload-upload-an-artifact
function Publish-ArtifactFile {
    [CmdletBinding()]
    param(
        # The file path
        [string]$path,
        [string]$artifactName,
        [string]$containerFolder
    )

    if ([string]::IsNullOrWhiteSpace($path)) { throw "-path must not be null or whitespace" }
    if (-not (Test-Path $path)) { throw "-path $path does not exist" }
    if ([string]::IsNullOrWhiteSpace($artifactName)) { throw "-artifactName must not be null or whitespace" }

    if ($env:SYSTEM_HOSTTYPE -ne "build") {
        Write-PipelineIssue -warningMessage "Skipping publish for $path to $containerFolder $artifactName as `$env:SYSTEM_HOSTTYPE, $env:SYSTEM_HOSTTYPE, -ne build"
        return
    }

    $details = "artifactname=$artifactName"

    # Ado will only show these in a hierarchy view if you use \s for the folder separators
    if (-not [string]::IsNullOrWhiteSpace($containerFolder)) {
        $containerFolder = $containerFolder -replace "/","\"
        $details += ";containerfolder=$containerFolder"
    }
    
    Write-Command "vso[artifact.upload $details;]$path"
    Write-Host  "##vso[artifact.upload $details;]$path"
}
Export-ModuleMember -Function Publish-ArtifactFile

# See https://learn.microsoft.com/rest/api/azure/devops/core/projects/list?view=azure-devops-rest-5.1
function Get-Projects {
    [CmdletBinding()]
    param(
        [string]$organization = $script:defaultOrganization
    )

    if ([string]::IsNullOrWhitespace($organization)) { throw "The `$organization must not be null or whitespace" }

    return Invoke-RestMethodWithRetry -method "GET" -uri "https://dev.azure.com/$organization/_apis/projects`?api-version=6.0"
}
Export-ModuleMember -Function Get-Projects

# Cache repo lookups - we're using $repo as either repoName or repoId, we'll cache the results so we don't have to keep looking them up
$script:repoCache = @{}

# See https://learn.microsoft.com/rest/api/azure/devops/git/repositories/list?view=azure-devops-rest-5.1
function Get-Repository {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory=$true, ParameterSetName="repo")]
        [string]$repo,
        [Parameter(Mandatory=$true, ParameterSetName="pr")]
        [nullable[int]]$pullRequestId,

        [Parameter(ParameterSetName="all")]
        [Parameter(ParameterSetName="repo")]
        [Parameter(ParameterSetName="pr")]
        [string]$organization = $script:defaultOrganization,

        [Parameter(ParameterSetName="all")]
        [Parameter(ParameterSetName="repo")]
        [string]$project = $script:defaultProject
    )

    if ([string]::IsNullOrWhitespace($organization)) { throw "-organization must not be null or whitespace" }

    if (-not [string]::IsNullOrWhiteSpace($repo)) {
        if ([string]::IsNullOrWhitespace($project)) { throw "-project must not be null or whitespace" }

        $cacheKey = "${organization}_${project}_${repo}"
        if ($script:repoCache.ContainsKey($cacheKey)) {
            return $script:repoCache.$cacheKey
        }
        
        $repoValue = Invoke-RestMethodWithRetry -method "GET" -uri "https://dev.azure.com/$organization/$project/_apis/git/repositories/$repo`?api-version=6.0" -returnNullOn404
        if ($null -ne $repoValue) {
            $script:repoCache.$cacheKey = $repoValue
            return $repoValue
        }

        $repoValue = Invoke-RestMethodWithRetry -method "GET" -uri "https://dev.azure.com/$organization/$project/_apis/git/repositories/Crm.Omnichannel.$repo`?api-version=6.0" -returnNullOn404
        if ($null -ne $repoValue) {
            $script:repoCache.$cacheKey = $repoValue
            return $repoValue
        }

        $allRepos = Get-Repository -organization:$organization -project:$project
        $repoOptions = @(
            "^$repo$",
            "^Crm.Omnichannel.$repo$",
            "^Crm.Omnichannel.$repo",
            "$repo$",
            "$repo"
        )
        forEach ($option in $repoOptions) {
            [array]$repoValue = $allRepos | Where-Object {
                $_.name -match $option
            }
            if (($null -ne $repoValue) -and ($repoValue.Count -gt 1)) {
                throw "Found multiple repositories matching $option`: $($repoValue.name)"
            }
            $repoValue = $repoValue | Select-Object -First 1
            if ($null -ne $repoValue) {
                break
            }
        }
        if ($null -eq $repoValue) {
            throw "Failed to find repo matching $repo | Options: $($allRespos.name)"
        }
        $script:repoCache.$cacheKey = $repoValue
        return $repoValue
    } elseif ($null -ne $pullRequestId) {
        $cacheKey = "${organization}_${pullRequestId}"
        if ($script:repoCache.ContainsKey($cacheKey)) {
            return $script:repoCache.$cacheKey
        }

        $pr = Get-PullRequest -pullRequestId $pullRequestId -organization $organization
        $result = Get-Repository -repo $($pr.repository.name) -organization $organization -project $pr.repository.project.name
        $script:repoCache.$cacheKey = $result
        return $result
    }

    if ([string]::IsNullOrWhitespace($project)) { throw "-project must not be null or whitespace" }

    $cacheKey = "${organization}_${project}__all"
    if ($script:repoCache.ContainsKey($cacheKey)) {
        return $script:repoCache.$cacheKey
    }

    $allRepos = Invoke-RestMethodWithRetry -method "GET" -uri "https://dev.azure.com/$organization/$project/_apis/git/repositories`?api-version=6.0"
    $script:repoCache.$cacheKey = $allRepos
    ForEach ($repoValue in $allRepos) {
        $shortName = $repoValue.name -replace "^Crm\.Omnichannel\.",""
        ForEach ($cacheKey in @(
            "${organization}_${project}_$($repoValue.id)"
            "${organization}_${project}_$($repoValue.name)"
            "${organization}_${project}_${shortName}"
        )) {
            $script:repoCache.$cacheKey = $repoValue
        }
    }
    return $allRepos
}
Export-ModuleMember -Function Get-Repository

# See https://learn.microsoft.com/rest/api/azure/devops/core/projects/list?view=azure-devops-rest-7.0
function Get-Commit {
    [CmdletBinding()]
    param(
        [string]$organization = $script:defaultOrganization,
        [string]$project = $script:defaultProject,
        [string]$repo,
        [string]$commitId
    )

    if ([string]::IsNullOrWhitespace($organization)) { throw "-organization must not be null or whitespace" }
    if ([string]::IsNullOrWhitespace($project)) { throw "-project must not be null or whitespace" }
    if ([string]::IsNullOrWhitespace($repo)) { throw "-repo must not be null or whitespace" }
    if ([string]::IsNullOrWhitespace($commitId)) { throw "-commitId must not be null or whitespace" }

    $repoValue = Get-Repository -repo:$repo -organization:$organization -project:$project
    return Invoke-RestMethodWithRetry -method "GET" -uri "https://dev.azure.com/$organization/$project/_apis/git/repositories/$($repoValue.Name)/commits/$commitId`?api-version=7.0"
}
Export-ModuleMember -Function Get-Commit

# See https://learn.microsoft.com/rest/api/azure/devops/git/commits/get%20commits?view=azure-devops-rest-6.0
function Get-Commits {
    [CmdletBinding()]
    param(
        [string]$organization = $script:defaultOrganization,
        [string]$project = $script:defaultProject,
        [string]$repo,
        
        # Number of entries to skip
        [int]$skip,
        
        # Maximum number of entries to retrieve
        [int]$top,

        # Alias or display name of the author
        [string]$author,
        
        # Version string identifier (name of tag/branch, SHA1 of commit)
        [string]$compareVersion,
        
        # Version options - Specify additional modifiers to version (e.g Previous)
        [ValidateSet("firstParent", "none", "previousChange")][string]$compareVersionOption,
        
        # Version type (branch, tag, or commit). Determines how Id is interpreted
        [ValidateSet("branch", "commit", "tag")][string]$compareVersionType,

        # Only applies when an itemPath is specified. This determines whether to exclude delete entries of the specified path.
        [bool]$excludeDeletes,

        # If provided, a lower bound for filtering commits alphabetically
        [string]$fromCommitId,

        # If provided, only include history entries created after this date (string)
        [string]$fromDate,

        # What Git history mode should be used. This only applies to the search criteria when Ids = null and an itemPath is specified.
        [ValidateSet("firstParent", "fullHistory", "fullHistorySimplifyMerges", "simplifiedHistory")][string]$historyMode,

        # If provided, specifies the exact commit ids of the commits to fetch. May not be combined w
        [string[]]$ids,

        # Whether to include the _links field on the shallow references
        [bool]$includeLinks,

        # Whether to include the push information
        [bool]$includePushData,

        # Whether to include the image Url for committers and authors
        [bool]$includeUserImageUrl,

        # Whether to include linked work items
        [bool]$includeWorkItems,

        # Path of item to search under
        [string]$itemPath,

        # Version string identifier (name of tag/branch, SHA1 of commit)
        [string]$itemVersion,

        # Version options - Specify additional modifiers to version (e.g Previous)
        [ValidateSet("branch", "commit", "tag")][string]$itemVersionOption,

        # Version type (branch, tag, or commit). Determines how Id is interpreted
        [ValidateSet("firstParent", "none", "previousChange")][string]$itemVersionType,

        # If enabled, this option will ignore the itemVersion and compareVersion parameters
        [bool]$showOldestCommitsFirst,

        # If provided, an upper bound for filtering commits alphabetically
        [string]$toCommitId,

        # If provided, only include history entries created before this date (string)
        [string]$toDate,

        # Alias or display name of the committer
        [string]$user
    )

    if ([string]::IsNullOrWhitespace($organization)) { throw "-organization must not be null or whitespace" }
    if ([string]::IsNullOrWhitespace($project)) { throw "-project must not be null or whitespace" }
    if ([string]::IsNullOrWhitespace($repo)) { throw "-repo must not be null or whitespace" }

    $url = "https://dev.azure.com/$organization/$project/_apis/git/repositories/$repo/commits`?api-version=6.0"
    if ($PsBoundParameters.ContainsKey("skip")) {
        $url = "$url&`searchCriteria.$skip=$skip"
    }
    if ($PsBoundParameters.ContainsKey("top")) {
        $url = "$url&`searchCriteria.$top=$top"
    }
    if ($PsBoundParameters.ContainsKey("author")) {
        $url = "$url&`searchCriteria.author=$author"
    }
    if ($PsBoundParameters.ContainsKey("compareVersion")) {
        $url = "$url&`searchCriteria.compareVersion.version=$compareVersion"
    }
    if ($PsBoundParameters.ContainsKey("compareVersionOption")) {
        $url = "$url&`searchCriteria.compareVersion.compareVersionOption=$compareVersionOption"
    }
    if ($PsBoundParameters.ContainsKey("compareVersionType")) {
        $url = "$url&`searchCriteria.compareVersion.versionType=$compareVersionType"
    }
    if ($PsBoundParameters.ContainsKey("excludeDeletes")) {
        $url = "$url&`searchCriteria.excludeDeletes=$excludeDeletes"
    }
    if ($PsBoundParameters.ContainsKey("fromCommitId")) {
        $url = "$url&`searchCriteria.fromCommitId=$fromCommitId"
    }
    if ($PsBoundParameters.ContainsKey("fromDate")) {
        $url = "$url&`searchCriteria.fromDate=$fromDate"
    }
    if ($PsBoundParameters.ContainsKey("historyMode")) {
        $url = "$url&`searchCriteria.historyMode=$historyMode"
    }
    if ($PsBoundParameters.ContainsKey("ids")) {
        $url = "$url&`searchCriteria.ids=$($ids -join ",")"
    }
    if ($PsBoundParameters.ContainsKey("includeLinks")) {
        $url = "$url&`searchCriteria.includeLinks=$includeLinks"
    }
    if ($PsBoundParameters.ContainsKey("includePushData")) {
        $url = "$url&`searchCriteria.includePushData=$includePushData"
    }
    if ($PsBoundParameters.ContainsKey("includeUserImageUrl")) {
        $url = "$url&`searchCriteria.includeUserImageUrl=$includeUserImageUrl"
    }
    if ($PsBoundParameters.ContainsKey("includeWorkItems")) {
        $url = "$url&`searchCriteria.includeWorkItems=$includeWorkItems"
    }
    if ($PsBoundParameters.ContainsKey("itemPath")) {
        $url = "$url&`searchCriteria.itemPath=$itemPath"
    }
    if ($PsBoundParameters.ContainsKey("itemVersion")) {
        $url = "$url&`searchCriteria.itemVersion.version=$itemVersion"
    }
    if ($PsBoundParameters.ContainsKey("itemVersionType")) {
        $url = "$url&`searchCriteria.itemVersion.versionType=$itemVersionType"
    }
    if ($PsBoundParameters.ContainsKey("showOldestCommitsFirst")) {
        $url = "$url&`searchCriteria.showOldestCommitsFirst=$showOldestCommitsFirst"
    }
    if ($PsBoundParameters.ContainsKey("toCommitId")) {
        $url = "$url&`searchCriteria.toCommitId=$toCommitId"
    }
    if ($PsBoundParameters.ContainsKey("toDate")) {
        $url = "$url&`searchCriteria.toDate=$toDate"
    }
    if ($PsBoundParameters.ContainsKey("user")) {
        $url = "$url&`searchCriteria.user=$user"
    }
    return Invoke-RestMethodWithRetry -method "GET" -uri $url
}
Export-ModuleMember -Function Get-Commits


# See https://learn.microsoft.com/rest/api/azure/devops/build/builds/get?view=azure-devops-rest-7.0
# See https://learn.microsoft.com/rest/api/azure/devops/build/definitions/list?view=azure-devops-rest-7.0
function Get-BuildDefinition {
    [CmdletBinding()]
    param(
        # The build definition id
        [Parameter(Mandatory=$true, ParameterSetName="single")]
        [int]$definitionId,

        # The repository name
        [Parameter(Mandatory=$true, ParameterSetName="repo")]
        [Alias("repository")]
        [string]$repo,

        # A yaml file name, if specified
        [Parameter(ParameterSetName="repo")]
        $yamlFileName,

        # The ado organization name
        [string]$organization = $script:defaultOrganization,

        # The ado project name
        [string]$project = $script:defaultProject
    )

    if ([string]::IsNullOrWhitespace($organization)) { throw "The `$organization must not be null or whitespace" }
    if ([string]::IsNullOrWhitespace($project)) { throw "The `$project must not be null or whitespace" }
    
    if ($PsCmdlet.ParameterSetName -eq "single") {
        if ($definitionId -le 0) { throw "The `$buildDefinitionId must be a positive number" }

        return Invoke-RestMethodWithRetry -method "GET" -uri "https://dev.azure.com/$organization/$project/_apis/build/definitions/$definitionId`?api-version=6.0" -returnNullOn404
    } else {
        $repoValue = Get-Repository -repo:$repo -organization:$organization -project:$project
        $url = "https://dev.azure.com/$organization/$project/_apis/build/definitions?repositoryId=$($repoValue.id)&repositoryType=tfsgit&includeAllProperties=$true&api-version=7.0"
        if (-not [string]::IsNullOrWhiteSpace($yamlFileName)) {
            $url += "&yamlFilename=$yamlFileName"
        }
        return Invoke-RestMethodWithRetry -method "GET" -uri $url -returnNullOn404 -organization $organization
    }
}
Export-ModuleMember -Function Get-BuildDefinition

# See https://learn.microsoft.com/rest/api/azure/devops/build/builds/get?view=azure-devops-rest-5.1
function Get-Build {
    [CmdletBinding()]
    param(
        [int]$buildId,

        [string]$organization = $script:defaultOrganization,
        [string]$project = $script:defaultProject
    )

    if ([string]::IsNullOrWhitespace($organization)) { throw "The `$organization must not be null or whitespace" }
    if ([string]::IsNullOrWhitespace($project)) { throw "The `$project must not be null or whitespace" }
    if ($buildId -le 0) { throw "The `$buildId must be a positive number" }

    return Invoke-RestMethodWithRetry -method "GET" -uri "https://dev.azure.com/$organization/$project/_apis/build/builds/$buildId`?api-version=6.0"
}
Export-ModuleMember -Function Get-Build

function Update-BuildDefinition {
    [CmdletBinding()]
    param(
        [string]$organization = $script:defaultOrganization,
        [string]$project = $script:defaultProject,
        $body,
        [string]$comment
    )

    if ([string]::IsNullOrWhitespace($organization)) { throw "The `$organization must not be null or whitespace" }
    if ([string]::IsNullOrWhitespace($project)) { throw "The `$project must not be null or whitespace" }
    if ($null -eq $body) { throw "The `$body must not be null" }

    if (($body -isnot [string]) -and (-not [string]::IsNullOrWhiteSpace($comment))) {
        if ([bool]($body.PSObject.Properties.Name -eq "comment")) {
            $body.comment = $comment
        } else {
            $body | Add-Member -MemberType NoteProperty -Name "comment" -Value $comment
        }
    }
    return Invoke-RestMethodWithRetry -method "PUT" -uri "https://dev.azure.com/$organization/$project/_apis/build/definitions/$($body.id)`?api-version=6.0" -body $body
}
Export-ModuleMember -Function Update-BuildDefinition

$script:cdpxEndpoint = "https://op-onboarding-prod.azurefd.net"

# See https://onebranch.visualstudio.com/Pipeline/_wiki/wikis/Pipeline.wiki/1923/Allow-Extra-Branches-for-Existing-Official-Build
function Get-CDPxBuildDefinitionDetails {
    [CmdletBinding()]
    param(
        [string]$organization = $script:defaultOrganization,
        [string]$projectId = $script:defaultProjectId,
        [int]$definitionId
    )

    if ([string]::IsNullOrWhitespace($organization)) { throw "The `$organization must not be null or whitespace" }
    if ([string]::IsNullOrWhitespace($projectId)) { throw "The `$projectId must not be null or whitespace" }
    if ($definitionId -le 0) { throw "The `$buildDefinitionId must be a positive number" }

    $url = "$script:cdpxEndpoint/api/servicetree/cosmosdb/builddefinition?account_name=$organization&project_id=$projectId&build_definition_id=$definitionId"
    $value = Invoke-RestMethodWithRetry -method "GET" -uri $url -organization $organization
    if ($value -is [string]) {
        try {
            $value = ConvertFrom-Json $value
        } catch {
            throw Write-PipelineIssue -errorMessage "Failed to deserialized output from $url | $value"
        }
    }
    return $value
}
Export-ModuleMember -Function Get-CDPxBuildDefinitionDetails

<# See https://onebranch.visualstudio.com/Pipeline/_wiki/wikis/Pipeline.wiki/1923/Allow-Extra-Branches-for-Existing-Official-Build
.Example
Update-CDPxBuildDefinitionDetails -definitionId 1234 -branchRegex ".*" -allowPullRequests
#> 
function Update-CDPxBuildDefinitionDetails {
    [CmdletBinding()]
    param(
        [string]$organization = $script:defaultOrganization,
        [string]$projectId = $script:defaultProjectId,
        [int]$definitionId,
        [string]$branchRegex,
        [string]$buildType,
        [switch]$allowPullRequests
    )

    if ([string]::IsNullOrWhitespace($organization)) { throw "The `$organization must not be null or whitespace" }
    if ([string]::IsNullOrWhitespace($projectId)) { throw "The `$projectId must not be null or whitespace" }
    if ($definitionId -le 0) { throw "The `$buildDefinitionId must be a positive number" }

    $current = Get-CDPxBuildDefinitionDetails -organization $organization -projectId $projectId -definitionId $definitionId

    $body = @{
        account_name = $organization;
        project_id = $projectId;
        build_definition_id = $definitionId.ToString();
        build_type = $current.build_type;
        service_id = $current.service_id;
    }
    if ($null -ne ($current.PSObject.Properties | Where-Object { $_.Name -eq "allow_branch_regex" })) {
        $body.allow_branch_regex = $current.allow_branch_regex;
    }
    if (-not [string]::IsNullOrWhiteSpace($branchRegex)) {
        $body.allow_branch_regex = $branchRegex
    }
    
    if (-not [string]::IsNullOrWhiteSpace($buildType)) {
        $body.build_type = $buildType
    }

    if ($allowPullRequests) {
        $body.allow_pullrequest = $true
    }

    return Invoke-RestMethodWithRetry -method "PUT" -uri "$script:cdpxEndpoint/api/definition/existed?account_name=$organization" -body $body -organization $organization
}
Export-ModuleMember -Function Update-CDPxBuildDefinitionDetails

# See https://learn.microsoft.com/rest/api/azure/devops/build/builds/list?view=azure-devops-rest-5.1
function Get-Builds {
    [CmdletBinding()]
    param(
        [string] $organization = $script:defaultOrganization,
        [string] $project = $script:defaultProject,
        [int[]] $definitionId = $null,
        [string] $branchName = $null,
        [nullable[int]]$pullRequestId,
        [string] $repo = $null,
        [string] $queryOrder = "finishTimeDescending",
        [nullable[int]] $maxBuildsPerDefinition = $null,
        [ValidateSet("canceled", "failed", "none", "partiallySucceeded", "succeeded")][string[]] $resultFilter = @("succeeded","partiallySucceeded"),
        [ValidateSet("all", "cancelling", "completed", "inProgress", "none", "notStarted", "postponed")][string[]] $statusFilter = "completed",
        [string[]] $tagFilters = $null
    )

    if ([string]::IsNullOrWhitespace($organization)) { throw "The `$organization must not be null or whitespace" }
    if ([string]::IsNullOrWhitespace($project)) { throw "The `$project must not be null or whitespace" }

    if ($PsBoundParameters.ContainsKey("statusFilter") -and (-not $PsBoundParameters.ContainsKey("resultFilter"))) {
        $resultFilter = @()
    } elseif ($PsBoundParameters.ContainsKey("resultFilter") -and (-not $PsBoundParameters.ContainsKey("statusFilter"))) {
        $statusFilter = @()
    }

    $url = "https://dev.azure.com/$organization/$project/_apis/build/builds`?api-version=6.0&`$top=5000"
    if ($null -ne $definitionId) {
        $url = "$url&definitions=$($definitionId -join ",")"
    }
    if ([string]::IsNullOrWhiteSpace($branchName) -and ($null -ne $pullRequestId)) {
        $branchName = "refs/pull/$pullRequestId/merge"
    }
    if (-not [string]::IsNullOrWhiteSpace($branchName)) {
        if (-not $branchName.StartsWith("refs/")) {
            $branchName = "refs/heads/$branchName"
        }
        $url = "$url&branchName=$branchName"
    }
    if (-not [string]::IsNullOrWhiteSpace($repo)) {
        $repoValue = Get-Repository -repo $repo -project $project -organization $organization
        $url = "$url&repositoryId=$($repoValue.id)&repositoryType=tfsgit"
    }
    if (-not [string]::IsNullOrWhiteSpace($queryOrder)) {
        $url = "$url&queryOrder=$queryOrder"
    }
    if ($null -ne $maxBuildsPerDefinition) {
        $url = "$url&maxBuildsPerDefinition=$maxBuildsPerDefinition"
    }
    if (($null -ne $resultFilter) -and ($resultFilter.Length -gt 0)) {
        $url = "$url&resultFilter=$($resultFilter -join ",")"
    }
    if (($null -ne $statusFilter) -and ($statusFilter.Length -gt 0)) {
        $url = "$url&statusFilter=$($statusFilter -join ",")"
    }
    if (($null -ne $tagFilters) -and ($tagFilters.Length -gt 0)) {
        $url = "$url&tagFilters=$($tagFilters -join ",")"
    }

    return Invoke-RestMethodWithRetry -method "GET" -uri $url
}
Export-ModuleMember -Function Get-Builds


# https://learn.microsoft.com/rest/api/azure/devops/build/builds/queue?view=azure-devops-rest-5.1
function New-Build {
    [CmdletBinding()]
    param(
        [string]$organization = $script:defaultOrganization,
        [string]$project = $script:defaultProject,
        [int]$definitionId,
        [string]$branch = "master",
        $parameters = @{},
        $templateParameters = @{}
    )

    if ([string]::IsNullOrWhitespace($organization)) { throw "The `$organization must not be null or whitespace" }
    if ([string]::IsNullOrWhitespace($project)) { throw "The `$project must not be null or whitespace" }
    if ($definitionId -le 0) { throw "The `$definitionId must be a positive number" }
    if ([string]::IsNullOrWhitespace($branch)) { throw "The `$branch must not be null or whitespace" }

    if (-not $branch.StartsWith("refs/heads/")) {
        $branch = "refs/heads/$branch"
    }

    $definition = Get-BuildDefinition -organization $organization -project $project -definitionId $definitionId
    return Invoke-RestMethodWithRetry -method "POST" -uri "https://dev.azure.com/$organization/$project/_apis/build/builds`?api-version=6.0" -body @{ 
        definition = $definition;
        demands = @();
        parameters = ($parameters | ConvertTo-Json -Depth 100 -Compress);
        templateParameters = $templateParameters
        project = $definition.project;
        queue = $definition.queue;
        reason = 1; # Manual
        sourceBranch = $branch;
    }
}
Export-ModuleMember -Function New-Build

function Get-BuildTimeline {
    [CmdletBinding()]
    param(
        [string]$organization = $script:defaultOrganization,
        [string]$project = $script:defaultProject,
        [int]$buildId
    )

    if ([string]::IsNullOrWhitespace($organization)) { throw "The `$organization must not be null or whitespace" }
    if ([string]::IsNullOrWhitespace($project)) { throw "The `$project must not be null or whitespace" }
    if ($buildId -le 0) { throw "The `$buildId must be a positive number" }

    return Invoke-RestMethodWithRetry -method "GET" -uri "https://dev.azure.com/$organization/$project/_apis/build/builds/$buildId/Timeline`?api-version=6.0"
}
Export-ModuleMember -Function Get-BuildTimeline

function Wait-Build {
    [CmdletBinding()]
    param(
        [string]$organization = $script:defaultOrganization,
        [string]$project = $script:defaultProject,
        [int]$buildId,
        # Invoked for the current timeline to determine if we should exit before the build is complete
        [ScriptBlock]$shouldReturn = {
            param($build)
            return $false
        },
        [switch]$failIfAnyJobFails,
        [switch]$skipLoggingTaskOutput,
        [int]$waitTime = 5
    )

    if ([string]::IsNullOrWhitespace($organization)) { throw "The `$organization must not be null or whitespace" }
    if ([string]::IsNullOrWhitespace($project)) { throw "The `$project must not be null or whitespace" }
    if ($buildId -le 0) { throw "The `$buildId must be a positive number" }

    $timeline = @{}
    while ($true) {
        $build = Get-Build -organization $organization -project $project -buildId $buildId
        $result = if ($build.PSObject.Properties.Name -eq "result") { $build.result } else { $build.status }
        $now = Get-Date
        $queueTime = [DateTime]$build.queueTime
        if ($build.PSObject.Properties.Name -eq "startTime") {
            $startTime = [DateTime]$build.startTime
            $elapsed = $now - $startTime
            $queueTime = $startTime - $queueTime
        } else {
            $startTime = "TBD"
            $elapsed = "Waiting..."
            $queueTime = $now - $queueTime
        }
        Write-Info "Build $($build.definition.name)($($build.definition.id)) $($build.id) status: $($build.status) | result: $result | queueTime: $queueTime | elapsed: $elapsed"

        # Try to write the timeline/logs to the console
        try {
            $currentTimeline = Get-BuildTimeline -buildId $buildId -organization $organization -project $project
            [array]$tasks = $currentTimeline.records | Where-Object { $_.type -eq "Task" } | Sort-Object { $_.order }
            if ($null -ne $tasks) {
                For ($i = 0; $i -lt $tasks.Length; ++$i) {
                    $task = $tasks[$i]
                    if ($null -eq $task.startTime) {
                        continue
                    }
                    if (-not $timeline.ContainsKey($task.id)) {
                        Write-Section "[$($i + 1)/$($tasks.Length)] $($task.name)" -start
                        $timeline.($task.id) = $task
                    } else {
                        $previousTask = $timeline.($task.id)
                        if ($null -ne $previousTask.finishTime) {
                            continue
                        }
                    }

                    if ($null -ne $task.finishTime) {
                        # Sometimes logs aren't available at this point so they won't get logged to the console, but it's good enough for now.
                        if ($null -ne $task.log -and -not $skipLoggingTaskOutput) {
                            Invoke-RestMethodWithRetry -method "GET" -uri $task.log.url -organization $organization | Write-Host
                            $timeline.($task.id) = $task
                        }
                        Write-Section "[$($i + 1)/$($tasks.Length)] $($task.name) $($task.state) with status $($task.result) after $([DateTime]$task.finishTime - [DateTime]$task.startTime)" -end
                        $timeline.($task.id) = $task
                    }
                }
                if ($null -ne $shouldReturn -and (& $shouldReturn $build $tasks)) {
                    Write-Info "Exiting early because the shouldReturn returned true"
                    return
                }
            }
            if ($failIfAnyJobFails) {
                [array]$failedJobs = $currentTimeline.records | Where-Object { $_.type -eq "Job" -and ($_.result -eq "failed" -or $_.result -eq "canceled") } | Sort-Object { $_.order }
                if ($null -ne $failedJobs -and $failedJobs.Count -ne 0) {
                    throw "Build $($build.definition.name)($($build.definition.id)) $($build.id) failed with $($failedJobs.Count) failed jobs: [$(($failedJobs | ForEach-Object { $_.name }) -join ", ")]"
                }
            }
        } catch {
            # try again next time
        }

        # https://learn.microsoft.com/rest/api/azure/devops/build/builds/get?view=azure-devops-rest-5.1#buildstatus
        switch ($build.status) {
            "completed" {
                # https://learn.microsoft.com/rest/api/azure/devops/build/builds/queue?view=azure-devops-rest-5.1#buildresult
                switch ($build.result) {
                    { $_ -in @("succeeded", "partiallySucceeded") } {
                        return $build
                    }
                    # canceled
                    # failed
                    # none
                    default {
                        throw "Build $($build.definition.name)($($build.definition.id)) $buildId $($build.result)"
                    }
                }
            }
            "inProgress" {
                break
            }
            "notStarted" {
                break
            }
            # cancelling
            # postponed
            # none
            # all
            default {
                throw "Build $($build.definition.name)($($build.definition.id)) $buildId is $($build.status)."
            }
        }
        Start-Sleep -Seconds $waitTime
    }
}
Export-ModuleMember -Function Wait-Build

function Stop-Build {
    [CmdletBinding()]
    param(
        [string]$organization = $script:defaultOrganization,
        [string]$project = $script:defaultProject,
        [int]$buildId
    )

    if ([string]::IsNullOrWhitespace($organization)) { throw "The `$organization must not be null or whitespace" }
    if ([string]::IsNullOrWhitespace($project)) { throw "The `$project must not be null or whitespace" }
    if ($buildId -le 0) { throw "The `$buildId must be a positive number" }

    return Invoke-RestMethodWithRetry -method "PATCH" -uri "https://dev.azure.com/$organization/$project/_apis/build/builds/$buildId`?api-version=6.0" -body @{ status = 4 } -logBody
}
Export-ModuleMember -Function Stop-Build

# https://learn.microsoft.com/rest/api/azure/devops/build/tags/get%20build%20tags?view=azure-devops-rest-5.1
function Get-BuildTags {
    [CmdletBinding()]
    param(
        [string]$organization = $script:defaultOrganization,
        [string]$project = $script:defaultProject,
        [int]$buildId
    )

    if ([string]::IsNullOrWhitespace($organization)) { throw "The `$organization must not be null or whitespace" }
    if ([string]::IsNullOrWhitespace($project)) { throw "The `$project must not be null or whitespace" }
    if ($buildId -le 0) { throw "The `$buildId must be a positive number" }

    return Invoke-RestMethodWithRetry -method "GET" -uri "https://dev.azure.com/$organization/$project/_apis/build/builds/$buildId/tags`?api-version=6.0"
}
Export-ModuleMember -Function Get-BuildTags

# Gets the build artifact
# 1. List out all build artifacts
# 2. Get a specific build artifact
# 3. List the files in a build artifact
# 4. Download a build artifact (full, file, or folder)
# See https://learn.microsoft.com/rest/api/azure/devops/build/artifacts/list?view=azure-devops-rest-5.1 for some details
function Get-BuildArtifact {
    [CmdletBinding()]
    param(
        # The build id
        [Parameter(Mandatory=$true, ParameterSetName="all")]
        [Parameter(Mandatory=$true, ParameterSetName="artifactName")]
        [Parameter(Mandatory=$true, ParameterSetName="listFiles_artifactName")]
        [Parameter(Mandatory=$true, ParameterSetName="listFiles_artifact")]
        [Parameter(Mandatory=$true, ParameterSetName="full_artifactName")]
        [Parameter(Mandatory=$true, ParameterSetName="full_artifact")]
        [Parameter(Mandatory=$true, ParameterSetName="subFolder_artifactName")]
        [Parameter(Mandatory=$true, ParameterSetName="subFolder_artifact")]
        [Parameter(Mandatory=$true, ParameterSetName="file_outDir_artifactName")]
        [Parameter(Mandatory=$true, ParameterSetName="file_outDir_artifact")]
        [Parameter(Mandatory=$true, ParameterSetName="file_outFile_artifactName")]
        [Parameter(Mandatory=$true, ParameterSetName="file_outFile_artifact")]
        [Parameter(Mandatory=$true, ParameterSetName="matching_artifactName")]
        [Parameter(Mandatory=$true, ParameterSetName="matching_artifact")]
        [Parameter(Mandatory=$true, ParameterSetName="matching_preprocess_artifactName")]
        [Parameter(Mandatory=$true, ParameterSetName="matching_preprocess_artifact")]
        [int]$buildId,

        # The artifact name
        [Parameter(Mandatory=$true, ParameterSetName="artifactName")]
        [Parameter(Mandatory=$true, ParameterSetName="listFiles_artifactName")]
        [Parameter(Mandatory=$true, ParameterSetName="full_artifactName")]
        [Parameter(Mandatory=$true, ParameterSetName="subFolder_artifactName")]
        [Parameter(Mandatory=$true, ParameterSetName="file_outDir_artifactName")]
        [Parameter(Mandatory=$true, ParameterSetName="file_outFile_artifactName")]
        [Parameter(Mandatory=$true, ParameterSetName="matching_artifactName")]
        [Parameter(Mandatory=$true, ParameterSetName="matching_preprocess_artifactName")]
        [string]$artifactName,

        # The artifact - use this to prevent multiple requests to fetch the artifact
        [Parameter(Mandatory=$true, ParameterSetName="listFiles_artifact")]
        [Parameter(Mandatory=$true, ParameterSetName="full_artifact")]
        [Parameter(Mandatory=$true, ParameterSetName="subFolder_artifact")]
        [Parameter(Mandatory=$true, ParameterSetName="file_outDir_artifact")]
        [Parameter(Mandatory=$true, ParameterSetName="file_outFile_artifact")]
        [Parameter(Mandatory=$true, ParameterSetName="matching_artifact")]
        [Parameter(Mandatory=$true, ParameterSetName="matching_preprocess_artifact")]
        $artifact,

        # Whether to fetch the artifact's file hierarchy
        [Parameter(Mandatory=$true, ParameterSetName="listFiles_artifactName")]
        [Parameter(Mandatory=$true, ParameterSetName="listFiles_artifact")]
        [switch]$listFiles,

        # Whether to return a flat list of the files
        [Parameter(Mandatory=$false, ParameterSetName="listFiles_artifactName")]
        [Parameter(Mandatory=$false, ParameterSetName="listFiles_artifact")]
        [switch]$flat,

        # Whether to include directories when returning a flat list of the files
        [Parameter(Mandatory=$false, ParameterSetName="listFiles_artifactName")]
        [Parameter(Mandatory=$false, ParameterSetName="listFiles_artifact")]
        [switch]$includeDirectories,

        # Whether to return the raw file list
        [Parameter(Mandatory=$false, ParameterSetName="listFiles_artifactName")]
        [Parameter(Mandatory=$false, ParameterSetName="listFiles_artifact")]
        [switch]$raw,

        # The output directory
        [Parameter(Mandatory=$true, ParameterSetName="full_artifactName")]
        [Parameter(Mandatory=$true, ParameterSetName="full_artifact")]
        [Parameter(Mandatory=$true, ParameterSetName="subFolder_artifactName")]
        [Parameter(Mandatory=$true, ParameterSetName="subFolder_artifact")]
        [Parameter(Mandatory=$true, ParameterSetName="matching_artifactName")]
        [Parameter(Mandatory=$true, ParameterSetName="matching_artifact")]
        [Parameter(Mandatory=$true, ParameterSetName="matching_preprocess_artifactName")]
        [Parameter(Mandatory=$true, ParameterSetName="matching_preprocess_artifact")]
        [Parameter(Mandatory=$true, ParameterSetName="file_outDir_artifactName")]
        [Parameter(Mandatory=$true, ParameterSetName="file_outDir_artifact")]
        [string]$outDir,

        # The artifact sub-folder to download
        [Parameter(Mandatory=$true, ParameterSetName="subFolder_artifactName")]
        [Parameter(Mandatory=$true, ParameterSetName="subFolder_artifact")]
        [string]$subFolder,

        # Item filters - regex
        [Parameter(Mandatory=$true, ParameterSetName="matching_artifactName")]
        [Parameter(Mandatory=$true, ParameterSetName="matching_artifact")]
        [Parameter(Mandatory=$true, ParameterSetName="matching_preprocess_artifactName")]
        [Parameter(Mandatory=$true, ParameterSetName="matching_preprocess_artifact")]
        [string[]]$matching,

        # If specified, the output directory will not be deleted before the artifacts are downloaded
        [Parameter(Mandatory=$false, ParameterSetName="matching_artifactName")]
        [Parameter(Mandatory=$false, ParameterSetName="matching_artifact")]
        [Parameter(Mandatory=$false, ParameterSetName="matching_preprocess_artifactName")]
        [Parameter(Mandatory=$false, ParameterSetName="matching_preprocess_artifact")]
        [switch]$combine,

        # The artifact file to download
        [Parameter(Mandatory=$true, ParameterSetName="file_outDir_artifactName")]
        [Parameter(Mandatory=$true, ParameterSetName="file_outDir_artifact")]
        [Parameter(Mandatory=$true, ParameterSetName="file_outFile_artifactName")]
        [Parameter(Mandatory=$true, ParameterSetName="file_outFile_artifact")]
        [string]$file,

        # The output file
        [Parameter(Mandatory=$true, ParameterSetName="file_outFile_artifactName")]
        [Parameter(Mandatory=$true, ParameterSetName="file_outFile_artifact")]
        [string]$outFile,

        # The Azure DevOps organization
        [string]$organization = $script:defaultOrganization,

        # The Azure DevOps organization project
        [string]$project = $script:defaultProject,

        # Keeps any temporary files instead of deleting them
        [Parameter(Mandatory=$false, ParameterSetName="full_artifactName")]
        [Parameter(Mandatory=$false, ParameterSetName="full_artifact")]
        [Parameter(Mandatory=$false, ParameterSetName="subFolder_artifactName")]
        [Parameter(Mandatory=$false, ParameterSetName="subFolder_artifact")]
        [Parameter(Mandatory=$false, ParameterSetName="matching_artifactName")]
        [Parameter(Mandatory=$false, ParameterSetName="matching_artifact")]
        [Parameter(Mandatory=$false, ParameterSetName="matching_preprocess_artifactName")]
        [Parameter(Mandatory=$false, ParameterSetName="matching_preprocess_artifact")]
        [Parameter(Mandatory=$false, ParameterSetName="file_outDir_artifactName")]
        [Parameter(Mandatory=$false, ParameterSetName="file_outDir_artifact")]
        [switch]$keep,

        # Pre-process the calls to download the files
        [Parameter(Mandatory=$true, ParameterSetName="matching_preprocess_artifactName")]
        [Parameter(Mandatory=$true, ParameterSetName="matching_preprocess_artifact")]
        [switch]$preprocess
    )

    if ([string]::IsNullOrWhitespace($organization)) { throw "The `$organization must not be null or whitespace" }
    if ([string]::IsNullOrWhitespace($project)) { throw "The `$project must not be null or whitespace" }
    if ($buildId -le 0) { throw "The `$buildId must be a positive number" }

    if (@("all", "artifactName") -eq $PsCmdlet.ParameterSetName) {
        $url = "https://dev.azure.com/$organization/$project/_apis/build/builds/$buildId/artifacts"
        if ($PsBoundParameters.ContainsKey("artifactName")) {
            $url += "?artifactName=$artifactName"
        }
        return Invoke-RestMethodWithRetry -method "GET" -uri $url
    }

    if (-not $PsBoundParameters.ContainsKey("artifact")) {
        $artifact = Get-BuildArtifact -buildId $buildId -artifactName $artifactName -organization:$organization -project:$project
    }

    $artifactProviderKey = "ms.vss-build-web.run-artifacts-data-provider"
    switch ($PsCmdlet.ParameterSetName) {
        {@("listFiles_artifactName", "listFiles_artifact") -eq $_} {
            $response = Invoke-RestMethodWithRetry -method "POST" -uri "https://dev.azure.com/$organization/_apis/Contribution/HierarchyQuery/project/$project`?api-version=6.0-preview" -body @{
                "contributionIds" = @($artifactProviderKey);
                "dataProviderContext" = @{
                    "properties" = @{
                        "artifactId" = $artifact.Id;
                        "buildId" = $buildId;
                        "sourcePage" = @{
                            "url" = "https://dev.azure.com/$organization/$project/_build/results?buildId=$buildId&view=artifacts&pathAsName=false&type=publishedArtifacts";
                            "routeId" = "ms.vss-build-web.ci-results-hub-route";
                            "routeValues" = @{
                                "project" = $project;
                                "viewname" = "build-results";
                                "controller" = "ContributedPage";
                                "action" = "Execute";
                            }
                        }
                    }
                }
            } -logBody
            if ($raw) {
                return $response
            }
            
            if (($null -eq $response.dataProviders) -or ($null -eq ($response.dataProviders.PSObject.Properties | Where-Object { $_.Name -eq $artifactProviderKey }))) {
                return @()
            }
            $items = $response.dataProviders.$artifactProviderKey.items
            if (-not $flat) {
                return $items
            }

            # Use a loop to reduce recursion depth
            $flatFiles = @()
            [array]$toProcess = $items
            while (($null -ne $toProcess) -and ($toProcess.Count -gt 0)) {
                $item,[array]$toProcess = $toProcess
                switch ($item.type) {
                    "file" {
                        $flatFiles += $item.sourcePath
                    }
                    "directory" {
                        if ($includeDirectories) {
                            $flatFiles += $item.sourcePath
                        }
                        if ($null -ne $item.items) {
                            $toProcess += $item.items
                        }
                    }
                }
            }
            return $flatFiles | Sort-Object
        }
        {@("full_artifactName", "full_artifact", "file_outDir_artifactName", "file_outDir_artifact", "file_outFile_artifactName", "file_outFile_artifact", "subFolder_artifactName", "subFolder_artifact") -eq $_} {
            $isFile = $PsBoundParameters.ContainsKey("file")
            if ($PsBoundParameters.ContainsKey("outDir")) {
                $null = New-Item $outDir -ItemType Directory -Force -ErrorAction SilentlyContinue
            } else {
                $null = New-Item (Split-Path $outFile) -ItemType Directory -Force -ErrorAction SilentlyContinue
            }
            
            if ($PsCmdlet.ParameterSetName -match "full_") {
                $downloadUrl = $artifact.resource.downloadUrl
            } else {
                $itemPath = if ($isFile) { $file } else { $subFolder }
                $details = Invoke-RestMethodWithRetry -method "POST" -uri "https://dev.azure.com/$organization/_apis/Contribution/HierarchyQuery/project/$project`?api-version=6.0-preview" -body @{
                    "contributionIds" = @("ms.vss-build-web.run-artifacts-download-data-provider");
                    "dataProviderContext" = @{
                        "properties" = @{
                            "artifactId" = $artifact.Id;
                            "buildId" = $buildId;
                            "compressDownload" = (-not $isFile);
                            "path" = $itemPath;
                            "saveAbsolutePath" = $true;
                            "sourcePage" = @{
                                "url" = "https://dev.azure.com/$organization/$project/_build/results?buildId=$buildId&view=artifacts&pathAsName=false&type=publishedArtifacts";
                                "routeId" = "ms.vss-build-web.ci-results-hub-route";
                                "routeValues" = @{
                                    "project" = $project;
                                    "viewname" = "build-results";
                                    "controller" = "ContributedPage";
                                    "action" = "Execute";
                                }
                            }
                        }
                    }
                } -logBody
                
                $downloadUrl = $details.dataProviders.'ms.vss-build-web.run-artifacts-download-data-provider'.downloadUrl
            }

            if ($PsBoundParameters.ContainsKey("outFile")) {
                $outputPath = $outFile
            } elseif ($isFile) {
                $outputPath = "$outDir\$($artifact.name)$file"
                $null = New-Item (Split-Path $outputPath) -ItemType Directory -Force -ErrorAction SilentlyContinue
            } else {
                $outputPath = "$env:Temp\ocDlTemp_${buildId}_$([Guid]::NewGuid()).zip"
            }
            
            $response = Invoke-RestMethodWithRetry -method "Get" -uri $downloadUrl -outFile $outputPath -organization $organization
            if ((-not $PsBoundParameters.ContainsKey("outFile")) -and (-not $isFile)) {
                Write-Command "[System.IO.Compression.ZipFile]::ExtractToDirectory(`"$outputPath`", `"$outDir`")"
                [System.IO.Compression.ZipFile]::ExtractToDirectory($outputPath, $outDir)
                if (-not $keep) {
                    $null = Remove-Item $outputPath -Force -ErrorAction SilentlyContinue
                }
            }
        }
        {@("matching_artifactName", "matching_artifact", "matching_preprocess_artifactName", "matching_preprocess_artifact") -eq $_} {
            Write-Info "Downloading build $buildId artifact $($artifact.name) ($($artifact.resource.type)) files matching $matching to $outDir"
            if ((-not $combine) -and (Test-Path $outDir)) {
                $null = Remove-Item $outDir -Force -Recurse -ErrorAction SilentlyContinue
            }
            
            [array]$files = Get-BuildArtifact -buildId $buildId -artifact $artifact -listFiles -organization $organization -project $project

            # Use a loop to reduce recursion depth
            $toDownload = [System.Collections.ArrayList]::new()
            [array]$allDirectories = @()
            [array]$toProcess = $files
            while (($null -ne $toProcess) -and ($toProcess.Count -gt 0)) {
                $item,[array]$toProcess = $toProcess
                if ($null -eq $item) {
                    continue
                }
                switch ($item.type) {
                    "file" {
                        if ($null -ne ($matching | Where-Object { $item.sourcePath -match $_ } | Select-Object -First 1)) {
                            $null = $toDownload.Add($item)
                        }
                    }
                    "directory" {
                        if ($null -ne $item.items) {
                            $toProcess += $item.items
                        }
                        $allDirectories += $item
                    }
                }
            }
            $toProcess = $null
            # If we're going to download all items in a folder, download the folder directly
            [array]::Reverse($allDirectories)
            forEach ($directory in $allDirectories) {
                $all = $true
                forEach ($item in $directory.items) {
                    if (-not $toDownload.Contains($item)) {
                        $all = $false
                        break
                    }
                }
                if ($all) {
                    forEach ($item in $directory.items) {
                        $toDownload.Remove($item)
                    }
                    $null = $toDownload.Add($directory)
                }
            }
            $allDirectories = $null
            $directory = $null

            if (($null -eq $toDownload) -or
                ($toDownload.Count -eq 0) -or
                ($null -eq ($toDownload | Where-Object { ($_.type -eq "file") -or ($null -ne $_.items) } | Select-Object -First 1))
            ) {
                Write-PipelineIssue -warningMessage "Found no files matching $matching for artifact $($artifact.name) of build $buildId"
                return
            }

            $invocations = @()
            if (($null -ne $toDownload) -and ($null -eq (Compare-Object -ReferenceObject $toDownload -DifferenceObject $files))) {
                $invocations += @{
                    buildId = $buildId
                    artifact = $artifact
                    outDir = $outDir
                    organization = $organization
                    project = $project
                    keep = $keep
                }
            } else {
                ForEach ($item in $toDownload) {
                    $skip = $false
                    $invocation = @{
                        buildId = $buildId
                        artifact = $artifact
                        outDir = $outDir
                        organization = $organization
                        project = $project
                        keep = $keep
                    }
                    switch ($item.type) {
                        "file" {
                            $invocation.file = $item.sourcePath
                        }
                        "directory" {
                            if ($null -eq $item.items) {
                                # Skip empty directories
                                $skip = $true
                                continue # This isn't continuing the ForEach loop, so we'll keep track of whether to skip in a variable
                            }
                            $invocation.subFolder = $item.sourcePath
                        }
                    }
                    if (-not $skip) {
                        $invocations += $invocation
                    }
                }
            }
            # Free up memory - we're seeing out of memory exceptions on the pipeline
            $files = $null
            $toDownload = $null
            $item = $null

            if ($PsCmdlet.ParameterSetName -match "preprocess") {
                return $invocations
            }

            ForEach ($invocation in $invocations) {
                Get-BuildArtifact @invocation
            }
        }
        default {
            throw "Unknown ParameterSetName $_"
        }
    }
}
Export-ModuleMember -Function Get-BuildArtifact

# Re-runs a build's failed phases
function Repair-BuildFailedStages {
    [CmdletBinding()]
    param(
        [string]$organization = $script:defaultOrganization,
        [string]$project = $script:defaultProject,
        [int]$buildId
    )

    if ([string]::IsNullOrWhitespace($organization)) { throw "The `$organization must not be null or whitespace" }
    if ([string]::IsNullOrWhitespace($project)) { throw "The `$project must not be null or whitespace" }
    if ($buildId -le 0) { throw "The `$buildId must be a positive number" }

    return Invoke-RestMethodWithRetry -method "PATCH" -uri "https://dev.azure.com/$organization/$project/_apis/build/builds/$buildId`?retry=true&api-version=6.0"
}
Export-ModuleMember -Function Repair-BuildFailedStages

# See https://learn.microsoft.com/rest/api/azure/devops/pipelines/pipelines/get?view=azure-devops-rest-6.0
function Get-Pipeline {
    [CmdletBinding()]
    param(
        [int]$pipelineId,

        [string]$organization = $script:defaultOrganization,
        [string]$project = $script:defaultProject
    )

    if ([string]::IsNullOrWhitespace($organization)) { throw "The `$organization must not be null or whitespace" }
    if ([string]::IsNullOrWhitespace($project)) { throw "The `$project must not be null or whitespace" }
    if ($pipelineId -le 0) { throw "The `$pipelineId must be a positive number" }

    return Invoke-RestMethodWithRetry -method "Get" -uri "https://dev.azure.com/$organization/$project/_apis/pipelines/$pipelineId`?api-version=6.0-preview.1"
}
Export-ModuleMember -Function Get-Pipeline

# See https://learn.microsoft.com/rest/api/azure/devops/pipelines/runs/run%20pipeline?view=azure-devops-rest-7.1
function New-PipelineRun {
    [CmdletBinding()]
    param(
        [int]$pipelineId,
        [switch]$previewRun,
        [string]$branch,
        [hashtable]$pipelineVersions = @{},
        [object]$templateParameters = $null,
        [object]$variables = $null,
        [string[]]$stagesToSkip = @(),

        [string]$organization = $script:defaultOrganization,
        [string]$project = $script:defaultProject
    )

    if ([string]::IsNullOrWhitespace($organization)) { throw "The `$organization must not be null or whitespace" }
    if ([string]::IsNullOrWhitespace($project)) { throw "The `$project must not be null or whitespace" }
    if ($pipelineId -le 0) { throw "The `$pipelineId must be a positive number" }

    $body = @{
        previewRun = ($previewRun.IsPresent)
        resources = $null
        templateParameters = $templateParameters
        variables = $variables
        stagesToSkip = $stagesToSkip
    }

    $resources = @{}

    # Handle repositories
    if (-not [string]::IsNullOrWhiteSpace($branch)) {
        if (-not $branch.StartsWith("refs/")) {
            $branch = "refs/heads/$branch"
        }
        $resources["repositories"] = @{
            self = @{
                refName = $branch
            }
        }
    }

    # Handle pipeline versions
    if ($pipelineVersions.Count -gt 0) {
        $pipelines = @{}
        foreach ($pipelineAlias in $pipelineVersions.Keys) {
            $pipelines[$pipelineAlias] = @{
                version = $pipelineVersions[$pipelineAlias]
            }
        }
        $resources["pipelines"] = $pipelines
    }

    # Set resources in body if any were defined
    if ($resources.Count -gt 0) {
        $body.resources = $resources
    }

    return Invoke-RestMethodWithRetry -method "POST" -uri "https://dev.azure.com/$organization/$project/_apis/pipelines/$pipelineId/runs?api-version=7.1" -body $body -logBody -throwOriginalError:$previewRun
}
Export-ModuleMember -Function New-PipelineRun

# Validate a pipeline's yaml
# See https://learn.microsoft.com/rest/api/azure/devops/pipelines/runs/run%20pipeline?view=azure-devops-rest-6.0
function Test-PipelineYaml {
    [CmdletBinding()]
    param(
        [string]$organization = $script:defaultOrganization,
        [string]$project = $script:defaultProject,
        [int]$pipelineId
    )
    New-PipelineRun -pipelineId $pipelineId -previewRun
}
Export-ModuleMember -Function Test-PipelineYaml

# See https://learn.microsoft.com/rest/api/azure/devops/release/definitions/get?view=azure-devops-rest-5.1
function Get-ReleaseDefinition {
    [CmdletBinding()]
    param(
        [nullable[int]]$definitionId,
        [string]$organization = $script:defaultOrganization,
        [string]$project = $script:defaultProject
    )

    if ([string]::IsNullOrWhitespace($organization)) { throw "The `$organization must not be null or whitespace" }
    if ([string]::IsNullOrWhitespace($project)) { throw "The `$project must not be null or whitespace" }
    if ($null -ne $definitionId) {
        return Invoke-RestMethodWithRetry -method "GET" -uri "https://vsrm.dev.azure.com/$organization/$project/_apis/release/definitions/$definitionId`?api-version=6.0"
     } else {
        return Invoke-RestMethodWithRetry -method "GET" -uri "https://vsrm.dev.azure.com/$organization/$project/_apis/release/definitions?api-version=6.0"
     }
}
Export-ModuleMember -Function Get-ReleaseDefinition

# See https://learn.microsoft.com/rest/api/azure/devops/release/definitions/update?view=azure-devops-rest-5.1
function Update-ReleaseDefinition {
    [CmdletBinding()]
    param(
        [string]$organization = $script:defaultOrganization,
        [string]$project = $script:defaultProject,
        $body,
        [string]$comment
    )

    if ([string]::IsNullOrWhitespace($organization)) { throw "The `$organization must not be null or whitespace" }
    if ([string]::IsNullOrWhitespace($project)) { throw "The `$project must not be null or whitespace" }
    if ($null -eq $body) { throw "The `$body must not be null" }

    if (($body -isnot [string]) -and (-not [string]::IsNullOrWhiteSpace($comment))) {
        if ([bool]($body.PSObject.Properties.Name -eq "comment")) {
            $body.comment = $comment
        } else {
            $body | Add-Member -MemberType NoteProperty -Name "comment" -Value $comment
        }
    }
    return Invoke-RestMethodWithRetry -method "PUT" -uri "https://vsrm.dev.azure.com/$organization/$project/_apis/release/definitions`?api-version=6.0" -body $body
}
Export-ModuleMember -Function Update-ReleaseDefinition

# See https://learn.microsoft.com/rest/api/azure/devops/release/releases/get%20release?view=azure-devops-rest-5.1
function Get-Release {
    [CmdletBinding()]
    param(
        # The release id
        [int]$releaseId = $script:defaultReleaseId,

        [string]$organization = $script:defaultOrganization,
        [string]$project = $script:defaultProject
    )

    if ([string]::IsNullOrWhitespace($organization)) { throw "The `$organization must not be null or whitespace" }
    if ([string]::IsNullOrWhitespace($project)) { throw "The `$project must not be null or whitespace" }
    if ($releaseId -le 0) { throw "The `$releaseId must be a positive number" }

    return Invoke-RestMethodWithRetry -method "Get" -uri "https://vsrm.dev.azure.com/$organization/$project/_apis/release/releases/$releaseId`?api-version=6.0"
}
Export-ModuleMember -Function Get-Release

# See https://learn.microsoft.com/rest/api/azure/devops/release/releases/list?view=azure-devops-rest-6.0
function Get-Releases {
    [CmdletBinding()]
    param(
        [nullable[int]]$releaseDefinitionId,
        
        # See https://learn.microsoft.com/rest/api/azure/devops/release/releases/list?view=azure-devops-rest-6.0#releasestatus
        [ValidateSet("abandoned", "active", "draft", "undefined")]
        [string]$statusFilter = "active",
        
        [string]$branchFilter,
        [nullable[int]]$pullRequestId,
        [nullable[int]]$buildId,

        [string]$organization = $script:defaultOrganization,
        [string]$project = $script:defaultProject
    )

    if ([string]::IsNullOrWhitespace($organization)) { throw "The `$organization must not be null or whitespace" }
    if ([string]::IsNullOrWhitespace($project)) { throw "The `$project must not be null or whitespace" }

    $url = "https://vsrm.dev.azure.com/$organization/$project/_apis/release/releases`?api-version=6.0-preview"

    if ($null -ne $releaseDefinitionId) {
        $url = "$url&definitionId=$releaseDefinitionId"
    }

    if (-not [string]::IsNullOrWhiteSpace($statusFilter)) {
        $url = "$url&statusFilter=$statusFilter"
    }

    if ([string]::IsNullOrWhiteSpace($branchFilter) -and ($null -ne $pullRequestId)) {
        $branchFilter = "refs/pull/$pullRequestId/merge"
    }
    if ($null -ne $buildId) {
        $url = "$url&artifactVersionId=$buildId"
    }
    if (-not [string]::IsNullOrWhiteSpace($branchFilter)) {
        if (-not $branchFilter.StartsWith("refs/")) {
            $branchFilter = "refs/heads/$branchFilter"
        }
        $url = "$url&sourceBranchFilter=$branchFilter"
    }

    return Invoke-RestMethodWithRetry -method "Get" -uri $url
}
Export-ModuleMember -Function Get-Releases

# not currently documented
function Get-ReleaseArtifactVersions {
    param(
        [string] $organization = $script:defaultOrganization,
        [string] $project = $script:defaultProject,
        [int] $definitionId = -1,
        [string] $description
    )
    if ([string]::IsNullOrWhitespace($organization)) { throw "The `$organization must not be null or whitespace" }
    if ([string]::IsNullOrWhitespace($project)) { throw "The `$project must not be null or whitespace" }
    if ($definitionId -eq -1) { throw "The `$definitionId must be set" }

    Invoke-RestMethodWithRetry -method "Get" -uri "https://vsrm.dev.azure.com/$organization/$project/_apis/release/artifacts/versions?releaseDefinitionId=$definitionId&api-version=5.1-preview"
}
Export-ModuleMember -Function Get-ReleaseArtifactVersions

# See https://learn.microsoft.com/rest/api/azure/devops/release/releases/create?view=azure-devops-rest-5.1
function New-Release {
    param(
        [string] $organization = $script:defaultOrganization,
        [string] $project = $script:defaultProject,
        [int] $definitionId = -1,
        [string] $description = "",
        $artifacts,
        $variables = @{},
        [ValidateSet("continuousIntegration", "manual", "none", "pullRequest", "schedule")][string] $reason = "manual"
    )
    if ([string]::IsNullOrWhitespace($organization)) { throw "The `$organization must not be null or whitespace" }
    if ([string]::IsNullOrWhitespace($project)) { throw "The `$project must not be null or whitespace" }
    if ($definitionId -eq -1) { throw "The `$definitionId must be set" }
    if ($null -eq $artifacts) { throw "The `$artifacts must be set" }
    if ([string]::IsNullOrWhitespace($reason)) { throw "The `$reason must not be null or whitespace" }
    if ($null -eq $variables) { throw "The `$variables must be set" }

    $variablesValue = @{}
    ForEach ($key in $variables.Keys) {
        $variablesValue.$key = @{
            value = $variables.$key
        }
    }
    return Invoke-RestMethodWithRetry -method "POST" -uri "https://vsrm.dev.azure.com/$organization/$project/_apis/release/releases`?api-version=5.1" -body @{
        definitionId = $definitionId;
        isDraft = $false;
        description = $description;
        artifacts = $artifacts;
        reason = $reason;
        variables = $variablesValue;
    }
}
Export-ModuleMember -Function New-Release

function Wait-Release {
    [CmdletBinding()]
    param(
        [int] $releaseId = -1,
        [ScriptBlock]$shouldReturn = {
            param($release)
            return $false
        },
        
        [string] $organization = $script:defaultOrganization,
        [string] $project = $script:defaultProject,
        [int] $sleepInterval = 60
    )
    if ([string]::IsNullOrWhitespace($organization)) { throw "The `$organization must not be null or whitespace" }
    if ([string]::IsNullOrWhitespace($project)) { throw "The `$project must not be null or whitespace" }
    if ($releaseId -le 0) { throw "The `$releaseId must be a positive number" }

    $lastReleaseJson = $null
    $then = [DateTime]::UtcNow

    while ($true) {
        $release = Get-Release -releaseId $releaseId -organization $organization -project $project
        if ($null -eq $release) {
            throw "Release $releaseId not found"
        }
        $identifier = "Release $($release.releaseDefinition.name) $($($release.name)) ($($release.id)) (rev:$($release.releaseDefinitionRevision)-$($release.modifiedOn))"
        Write-Info "Release $($release.id) is in state $($release.status)"
        $currentReleaseJson = $release | ConvertTo-Json -Depth 100
        if ($currentReleaseJson -ne $lastReleaseJson) {
            Write-Info "$identifier updated:`n$currentReleaseJson"
            $lastReleaseJson = $currentReleaseJson
        }
        $done = $true
        forEach ($environment in $release.environments) {
            # https://learn.microsoft.com/rest/api/azure/devops/release/releases/get-release?view=azure-devops-rest-7.1&tabs=HTTP#environmentstatus
            Write-Info "$identifier environment $($environment.name) $($environment.status)"
            switch ($environment.status) {
                "inProgress" { $done = $false }
                "notStarted" { $done = $false }
                "queued"     { $done = $false }
                "scheduled"  { $done = $false }
                "partiallySucceeded" { Set-TaskCompletion "SucceededWithIssues" }
                "succeeded" { }
                "rejected"  { throw "$identifier environment $($environment.name) $($environment.status)" }
                "canceled"  { throw "$identifier environment $($environment.name) $($environment.status)" }
                "undefined" { throw "$identifier environment $($environment.name) $($environment.status)" }
                default     { throw "$identifier environment $($environment.name) $($environment.status)" }
            }
        }
        if ($done) {
            Write-Info "$identifier completed"
            return
        }
        if ($null -ne $shouldReturn -and (& $shouldReturn $release)) {
            Write-Info "Exiting early because the shouldReturn returned true"
            return
        }
        Write-Info "$([DateTime]::UtcNow - $then) elapsed waiting for $identifier to finish"
        Start-Sleep -Seconds $sleepInterval
    }
}
Export-ModuleMember -Function Wait-Release

# Not documented
function Get-CloudVaultBranches {
    [CmdletBinding()]
    param(
        [string] $organization = $script:defaultOrganization,
        [string] $project = $script:defaultProject,
        [string] $serviceTreeId,
        [string] $serviceEndpoint,
        [string] $branchFilter = "crm.omnichannel"
    )
    if ([string]::IsNullOrWhitespace($organization)) { throw "The `$organization must not be null or whitespace" }
    if ([string]::IsNullOrWhitespace($project)) { throw "The `$project must not be null or whitespace" }
    if ([string]::IsNullOrWhitespace($serviceTreeId)) { throw "The `$serviceTreeId must not be null or whitespace" }
    if ([string]::IsNullOrWhitespace($serviceEndpoint)) { throw "The `$serviceEndpoint must not be null or whitespace" }
    if ([string]::IsNullOrWhitespace($branchFilter)) { throw "The `$branchFilter must not be null or whitespace" }

    $response = Invoke-RestMethodWithRetry -method "Post" -uri "https://dev.azure.com/$organization/$project/_apis/serviceendpoint/endpointproxy?endpointId=$serviceEndpoint&api-version=5.1-preview" -body @{
        dataSourceDetails = @{
            dataSourceName = "";
            dataSourceUrl = "{{endpoint.url}}/BuildVaults/{{endpoint.vaultname}}/Branches?metadataFilter={{endpoint.globalMetadata}};{{localMetadata}};servicetreeid:{{definition}};&branchFilters={{branchFilters}}";
            headers = @(@{
                    name = "";
                    value = ""
                }
            );
            initialContextTemplate = "";
            parameters = @{
                connection = $serviceEndpoint;
                definition = $serviceTreeId;
                branchFilters = $branchFilter;
                localmetadata = ""
            };
            requestContent = "";
            requestVerb = "";
            resourceUrl = "";
            resultSelector = "jsonpath: $[*]"
        };
        resultTransformationDetails = @{
            callbackContextTemplate = "";
            callbackRequiredTemplate = "";
            resultTemplate = "{ Value : `"{{branchname}}`", DisplayValue : `"{{branchname}}`" }"
        };
        "serviceEndpointDetails" = $null
    }
    return $response.result
}
Export-ModuleMember Get-CloudVaultBranches

# Not documented
function Get-CloudVaultVersions {
    [CmdletBinding()]
    param(
        [string] $serviceTreeId,
        [string] $serviceEndpoint,
        [string] $branch,
        
        [string] $organization = $script:defaultOrganization,
        [string] $project = $script:defaultProject
    )
    if ([string]::IsNullOrWhitespace($organization)) { throw "The `$organization must not be null or whitespace" }
    if ([string]::IsNullOrWhitespace($project)) { throw "The `$project must not be null or whitespace" }
    if ([string]::IsNullOrWhitespace($serviceTreeId)) { throw "The `$serviceTreeId must not be null or whitespace" }
    if ([string]::IsNullOrWhitespace($serviceEndpoint)) { throw "The `$serviceEndpoint must not be null or whitespace" }
    if ([string]::IsNullOrWhitespace($branch)) { throw "The `$branch must not be null or whitespace" }

    return Invoke-RestMethodWithRetry -method "Post" -uri "https://dev.azure.com/$organization/$project/_apis/serviceendpoint/endpointproxy?endpointId=$serviceEndpoint&api-version=5.1-preview" -body @{
        dataSourceDetails = @{
            dataSourceName = "";
            dataSourceUrl = "{{endpoint.url}}/BuildVaults/{{endpoint.vaultname}}/Versions?metadataFilter={{endpoint.globalMetadata}};{{localMetadata}};servicetreeid:{{definition}};branch:{{branch}}";
            headers = @(@{
                    name = "";
                    value = ""
                }
            );
            initialContextTemplate = "";
            parameters = @{
                connection = $serviceEndpoint;
                definition = $serviceTreeId;
                branch = $branch;
                localmetadata = ""
            };
            requestContent = "";
            requestVerb = "";
            resourceUrl = "";
            resultSelector = "jsonpath: $[*]"
        };
        resultTransformationDetails = @{
            callbackContextTemplate = "";
            callbackRequiredTemplate = "";
            resultTemplate = "{ Value : `"{{artifactId}}`", DisplayValue : `"{{#if isBuildNumberInMultipleArtifacts}}{{buildnumber}} ({{artifactId}}){{else}}{{buildnumber}}{{/if}}`" }"
        };
        "serviceEndpointDetails" = $null
    }
}
Export-ModuleMember Get-CloudVaultVersions

# See https://learn.microsoft.com/rest/api/azure/devops/release/releases/update%20release?view=azure-devops-rest-5.1
function Update-Release {
    [CmdletBinding()]
    param(
        [string]$organization = $script:defaultOrganization,
        [string]$project = $script:defaultProject,
        [int]$releaseId = $script:defaultReleaseId,
        $content = $null,
    
        [ValidateSet("error","warning")]
        [string]$failureType = "error"
    )

    if ([string]::IsNullOrWhitespace($organization)) { throw "The `$organization must not be null or whitespace" }
    if ([string]::IsNullOrWhitespace($project)) { throw "The `$project must not be null or whitespace" }
    if ($releaseId -le 0) { throw "The `$releaseId must be a positive number" }
    if ($null -eq $content) { throw "The `$content must be set" }

    return Invoke-RestMethodWithRetry -method "PUT" -uri "https://vsrm.dev.azure.com/$organization/$project/_apis/release/releases/$releaseId`?api-version=6.0" -body $content
}
Export-ModuleMember -Function Update-Release

function Start-ReleaseStage {
    [CmdletBinding()]
    param(
        [string]$organization = $script:defaultOrganization,
        [string]$project = $script:defaultProject,
        [int]$releaseId,
        [int]$environmentId
    )

    if ([string]::IsNullOrWhitespace($organization)) { throw "The `$organization must not be null or whitespace" }
    if ([string]::IsNullOrWhitespace($project)) { throw "The `$project must not be null or whitespace" }
    if ($releaseId -le 0) { throw "The `$releaseId must be a positive number" }
    if ($environmentId -le 0) { throw "The `$environmentId must be a positive number" }
    
    return Invoke-RestMethodWithRetry -method "PATCH" -uri "https://vsrm.dev.azure.com/$organization/$project/_apis/release/releases/$releaseId/environments/$environmentId`?api-version=5.1-preview.1" -body @{
        comment = "";
        scheduledDeploymentTime = $null;
        status = 2;
    }
}
Export-ModuleMember -Function Start-ReleaseStage

function Get-ReleasePersistentVariableName {
    [CmdletBinding()]
    param(
        [string] $name,
        [bool]   $stage,
        [bool]   $attempt,
        [bool]   $phase,
        [bool]   $job
    )

    $parts = @()
    if ($stage) {
        $parts += "Stage",$env:RELEASE_ENVIRONMENTID
    }
    if ($attempt) {
        $parts += "Attempt",$env:RELEASE_ATTEMPTNUMBER
    }
    if ($phase) {
        $parts += "Phase",$env:RELEASE_DEPLOYPHASEID
    }
    if ($job) {
        $parts += "Job",$env:SYSTEM_JOBPOSITIONINPHASE
    }

    $parts += $name
    return $parts -join "_"
}

<#
.SYNOPSIS
Set the value of the specified persistent release variable.
These are stored as variables on the pipeline so that they can be read/written across jobs.
Variable names are pre-pended based on the flags specified.
#>
function Set-ReleasePersistentVariable {
    [CmdletBinding()]
    param(
        # The variable name
        [Parameter (Mandatory = $true, ValueFromPipelineByPropertyName = $true)] [string]$name,

        # The variable value
        [Parameter (Mandatory = $true, ValueFromPipelineByPropertyName = $true)] [string]$value,

        # Whether the variable should be scoped with the current stage
        [Parameter (Mandatory = $false, ValueFromPipelineByPropertyName = $true)] [switch]$stage,

        # Whether the variable should be scoped with the current attempt
        [Parameter (Mandatory = $false, ValueFromPipelineByPropertyName = $true)] [switch]$attempt,

        # Whether the variable should be scoped with the current phase
        [Parameter (Mandatory = $false, ValueFromPipelineByPropertyName = $true)] [switch]$phase,

        # Whether the variable should be scoped with the current job
        [Parameter (Mandatory = $false, ValueFromPipelineByPropertyName = $true)] [switch]$job,

        # The Azure DevOps organization
        [string]$organization = $script:defaultOrganization,

        # The Azure DevOps project
        [string]$project = $script:defaultProject,

        # The release id
        [int]$releaseId = $script:defaultReleaseId,

        # Whether to skip overwriting existing values
        [switch]$doNotOverwrite
    )

    Begin {
        if ([string]::IsNullOrWhitespace($organization)) { throw "The `$organization must not be null or whitespace" }
        if ([string]::IsNullOrWhitespace($project)) { throw "The `$project must not be null or whitespace" }
        if ($releaseId -le 0) { throw "The `$releaseId must be a positive number" }
        $variablesToAdd = @()
    }
    Process {
        if ([string]::IsNullOrWhitespace($name)) { throw "The `$name must not be null or whitespace" }

        $persistentName = Get-ReleasePersistentVariableName -name $name -stage $stage -attempt $attempt -phase $phase -job $job
        Write-Info "Setting persistent release variable [$persistentName]"
        $variablesToAdd += @{
            name = $persistentName;
            value = $value;
        }
    }
    End {
        for ($i = 1; $i -le 10; ++$i)
        {
            $releaseValue = Get-Release -organization $organization -project $project -releaseId $releaseId
            
            $variablesToAdd | ForEach-Object {
                $persistentName = $_.name
                $value = $_.value
                if ($null -ne ($releaseValue.variables | Get-Member -MemberType NoteProperty -name $persistentName)) {
                    if (-not $doNotOverwrite) {
                        ($releaseValue.variables.$persistentName).value = $value
                    } else {
                        Write-PipelineIssue -warningMessage "Skipping set of variable [$persistentName] as -doNotOverwrite has been specified"
                    }

                } else {
                    $releaseValue.variables | Add-Member -MemberType NoteProperty -name $persistentName -value (@{ value =  $value } | ConvertTo-Json -Compress | ConvertFrom-Json)
                }
            }
            
            try {
                $failureType = if ($i -eq 10) { "error" } else { "warning" }
                Update-Release -organization $organization -project $project -releaseId $releaseId -content $releaseValue -failureType $failureType | Out-Null
                return
            } catch {
                if ($i -eq 10) {
                    throw Write-PipelineIssue -errorMessage "Failed to update release persistent variable [$persistentName] after $i tries" -exception $_
                }
                Write-PipelineIssue -warningMessage "Failed to update release persistent variable [$persistentName] - trying again in $i seconds (try $i)" -exception $_
                Start-Sleep -Seconds $i
            }
        }
    }
}
Export-ModuleMember -Function Set-ReleasePersistentVariable

<#
.SYNOPSIS
Retrieves the value of the specified persistent release variable.
These are stored as variables on the pipeline so that they can be read/written across jobs.
Variable names are pre-pended based on the flags specified.
#>
function Get-ReleasePersistentVariable {
    [CmdletBinding()]
    param(
        # The variable name
        [Parameter (Mandatory = $true)] [string]$name,

        # Whether the variable should be scoped with the current stage
        [Parameter (Mandatory = $false)] [switch]$stage,

        # Whether the variable should be scoped with the current attempt
        [Parameter (Mandatory = $false)] [switch]$attempt,

        # Whether the variable should be scoped with the current phase
        [Parameter (Mandatory = $false)] [switch]$phase,

        # Whether the variable should be scoped with the current job
        [Parameter (Mandatory = $false)] [switch]$job,

        # The Azure DevOps organization
        [string]$organization = $script:defaultOrganization,

        # The Azure DevOps project
        [string]$project = $script:defaultProject,

        # The release id
        [int]$releaseId = $script:defaultReleaseId
    )

    if ([string]::IsNullOrWhitespace($organization)) { throw "The `$organization must not be null or whitespace" }
    if ([string]::IsNullOrWhitespace($project)) { throw "The `$project must not be null or whitespace" }
    if ($releaseId -le 0) { throw "The `$releaseId must be a positive number" }
    if ([string]::IsNullOrWhitespace($name)) { throw "The `$name must not be null or whitespace" }
    
    $persistentName = Get-ReleasePersistentVariableName -name $name -stage $stage -attempt $attempt -phase $phase -job $job
    Write-Info "Getting persistent release variable [$persistentName]"

    $releaseValue = Get-Release -organization $organization -project $project -releaseId $releaseId
    if ($null -ne ($releaseValue.variables | Get-Member -MemberType NoteProperty -name $persistentName)) {
        return $releaseValue.variables.$persistentName.value
    }
    return $null
}
Export-ModuleMember -Function Get-ReleasePersistentVariable

# Gets the specified task group
function Get-TaskGroup {
    [CmdletBinding()]
    param(
        [string]$organization = $script:defaultOrganization,
        [string]$project = $script:defaultProject,
        [string]$taskGroupId
    )

    if ([string]::IsNullOrWhitespace($organization)) { throw "The `$organization must not be null or whitespace" }
    if ([string]::IsNullOrWhitespace($project)) { throw "The `$project must not be null or whitespace" }
    if ([string]::IsNullOrWhitespace($taskGroupId)) { throw "The `$taskGroupId must not be null or whitespace" }

    return Invoke-RestMethodWithRetry -method "GET" -uri "https://dev.azure.com/$organization/$project/_apis/distributedtask/taskgroups/$taskGroupId`?api-version=6.0-preview"
}
Export-ModuleMember -Function Get-TaskGroup

# Updates the specified task group
function Update-TaskGroup {
    [CmdletBinding()]
    param(
        [string]$organization = $script:defaultOrganization,
        [string]$project = $script:defaultProject,
        $content = $null
    )

    if ([string]::IsNullOrWhitespace($organization)) { throw "The `$organization must not be null or whitespace" }3
    if ([string]::IsNullOrWhitespace($project)) { throw "The `$project must not be null or whitespace" }
    if ($null -eq $content) { throw "The `$content must be set" }

    return Invoke-RestMethodWithRetry -method "PUT" -uri "https://dev.azure.com/$organization/$project/_apis/distributedtask/taskgroups`?api-version=6.0-preview" -body $content
}
Export-ModuleMember -Function Update-TaskGroup

# See https://learn.microsoft.com/rest/api/azure/devops/git/refs/list?view=azure-devops-rest-6.0
function Get-Branches {
    [CmdletBinding()]
    param(
        [string]$repo,

        [string]$organization = $script:defaultOrganization,
        [string]$project = $script:defaultProject
    )

    if ([string]::IsNullOrWhitespace($repo)) { throw "The `$repo must not be null or whitespace" }
    if ([string]::IsNullOrWhitespace($organization)) { throw "The `$organization must not be null or whitespace" }
    if ([string]::IsNullOrWhitespace($project)) { throw "The `$project must not be null or whitespace" }

    return Invoke-RestMethodWithRetry -method "GET" -uri "https://dev.azure.com/$organization/$project/_apis/git/repositories/$repo/refs?api-version=7.0"
}
Export-ModuleMember -Function Get-Branches

# See https://learn.microsoft.com/rest/api/azure/devops/git/refs/update%20refs?view=azure-devops-rest-6.0
function Remove-Branch6 {
    [CmdletBinding()]
    param(
        [string]$repo,

        [array]$branch,

        [string]$organization = $script:defaultOrganization,
        [string]$project = $script:defaultProject
    )

    if ([string]::IsNullOrWhitespace($repo)) { throw "The `$repo must not be null or whitespace" }
    if (($null -eq $branch) -or ($branch.Count -eq 0)) { throw "The `$branch must be specified" }

    if ([string]::IsNullOrWhitespace($organization)) { throw "The `$organization must not be null or whitespace" }
    if ([string]::IsNullOrWhitespace($project)) { throw "The `$project must not be null or whitespace" }

    $body = @()
    ForEach ($branchValue in $branch) {
        $body += @{
            name = $branchValue.name;
            newObjectId = "0000000000000000000000000000000000000000";
            oldObjectId = $branchValue.objectId;
        }
    }
    return Invoke-RestMethodWithRetry -method "POST" -uri "https://dev.azure.com/$organization/$project/_apis/git/repositories/$repo/refs?api-version=6.0" -body $body
}
Export-ModuleMember -Function Remove-Branch

# See https://learn.microsoft.com/rest/api/azure/devops/git/refs/list?view=azure-devops-rest-7.0&tabs=HTTP
function Get-RepoRef {
    [CmdletBinding()]
    param(
        [Alias("repository")]
        [string]$repo,

        # Start with heads/ to filter to branches
        [string]$filter,

        # The Azure DevOps project
        [string]$organization = $script:defaultOrganization,

        # The Azure DevOps project
        [string]$project = $script:defaultProject
    )

    if ([string]::IsNullOrWhitespace($organization)) { throw "-organization must not be null or whitespace" }
    if ([string]::IsNullOrWhitespace($project)) { throw "-project must not be null or whitespace" }

    $repoValue = Get-Repository -repo:$repo -organization:$organization -project:$project

    $url = "https://dev.azure.com/$organization/$project/_apis/git/repositories/$($repoValue.id)/refs?api-version=7.0"
    if (-not [string]::IsNullOrWhiteSpace($filter)) {
        $url += "&filter=$filter"
    }
    return Invoke-RestMethodWithRetry -method "Get" -uri $url
}
Export-ModuleMember -Function Get-RepoRef

<#
.SYNOPSIS
Saves a file from an Azure DevOps repository to disk
.LINK
https://learn.microsoft.com/rest/api/azure/devops/git/items/get?view=azure-devops-rest-5.1
#>
function Get-RepoFile {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory=$true, ParameterSetName="list_repo")]
        [Parameter(Mandatory=$true, ParameterSetName="singleFile_repo")]
        [Alias("repository")]
        [string]$repo,
        
        # The branch from which the file should be downloaded
        [string]$branch = "master",
        
        # The path to the file in the Azure DevOps repository
        [Parameter(Mandatory=$true, ParameterSetName="singleFile_repo")]
        [string]$repoFilePath,
        
        # The path to which the file should be written
        [Parameter(ParameterSetName="singleFile_repo")]
        [string]$outputFile,

        # The Azure DevOps project
        [string]$organization = $script:defaultOrganization,

        # The Azure DevOps project
        [string]$project = $script:defaultProject
    )

    if ([string]::IsNullOrWhitespace($organization)) { throw "The `$organization must not be null or whitespace" }
    if ([string]::IsNullOrWhitespace($project)) { throw "The `$project must not be null or whitespace" }
    if ([string]::IsNullOrWhitespace($branch)) { throw "The `$branch must not be null or whitespace" }

    $repoValue = Get-Repository -repo:$repo -organization:$organization -project:$project
    if ([string]::IsNullOrWhiteSpace($branch)) {
        $branch = $repo.defaultBranch
    }
    
    if ($PsCmdlet.ParameterSetName -eq "singleFile_repo") {
        if ([string]::IsNullOrWhitespace($repoFilePath)) { throw "-repoFilePath must not be null or whitespace" }

        $url = "https://dev.azure.com/$organization/$project/_apis/git/repositories/$($repoValue.id)/items?path=$repoFilePath&versionDescriptor.version=$branch&api-version=6.0"
        if ([string]::IsNullOrWhiteSpace($outputFile)) {
            return Invoke-RestMethodWithRetry -method "Get" -uri $url -returnNullOn404
        } else {
            $null = Invoke-RestMethodWithRetry -method "Get" -uri $url -outFile $outputFile
        }
    } else {
        # List files - see https://learn.microsoft.com/rest/api/azure/devops/git/items/list?view=azure-devops-rest-7.0&tabs=HTTP
        $recursionLevel = "full"
        return Invoke-RestMethodWithRetry -method "Get" -uri "https://dev.azure.com/$organization/$project/_apis/git/repositories/$($repoValue.id)/items?recursionLevel=$recursionLevel&`$format=json&versionDescriptor.version=$branch&versionDescriptor.versionType=branch&api-version=7.0"
    }
    $null = Invoke-RestMethodWithRetry -method "Get" -uri "https://dev.azure.com/$organization/$project/_apis/git/repositories/$repo/items?path=$repoFilePath&versionDescriptor.version=$branch&api-version=6.0" -outFile $outputFile
}
Export-ModuleMember -Function Get-RepoFile

# Pushes changes up to Azure DevOps
# See https://learn.microsoft.com/rest/api/azure/devops/git/pushes/create?view=azure-devops-rest-7.0
function Push-Commit {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory=$true, ParameterSetName="repo")]
        [Alias("repository")]
        [string]$repo,

        $body,

        [switch]$whatIf,

        # The Azure DevOps project
        [string]$organization = $script:defaultOrganization,

        # The Azure DevOps project
        [string]$project = $script:defaultProject
    )

    if ([string]::IsNullOrWhitespace($organization)) { throw "-organization must not be null or whitespace" }
    if ([string]::IsNullOrWhitespace($project)) { throw "-project must not be null or whitespace" }
    if ($null -eq $body) { throw "-body must be specified" }

    $repoValue = Get-Repository -repo:$repo -organization:$organization -project:$project
    
    return Invoke-RestMethodWithRetry -method "Post" -uri "https://dev.azure.com/$organization/$project/_apis/git/repositories/$($repoValue.id)/pushes?api-version=7.0" -body $body -whatIf:$whatIf
}
Export-ModuleMember -Function Push-Commit

<#
.Synopsis
Delete the specified remote branch
.Link
https://learn.microsoft.com/rest/api/azure/devops/git/pushes/create?view=azure-devops-rest-7.0
#>
function Remove-Branch {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory=$true, ParameterSetName="repo")]
        [Alias("repository")]
        [string]$repo,

        $branch,

        [switch]$whatIf,

        # The Azure DevOps project
        [string]$organization = $script:defaultOrganization,

        # The Azure DevOps project
        [string]$project = $script:defaultProject
    )

    if ([string]::IsNullOrWhitespace($organization)) { throw "-organization must not be null or whitespace" }
    if ([string]::IsNullOrWhitespace($project)) { throw "-project must not be null or whitespace" }

    $repoValue = Get-Repository -repo:$repo -organization:$organization -project:$project

    if (-not $branch.StartsWith("refs")) {
        $branch = "refs/heads/$branch"
    }
    $ref = Get-RepoRef -repo $repo -filter ($branch -replace "refs/","") | Where-Object {
        $_.name -eq $branch
    }
    return Invoke-RestMethodWithRetry -method "Post" -uri "https://dev.azure.com/$organization/$project/_apis/git/repositories/$($repoValue.id)/refs?api-version=7.0" -body @(@{
        name = $branch
        oldObjectId = $ref.objectId
        newObjectId = "0000000000000000000000000000000000000000"
    }) -logBody -whatIf:$whatIf
}
Export-ModuleMember -Function Remove-Branch

<# Adds or updates the specified file and optionally creates prs
.Synopsis
See https://learn.microsoft.com/rest/api/azure/devops/git/pushes/create?view=azure-devops-rest-7.0
.Example
Get-Repository | Where-Object {
    -not $_.isDisabled
} | Where-Object {
    $_.name -match "((Omnichannel)|(UnifiedRouting.DecisionEngine))"
| | Where-Object {
    # Ignore A&E repos
    $_.name -notmatch "(ConversationControl|CRM\.Solutions\.CustomControlsExtended|CRM\.Solutions\.MAgE|CRM\.OmniChannel\.CallingSDK|CRM\.Solutions\.CustomerServiceTrial|CRM\.OmniChannel\.LiveChatWidget|CRM\.Omnichannel\.ProductivityTools|CRM\.Solutions\.CEC|CRM\.Solutions\.ChannelApiFramework)"
} | Sort-Object {
    $_.name
} | ForEach-Object {
    Update-RepoFileOrCreate -repo $_.name -repoFilePath /azurepipelines-coverage.yml -newContent "coverage:
  status:           # Code coverage status will be posted to pull requests based on targets defined below.
    comments: on    # Off by default. When on, details about coverage for each file changed will be posted as a pull request comment.
    diff:           # Diff coverage is code coverage only for the lines changed in a pull request.
      target: 80%   # Default is 70%" -title "Update code coverage settings" -description "Applying to all OC repos" -workItemId 3180262 -pr -open -autocomplete -deletePrBranch -closeWorkItems:$false  -whatif
}
#>
function Update-RepoFileOrCreate {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory=$true, ParameterSetName="repo")]
        [string]$repo,

        [string]$repoFilePath,

        [string]$newContent,

        [string]$branch,

        # Id of the parent work item
        [int]$workItemId = -1,

        [string]$prBranch,
        
        [switch]$pr,
        [switch]$open,
        [string]$title,
        [string]$description,

        [switch]$autocomplete,
        [switch]$deletePrBranch,
        [switch]$closeWorkItems,

        [switch]$whatIf,

        # The Azure DevOps project
        [string]$organization = $script:defaultOrganization,

        # The Azure DevOps project
        [string]$project = $script:defaultProject
    )

    if ([string]::IsNullOrWhitespace($organization)) { throw "-organization must not be null or whitespace" }
    if ([string]::IsNullOrWhitespace($project)) { throw "-project must not be null or whitespace" }
    if ([string]::IsNullOrWhitespace($repoFilePath)) { throw "-repoFilePath must not be null or whitespace" }
    if ([string]::IsNullOrWhitespace($newContent)) { throw "-newContent must not be null or whitespace" }
    if ([string]::IsNullOrWhitespace($title)) { throw "-title must not be null or whitespace" }
    if ([string]::IsNullOrWhitespace($description)) { throw "-description must not be null or whitespace" }

    if ($pr) {
        if ($workItemId -lt 1) { throw "-workItemId must be specified" }
    }

    if ([string]::IsNullOrWhiteSpace($prBranch)) {
        if ($workItemId -gt 0) {
            $prBranch = "oclpr/z_${workItemId}"
        } else {
            $prBranch = "oclpr/zz_$([Guid]::NewGuid())"
        }
    }

    $repoValue = Get-Repository -repo:$repo -organization:$organization -project:$project

    if ([string]::IsNullOrWhiteSpace($branch)) {
        $branch = $repo.defaultBranch
    }

    $branchShortName = $branch -replace "refs/","" -replace "heads/",""
    if ([string]::IsNullOrWhiteSpace($prBranch)) {
        $prBranch = $branch
    }
    $prBranchShortName = $prBranch -replace "refs/","" -replace "heads/",""

    if (-not $repoFilePath.StartsWith("/")) {
        $repoFilePath = "/$repoFilePath"
    }

    $ref = Get-RepoRef -repo $repo.id -filter "heads/$branchShortName" | Where-Object {
        $_.name -eq "refs/heads/$branchShortName"
    } | Select-Object -First 1
    if ($null -eq $ref) {
        throw "Failed to find branch $branch name in $($repo.name)"
    }
    $prRef = Get-RepoRef -repo $repo.id -filter "heads/$prBranchShortName" | Where-Object {
        $_.name -eq "refs/heads/$prBranchShortName"
    } | Select-Object -First 1
    if ($null -eq $prRef) { # Branch already exists - if so, use it
        $prRef = $ref
    }

    $newContentWithoutCharacterReturns = $newContent -replace "`r",""
    $current = Get-RepoFile -repo $repoValue.id -branch $branchShortName -repoFilePath $repoFilePath
    if ($null -ne $current) { $current = $current -replace "`r","" }
    $currentPr = Get-RepoFile -repo $repoValue.id -branch $prBranchShortName -repoFilePath $repoFilePath
    if ($null -ne $currentPr) { $currentPr = $currentPr -replace "`r","" }
    $global:current = $current
    $global:currentPr = $currentPr
    if ([string]::IsNullOrWhiteSpace($currentPr)) {
        $currentPr = $current
    }
    if ($currentPr -eq $newContentWithoutCharacterReturns) {
        Write-Info "$($repo.name) branch $prBranchShortName file $repoFilePath is up-to-date"
        return
    } else {
        $body = @{
            refUpdates = @(@{
                name = "refs/heads/$prBranchShortName"
                oldObjectId = $prRef.objectId
            })
            commits = @(@{
                comment = "$title`n`n$description"
                changes = @()
            })
        }
        if ($null -eq $currentPr) {
            # new file
            $body.commits[0].changes += @{
                changeType = "add"
                item = @{
                    path = $repoFilePath
                }
                newContent = @{
                    content = $newContent
                    contentType = "rawtext"
                }
            }
        } else {
            # update file
            $body.commits[0].changes += @{
                changeType = "edit"
                item = @{
                    path = $repoFilePath
                }
                newContent = @{
                    content = $newContent
                    contentType = "rawtext"
                }
            }
        }
        $null = Push-Commit -repo $repo.id -organization:$organization -project:$project -body $body -whatIf:$whatIf
    }
    if ($pr) {
        $pullRequest = Get-PullRequests -sourceBranchName $prBranch -targetBranch $branch -repo $repo.id -status active -organization $organization -project $project | Select-Object -First 1
        $new = $false
        if ($null -eq $pullRequest) {
            $new = $true
            # Prefix the pr title with the repo name to make listing multiple prs simpler
            $repoShortName = $repo.name -replace "CRM\.(((OmniChannel)|(Solutions)|(Services))\.)?", ""
            $pullRequest = New-PullRequest -repo $repo.id -sourceBranch $prBranchShortName -targetBranch $branchshortName -title "[$repoShortName] $title" -description $description -workItemId $workItemId -organization $organization -project $project -whatIf:$whatIf
        }
        
        if ($whatIf) {
            return
        }

        if ($autocomplete) {
            $null = Set-PullRequestAutoComplete -pullRequestId $pullRequest.pullRequestId -deleteSourceBranch:$deletePrBranch -closeWorkItems:$closeWorkItems -organization $organization -project $project -whatIf:$whatIf
        }
        
        $find = "https://[^@]*@" # put in a variable to fix ado syntax highlighting
        $url = "$($repo.remoteUrl -replace $find,"https://")/pullRequest/$($pullRequest.pullRequestId)"
        $details = if ($new) { "has been created" } else { "already exists" }
        Write-PipelineIssue -warningMessage "$($repo.name) $details - [$($pullRequest.title)]($url)"
        if ($open) {
            Write-Command $url
            Start-Process $url
        }
    }
}
Export-ModuleMember -Function Update-RepoFileOrCreate

# See https://learn.microsoft.com/rest/api/azure/devops/graph/users/list?view=azure-devops-rest-5.1
function Get-Users {
    [CmdletBinding()]
    param(
        # The organization
        [Parameter(Mandatory=$false)] [string]$organization = $script:defaultOrganization,
        
        # The list of user subject subtypes to reduce the retrieved results e.g. msa, microsoft entra, svc, imp, etc
        [Parameter(Mandatory=$false)] [string[]]$subjectTypes,

        [Parameter(Mandatory=$false)] [string]$user
    )

    if ([string]::IsNullOrWhitespace($organization)) { throw "The `$organization must not be null or whitespace" }

    $allUsers = @()
    $continuationToken = $null
    if (-not [string]::IsNullOrWhiteSpace($user)) {
        Write-Info "Looking for user $user..."
    }
    while ($true) {
        $url = "https://vssps.dev.azure.com/$organization/_apis/graph/users`?api-version=5.1-preview.1"
        if (($null -ne $subjectTypes) -and ($subjectTypes.Length -gt 0)) {
            $url += "&subjectTypes=$($subjectTypes -join ",")"
        }
        if (-not [string]::IsNullOrWhiteSpace($continuationToken)) {
            $url += "&continuationToken=$continuationToken"
        }
        $response = Invoke-RestMethodWithRetry -method "Get" -uri $url -raw
        $someUsers = $response.Content | ConvertFrom-Json
        if (-not [string]::IsNullOrWhiteSpace($user)) {
            $userValue = $someUsers.value | Where-Object { ($_.PSObject.Properties.Name -eq "directoryAlias") -and ($_.directoryAlias -eq $user) } | Select-Object -First 1
            if ($null -ne $userValue) {
                return $userValue
            }
        } else {
            $allUsers += $someUsers.value
        }
        if ($response.Headers.ContainsKey("X-MS-ContinuationToken")) {
            $continuationToken = $response.Headers["X-MS-ContinuationToken"]
        } else {
            break
        }
    }
    if ([string]::IsNullOrWhiteSpace($user)) {
        return $allUsers
    } else {
        return $null
    }
}
Export-ModuleMember -Function Get-Users

# Gets the current user - returns @(userId, email)
function Get-CurrentUser {
    [CmdletBinding()]
    param(
        # Default to false for backwards compatibility
        [switch]$full,

        [string]$organization = $script:defaultOrganization
    )

    if ($full) {
        # See https://stackoverflow.com/questions/73569095/how-to-get-current-user-information-from-rest-api-in-azure-devops
        Invoke-RestMethodWithRetry -method "GET" -uri "https://dev.azure.com/$organization/_apis/ConnectionData"
    } else {
        $response = Invoke-RestMethodWithRetry -method "GET" -uri "https://dev.azure.com/$organization/_apis/projects`?api-version=6.0" -raw
        return $response.Headers["X-VSS-UserData"] -split ":"
    }
}
Export-ModuleMember -Function Get-CurrentUser

function Get-UserId {
    [CmdletBinding()]
    param(
        $user,

        [string]$organization = $script:defaultOrganization
    )

    if ($null -eq $user) {
        throw "-user must not be null"
    }
    return Invoke-RestMethodWithRetry -method "Get" -uri $user._links.storageKey.href -organization $organization
}
Export-ModuleMember Get-UserId

# See https://stackoverflow.com/questions/59770400/how-do-you-revoke-someone-elses-pat
function Get-UserAccessTokens {
    [CmdletBinding()]
    param(
        [string]$organization = $script:defaultOrganization
    )

    if ([string]::IsNullOrWhitespace($organization)) { throw "-organization must not be null or whitespace" }

    return Invoke-RestMethodWithRetry -method "Get" -uri "https://vssps.dev.azure.com/$organization/_apis/Token/SessionTokens?api-version=5.0-preview.1"
}
Export-ModuleMember -Function Get-UserAccessTokens

# See https://stackoverflow.com/questions/59770400/how-do-you-revoke-someone-elses-pat
function Remove-UserAccessToken {
    [CmdletBinding()]
    param(
        [string]$organization = $script:defaultOrganization,
        [string]$authorizationId
    )

    if ([string]::IsNullOrWhitespace($organization)) { throw "-organization must not be null or whitespace" }
    if ([string]::IsNullOrWhitespace($authorizationId)) { throw "-authorizationId must not be null or whitespace" }

    return Invoke-RestMethodWithRetry -method "DELETE" -uri "https://vssps.dev.azure.com/$organization/_apis/Token/SessionTokens/$authorizationId`?api-version=5.0-preview.1"
}
Export-ModuleMember -Function Remove-UserAccessToken

# See https://learn.microsoft.com/rest/api/azure/devops/git/pull-requests/create?view=azure-devops-rest-7.0
function New-PullRequest {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory=$true, ParameterSetName="raw_repo")]
        [Parameter(Mandatory=$true, ParameterSetName="easy_repo")]
        [Alias("repository")]
        [string]$repo,

        [Parameter(ParameterSetName="easy_repo")]
        [string]$sourceBranch,

        [Parameter(ParameterSetName="easy_repo")]
        [string]$targetBranch = "master",
        
        [Parameter(ParameterSetName="easy_repo")]
        [string]$title,
        
        [Parameter(ParameterSetName="easy_repo")]
        [string]$description,
        
        # Id of the parent work item
        [int[]]$workItemId,

        [Parameter(ParameterSetName="easy_repo")]
        [string[]]$labels,

        [switch]$whatIf,

        [Parameter(ParameterSetName="raw_repo")]
        $body,

        [string]$organization = $script:defaultOrganization,
        [string]$project = $script:defaultProject
    )

    if ([string]::IsNullOrWhitespace($organization)) { throw "The `$organization must not be null or whitespace" }
    if ([string]::IsNullOrWhitespace($project)) { throw "The `$project must not be null or whitespace" }

    $repoValue = Get-Repository -repo:$repo -organization:$organization -project:$project

    if ($null -eq $body) {
        if ([string]::IsNullOrWhitespace($sourceBranch)) { throw "The `$sourceBranch must not be null or whitespace" }
        if ([string]::IsNullOrWhitespace($targetBranch)) { throw "The `$targetBranch must not be null or whitespace" }
        if ([string]::IsNullOrWhitespace($title)) { throw "The `$title must not be null or whitespace" }
        if ([string]::IsNullOrWhitespace($description)) { throw "The `$description must not be null or whitespace" }
        if (-not $sourceBranch.StartsWith("refs/")) {
            $sourceBranch = "refs/heads/$sourceBranch"
        }
        if (-not $targetBranch.StartsWith("refs/")) {
            $targetBranch = "refs/heads/$targetBranch"
        }

        $body = @{ 
            sourceRefName = $sourceBranch
            targetRefName = $targetBranch
            title = $title
            description = $description
            labels = @(@{
                name = "oclPr"
            })
        }
        if ($null -ne $labels -and $labels.Count -gt 0) {
            ForEach($label in $labels) {
                $body.labels += if ($label -is [string]) {
                    @{ name = $label }
                }
            }
        }
    }

    $pr = Invoke-RestMethodWithRetry -method "POST" -uri "https://dev.azure.com/$organization/$project/_apis/git/repositories/$($repoValue.id)/pullRequests`?api-version=7.0" -body $body -whatIf:$whatIf -logBody
    if ($null -ne $workItemId -and $workItemId.Count -gt 0) {
        $null = Add-PullRequestWorkItem -pullRequestId $pr.pullRequestId -workItemId $workItemId -organization $organization -project $project -whatIf:$whatIf
    }
    return $pr
}
Export-ModuleMember -Function New-PullRequest

# Updates a pull request
# See https://learn.microsoft.com/rest/api/azure/devops/git/pull-requests/update?view=azure-devops-rest-7.0
function Update-PullRequest {
    [CmdletBinding()]
    param(
        # The pull request id
        [Parameter(Mandatory=$false)] [int]$pullRequestId = -1,

        # The pull request update body
        $body,

        [string]$repo,

        [switch]$whatIf,

        [string]$organization = $script:defaultOrganization,
        [string]$project = $script:defaultProject,
        [string]$accessToken = $null
    )

    if ([string]::IsNullOrWhitespace($organization)) { throw "-organization must not be null or whitespace" }
    if ([string]::IsNullOrWhitespace($project)) { throw "-project must not be null or whitespace" }
    if ($null -eq $body) { throw "-body must not be null or whitespace" }
    if ($pullRequestId -lt 1) { throw "-pullRequestId must be specified" }

    $repoValue = Get-Repository -repo:$repo -organization:$organization -project:$project

    Invoke-RestMethodWithRetry -method "PATCH" -uri "https://dev.azure.com/$organization/$project/_apis/git/repositories/$($repoValue.id)/pullrequests/$pullRequestId`?api-version=7.0" -body $body -whatIf:$whatIf -logBody -accessToken $accessToken
}
Export-ModuleMember -Function Update-PullRequest

# Abandons the specified pull request
# See https://learn.microsoft.com/rest/api/azure/devops/git/pull-requests/update?view=azure-devops-rest-7.0
function Remove-PullRequest {
    [CmdletBinding()]
    param(
        # The pull request id
        [Parameter(Mandatory=$false)] [int]$pullRequestId = -1,

        [switch]$whatIf,

        [string]$organization = $script:defaultOrganization,
        [string]$project = $script:defaultProject,
        [string]$accessToken = $null
    )

    if ([string]::IsNullOrWhitespace($organization)) { throw "-organization must not be null or whitespace" }
    if ([string]::IsNullOrWhitespace($project)) { throw "-project must not be null or whitespace" }
    if ($pullRequestId -lt 1) { throw "-pullRequestId must be specified" }
    $pr = Get-PullRequest -pullRequestId $pullRequestId -organization:$organization -project:$project
    Update-PullRequest -pullRequestId $pullRequestId -body @{
        status = "abandoned"
    } -repo $pr.repository.id -whatIf:$whatIf -organization:$organization -project:$project -accessToken $accessToken
}
Export-ModuleMember -Function Remove-PullRequest

# Sets autocomplete on the specified pull request
function Set-PullRequestAutoComplete {
    [CmdletBinding()]
    param(
        # The pull request id
        [int]$pullRequestId = -1,

        [switch]$deleteSourceBranch,
        [switch]$closeWorkItems,

        [ValidateSet("noFastForward","rebase","rebaseMerge","squash")]
        [string]$mergeStrategy = "squash",

        [switch]$whatIf,

        [string]$organization = $script:defaultOrganization,
        [string]$project = $script:defaultProject
    )

    if ([string]::IsNullOrWhitespace($organization)) { throw "-organization must not be null or whitespace" }
    if ([string]::IsNullOrWhitespace($project)) { throw "-project must not be null or whitespace" }

    $pr = Get-PullRequest -pullRequestId $pullRequestId -organization $organization -project $project
    
    $evaluations = Get-PullRequestEvaluations -pullRequestId $pullRequestId -organization $organization -project $project
    Update-PullRequest -pullRequestId $pullRequestId -repo $pr.repository.id -body @{
        autoCompleteSetBy = @{
            id = $pr.createdBy.id
        }
        completionOptions = @{
            deleteSourceBranch = [bool]$deleteSourceBranch
            transitionWorkItems = [bool]$closeWorkItems
            autoCompleteIgnoreConfigIds = @($evaluations | Where-Object { -not $_.configuration.isBlocking } | ForEach-Object { $_.configuration.id })
            bypassPolicy = $false
            mergeStrategy = $mergeStrategy
        }
    } -organization $organization -project $project -whatIf:$whatIf
}
Export-ModuleMember -Function Set-PullRequestAutoComplete

# See https://learn.microsoft.com/rest/api/azure/devops/git/pull%20requests/get%20pull%20request%20by%20id?view=azure-devops-rest-7.0
function Get-PullRequest {
    [CmdletBinding(DefaultParameterSetName="single")]
    param(
        # The pull request id
        [Parameter(Mandatory=$true,ParameterSetName="single")] [nullable[int]]$pullRequestId,

        # The pull request source branch name
        [Parameter(ParameterSetName="multiple")] [string]$sourceBranchName,
        
        # The pull request target branch name
        [Parameter(ParameterSetName="multiple")] [string]$targetBranchName,

        [Parameter(ParameterSetName="multiple")] [string]$repo,

        # The pull request status
        [ValidateSet("abandoned", "active", "all", "completed", "notSet")]
        [Parameter(ParameterSetName="multiple")] [string]$status = "completed",
        
        # The id of the pull request creator
        [Parameter(ParameterSetName="multiple")] [string]$creatorId,
        
        # The id of a pull request reviewer
        [Parameter(ParameterSetName="multiple")] [string]$reviewerId,
        
        # Whether to include links
        [Parameter(ParameterSetName="multiple")] [switch]$includeLinks,
        
        # The organization
        [string]$organization = $script:defaultOrganization,
        
        # The project
        [string]$project = $script:defaultProject
    )
    if ([string]::IsNullOrWhitespace($organization)) { throw "-organization must not be null or whitespace" }
    if ([string]::IsNullOrWhitespace($project)) { throw "-project must not be null or whitespace" }
    
    switch ($PsCmdlet.ParameterSetName) {
        "single" {
            if ($null -eq $pullRequestId) { throw "-pullRequestId must be specified" }
            return Invoke-RestMethodWithRetry -method "GET" -uri "https://dev.azure.com/$organization/$project/_apis/git/pullrequests/$pullRequestId`?api-version=7.0"
        }
        "multiple" {
            $url = "https://dev.azure.com/$organization/$project/_apis/git/pullrequests`?api-version=7.0&`$top=100000"
            if ($includeLinks) {
                $url += "&searchCriteria.includeLinks=true"
            }
            if (-not [string]::IsNullOrWhiteSpace($sourceBranchName)) {
                if (-not $sourceBranchName.startsWith("refs/")) {
                    $sourceBranchName = "refs/heads/$sourceBranchName"
                }
                $url += "&searchCriteria.sourceRefName=$sourceBranchName"
            }
            if (-not [string]::IsNullOrWhiteSpace($targetBranchName)) {
                if (-not $targetBranchName.startsWith("refs/")) {
                    $targetBranchName = "refs/heads/$targetBranchName"
                }
                $url += "&searchCriteria.targetRefName=$targetBranchName"
            }
            if (-not [string]::IsNullOrWhiteSpace($repo)) {
                $repoValue = Get-Repository -repo $repo -organization $organization -project $project
                $url += "&searchCriteria.repositoryId=$($repoValue.id)"
            }
            if (-not [string]::IsNullOrWhiteSpace($status)) {
                $url += "&searchCriteria.status=$status"
            }
            if (-not [string]::IsNullOrWhiteSpace($creatorId)) {
                $url += "&searchCriteria.creatorId=$creatorId"
            }
            if (-not [string]::IsNullOrWhiteSpace($reviewerId)) {
                $url += "&searchCriteria.reviewerId=$reviewerId"
            }
            return Invoke-RestMethodWithRetry -method "GET" -uri $url
        }
    }
}
Export-ModuleMember -Function Get-PullRequest

# See https://learn.microsoft.com/rest/api/azure/devops/git/pull-request-work-items/list?view=azure-devops-rest-7.0
function Get-PullRequestWorkItems {
    [CmdletBinding()]
    param(
        # The pull request id
        [Parameter(Mandatory=$false)] [int]$pullRequestId = -1,

        # Optional repo - repo can be determined via prId
        [string]$repo,
        
        # The organization
        [Parameter(Mandatory=$false)] [string]$organization = $script:defaultOrganization,
        
        # The project
        [Parameter(Mandatory=$false)] [string]$project = $script:defaultProject
    )
    if ([string]::IsNullOrWhitespace($organization)) { throw "-organization must not be null or whitespace" }
    if ([string]::IsNullOrWhitespace($project)) { throw "-project must not be null or whitespace" }
    if ($pullRequestId -le 0) { throw "-pullRequestId must be a positive number" }

    $repoValue = Get-Repository -repo:$repo -organization:$organization -project:$project

    return Invoke-RestMethodWithRetry -method "GET" -uri "https://dev.azure.com/$organization/$project/_apis/git/repositories/$($repoValue.id)/pullRequests/$pullRequestId/workitems?api-version=7.0"
}
Export-ModuleMember -Function Get-PullRequestWorkItems

# Associates a pull request and a work item
# Undocumented
function Add-PullRequestWorkItem {
    [CmdletBinding()]
    param(
        # The pull request id
        [Parameter(Mandatory=$false)] [int]$pullRequestId = -1,
        
        # Id of the parent work item
        [int[]]$workItemId,
        
        # The organization
        [Parameter(Mandatory=$false)] [string]$organization = $script:defaultOrganization,
        
        # The project
        [Parameter(Mandatory=$false)] [string]$project = $script:defaultProject,

        [switch]$whatIf
    )
    if ([string]::IsNullOrWhitespace($organization)) { throw "-organization must not be null or whitespace" }
    if ($pullRequestId -le 0) { throw "-pullRequestId must be a positive number" }
    if ($null -eq $workItemId -or $workItemId.Count -eq 0) { throw "-workItemId must be specified" }
    if ($null -eq $workItemId) { throw "-workItemId must be specified" }

    $pr = Get-PullRequest -pullRequestId $pullRequestId -organization $organization -project $project
    ForEach ($id in $workItemId) {
        return Invoke-RestMethodWithRetry -method "PATCH" -uri "https://dev.azure.com/$organization/_apis/wit/workItems/$id`?api-version=4.0-preview" -body @(@{
            op = 0
            path = "/relations/-"
            value = @{
                attributes = @{
                    name = "Pull Request"
                }
                rel = "ArtifactLink"
                url = $pr.artifactId # e.g. vstfs:///Git/PullRequestId/b276c3e1-2902-46bd-a686-484157b97f48%2fdf075eb7-594e-4bb3-a783-5b941001c3ed%2f884213
            }
        }) -contentType "application/json-patch+json" -logBody -whatIf:$whatIf
    }
}
Export-ModuleMember -Function Add-PullRequestWorkItem

# See https://learn.microsoft.com/rest/api/azure/devops/git/pull%20requests/get%20pull%20requests%20by%20project?view=azure-devops-rest-5.1
function Get-PullRequests {
    [CmdletBinding()]
    param(
        # The organization
        [Parameter(Mandatory=$false)] [string]$organization = $script:defaultOrganization,
        
        # The project
        [Parameter(Mandatory=$false)] [string]$project = $script:defaultProject,
        
        # The pull request source branch name
        [Parameter(Mandatory=$false)] [string]$sourceBranchName,
        
        # The pull request target branch name
        [Parameter(Mandatory=$false)] [string]$targetBranchName,

        [string]$repo,

        # The pull request status
        [ValidateSet("abandoned", "active", "all", "completed", "notSet")]
        [Parameter(Mandatory=$false)] [string]$status = "completed",
        
        # The id of the pull request creator
        [Parameter(Mandatory=$false)] [string]$creatorId,
        
        # The id of a pull request reviewer
        [Parameter(Mandatory=$false)] [string]$reviewerId,
        
        # Whether to include links
        [Parameter(Mandatory=$false)] [switch]$includeLinks
    )

    if ([string]::IsNullOrWhitespace($organization)) { throw "The `$organization must not be null or whitespace" }
    if ([string]::IsNullOrWhitespace($project)) { throw "The `$project must not be null or whitespace" }

    $allPrs = @()
    while ($true) {
        $url = "https://dev.azure.com/$organization/$project/_apis/git/pullrequests`?api-version=6.0&`$top=100000"
        if ($includeLinks) {
            $url += "&searchCriteria.includeLinks=true"
        }
        if (-not [string]::IsNullOrWhiteSpace($sourceBranchName)) {
            if (-not $sourceBranchName.startsWith("refs/")) {
                $sourceBranchName = "refs/heads/$sourceBranchName"
            }
            $url += "&searchCriteria.sourceRefName=$sourceBranchName"
        }
        if (-not [string]::IsNullOrWhiteSpace($targetBranchName)) {
            if (-not $targetBranchName.startsWith("refs/")) {
                $targetBranchName = "refs/heads/$targetBranchName"
            }
            $url += "&searchCriteria.targetRefName=$targetBranchName"
        }
        if (-not [string]::IsNullOrWhiteSpace($repo)) {
            $repoValue = Get-Repository -repo $repo -organization $organization -project $project
            $url += "&searchCriteria.repositoryId=$($repoValue.id)"
        }
        if (-not [string]::IsNullOrWhiteSpace($status)) {
            $url += "&searchCriteria.status=$status"
        }
        if (-not [string]::IsNullOrWhiteSpace($creatorId)) {
            $url += "&searchCriteria.creatorId=$creatorId"
        }
        if (-not [string]::IsNullOrWhiteSpace($reviewerId)) {
            $url += "&searchCriteria.reviewerId=$reviewerId"
        }
        return Invoke-RestMethodWithRetry -method "GET" -uri $url
    }
    return $allPrs
}
Export-ModuleMember -Function Get-PullRequests

# See https://learn.microsoft.com/rest/api/azure/devops/git/pull-request-labels/list?view=azure-devops-rest-7.0
function Get-PullRequestLabel {
    [CmdletBinding()]
    param(
        # The pull request id
        [nullable[int]]$pullRequestId,

        # The pull request repository - if not specified, this will be determined by fetching the pull request
        [string]$repo,
        
        # The organization
        [string]$organization = $script:defaultOrganization,
        
        # The project
        [string]$project = $script:defaultProject
    )
    if ([string]::IsNullOrWhitespace($organization)) { throw "-organization must not be null or whitespace" }
    if ([string]::IsNullOrWhitespace($project)) { throw "-project must not be null or whitespace" }
    if ($null -eq $pullRequestId) { throw "-pullRequestId must be specified" }

    if ([string]::IsNullOrWhiteSpace($repo)) {
        $pr = Get-PullRequest -pullRequestId $pullRequestId -organization $organization -project $project
        $repo = $pr.repository.name
    }
    return Invoke-RestMethodWithRetry -method "GET" -uri "https://dev.azure.com/$organization/$project/_apis/git/repositories/$repo/pullrequests/$pullRequestId/labels`?api-version=7.0"
}
Export-ModuleMember -Function Get-PullRequestLabel

# See https://learn.microsoft.com/rest/api/azure/devops/git/pull-request-labels/create?view=azure-devops-rest-7.0
function New-PullRequestLabel {
    [CmdletBinding()]
    param(
        # The pull request id
        [nullable[int]]$pullRequestId,

        # The pull request repository - if not specified, this will be determined by fetching the pull request
        [string]$repo,

        # The pr label
        [string]$label,
        
        # The organization
        [string]$organization = $script:defaultOrganization,
        
        # The project
        [string]$project = $script:defaultProject
    )
    if ([string]::IsNullOrWhitespace($organization)) { throw "-organization must not be null or whitespace" }
    if ([string]::IsNullOrWhitespace($project)) { throw "-project must not be null or whitespace" }
    if ($null -eq $pullRequestId) { throw "-pullRequestId must be specified" }
    if ([string]::IsNullOrWhitespace($label)) { throw "-label must not be null or whitespace" }

    if ([string]::IsNullOrWhiteSpace($repo)) {
        $pr = Get-PullRequest -pullRequestId $pullRequestId -organization $organization -project $project
        $repo = $pr.repository.name
    }
    return Invoke-RestMethodWithRetry -method "Post" -uri "https://dev.azure.com/$organization/$project/_apis/git/repositories/$repo/pullrequests/$pullRequestId/labels`?api-version=7.0" -body @{
        name = $label
    }
}
Export-ModuleMember -Function New-PullRequestLabel

# See https://learn.microsoft.com/rest/api/azure/devops/git/pull-request-labels/delete?view=azure-devops-rest-7.0
function Remove-PullRequestLabel {
    [CmdletBinding()]
    param(
        # The pull request id
        [nullable[int]]$pullRequestId,

        # The pull request repository - if not specified, this will be determined by fetching the pull request
        [string]$repo,

        # The pr label name or id
        [string]$label,
        
        # The organization
        [string]$organization = $script:defaultOrganization,
        
        # The project
        [string]$project = $script:defaultProject
    )
    if ([string]::IsNullOrWhitespace($organization)) { throw "-organization must not be null or whitespace" }
    if ([string]::IsNullOrWhitespace($project)) { throw "-project must not be null or whitespace" }
    if ($null -eq $pullRequestId) { throw "-pullRequestId must be specified" }
    if ([string]::IsNullOrWhitespace($label)) { throw "-label must not be null or whitespace" }

    if ([string]::IsNullOrWhiteSpace($repo)) {
        $pr = Get-PullRequest -pullRequestId $pullRequestId -organization $organization -project $project
        $repo = $pr.repository.name
    }
    $null = Invoke-RestMethodWithRetry -method "Delete" -uri "https://dev.azure.com/$organization/$project/_apis/git/repositories/$repo/pullrequests/$pullRequestId/labels/$label`?api-version=7.0"
}
Export-ModuleMember -Function Remove-PullRequestLabel

# See https://learn.microsoft.com/rest/api/azure/devops/git/pull%20requests/get%20pull%20request%20by%20id?view=azure-devops-rest-5.1
function Get-PullRequestIteration {
    [CmdletBinding()]
    param(
        # The pull request id
        [nullable[int]]$pullRequestId,

        # The pull request repository - if not specified, this will be determined by fetching the pull request
        [string]$repo,
        
        # The organization
        [string]$organization = $script:defaultOrganization,
        
        # The project
        [string]$project = $script:defaultProject
    )
    if ([string]::IsNullOrWhitespace($organization)) { throw "-organization must not be null or whitespace" }
    if ([string]::IsNullOrWhitespace($project)) { throw "-project must not be null or whitespace" }
    if ($null -eq $pullRequestId) { throw "-pullRequestId must be specified" }

    if ([string]::IsNullOrWhiteSpace($repo)) {
        $pr = Get-PullRequest -pullRequestId $pullRequestId -organization $organization -project $project
        $repo = $pr.repository.name
    }
    return Invoke-RestMethodWithRetry -method "GET" -uri "https://dev.azure.com/$organization/$project/_apis/git/repositories/$repo/pullrequests/$pullRequestId/iterations`?api-version=7.0"
}
Export-ModuleMember -Function Get-PullRequestIteration

# See https://learn.microsoft.com/rest/api/azure/devops/git/pull-request-threads/create?view=azure-devops-rest-7.0&tabs=HTTP
function New-PullRequestThread {
    [CmdletBinding()]
    param(
        # The pull request id
        [nullable[int]]$pullRequestId,

        # The pull request repository - if not specified, this will be determined by fetching the pull request
        [string]$repo,
        
        # The organization
        [string]$organization = $script:defaultOrganization,
        
        # The project
        [string]$project = $script:defaultProject,

        # The thread body
        $body
    )
    if ([string]::IsNullOrWhitespace($organization)) { throw "-organization must not be null or whitespace" }
    if ([string]::IsNullOrWhitespace($project)) { throw "-project must not be null or whitespace" }
    if ($null -eq $pullRequestId) { throw "-pullRequestId must be specified" }

    if ([string]::IsNullOrWhiteSpace($repo)) {
        $pr = Get-PullRequest -pullRequestId $pullRequestId -organization $organization -project $project
        $repo = $pr.repository.name
    }

    return Invoke-RestMethodWithRetry -method "Post" -uri "https://dev.azure.com/$organization/$project/_apis/git/repositories/$repo/pullRequests/$pullRequestId/threads?api-version=7.0" -body $body -logBody
}
Export-ModuleMember -Function New-PullRequestThread

# Copies the MerlinBot Useful/not useful buttons
# They look a lot better than 👍👎 and it makes the voting experience more consistent
function Format-PullRequestCommentVoteButtonHtml {
    [CmdletBinding()]
    param(
        [string]$usefulUrl,
        [string]$notUsefulUrl,
        [string]$email = "ocdevproductivity@microsoft.com",
        [string]$header,
        [string]$footer
    )
    $value = ""
    if (-not [string]::IsNullOrWhiteSpace($header)) {
        $value += "<p><small class=""secondary-text"">$header</small></p>"
    }
    $value = "<table style=""margin:24px 0 4px 0;border:1px solid rgba(0,0,0,0.08);padding:8px 12px;"">
<tr>
<td style=""padding:6px 0;border: none;white-space: nowrap"">
    <p style=""margin:0;"">Rate this:</p>
</td>
<td style=""padding: 6px 8px;border: none;"">
<p style=""margin:0;"">
    <a rel=""noopener noreferrer"" target=""_blank"" href=""$usefulUrl"" style=""background: rgba(0,0,0,0.06);padding:6px 12px;font-weight:600;cursor:pointer;text-decoration:none;"">Useful</a>
</p>
</td>
<td style=""padding:6px 8px 6px 0;border:none;white-space:nowrap"">
<p style=""margin:0;"">
    <a rel=""noopener noreferrer"" target=""_blank"" href=""$notUsefulUrl"" style=""background:rgba(0,0,0,0.06);padding:6px 12px;font-weight:600;cursor:pointer;text-decoration:none;"">Not useful</a>
</p>
</td>
<td width=""99%"" style=""padding:6px 0;border:none;text-align:right;"">
    <p style=""margin:0;"">Questions: <a href=""mailto:$email"">$email</a></p>
</td>
</tr>
</table>"
    if (-not [string]::IsNullOrWhiteSpace($footer)) {
        $value += "<p><small class=""secondary-text"">$footer</small></p>"
    }
    return $value -replace "`n","" -replace " +"," "
}
Export-ModuleMember -Function Format-PullRequestCommentVoteButtonHtml

# See https://learn.microsoft.com/rest/api/azure/devops/git/pull%20request%20threads/list?view=azure-devops-rest-6.0
function Get-PullRequestThreads {
    [CmdletBinding()]
    param(
        # The pull request id
        [nullable[int]]$pullRequestId,

        # The repository
        [string]$repo,
        
        # The organization
        [string]$organization = $script:defaultOrganization,
        
        # The project
        [string]$project = $script:defaultProject
    )
    if ([string]::IsNullOrWhitespace($organization)) { throw "-organization must not be null or whitespace" }
    if ([string]::IsNullOrWhitespace($project)) { throw "-project must not be null or whitespace" }
    if ($pullRequestId -le 0) { throw "-pullRequestId must be a positive number" }

    if ([string]::IsNullOrWhiteSpace($repo)) {
        $pr = Get-PullRequest -pullRequestId $pullRequestId -organization $organization -project $project
        $repo = $pr.repository.name
    }

    return Invoke-RestMethodWithRetry -method "GET" -uri "https://dev.azure.com/$organization/$project/_apis/git/repositories/$repo/pullrequests/$pullRequestId/threads`?api-version=6.0"
}
Export-ModuleMember -Function Get-PullRequestThreads

# See https://learn.microsoft.com/rest/api/azure/devops/git/pull-request-threads/create?view=azure-devops-rest-7.0&tabs=HTTP
function New-PullRequestThread {
    [CmdletBinding()]
    param(
        # The pull request id
        [nullable[int]]$pullRequestId,

        # The pull request repository - if not specified, this will be determined by fetching the pull request
        [string]$repo,
        
        # The organization
        [string]$organization = $script:defaultOrganization,
        
        # The project
        [string]$project = $script:defaultProject,

        # The thread body
        $body
    )
    if ([string]::IsNullOrWhitespace($organization)) { throw "-organization must not be null or whitespace" }
    if ([string]::IsNullOrWhitespace($project)) { throw "-project must not be null or whitespace" }
    if ($null -eq $pullRequestId) { throw "-pullRequestId must be specified" }

    if ([string]::IsNullOrWhiteSpace($repo)) {
        $pr = Get-PullRequest -pullRequestId $pullRequestId -organization $organization -project $project
        $repo = $pr.repository.name
    }

    return Invoke-RestMethodWithRetry -method "Post" -uri "https://dev.azure.com/$organization/$project/_apis/git/repositories/$repo/pullRequests/$pullRequestId/threads?api-version=7.0" -body $body -logBody
}
Export-ModuleMember -Function New-PullRequestThread

# See https://learn.microsoft.com/rest/api/azure/devops/git/pull-request-threads/update?view=azure-devops-rest-7.0
function Update-PullRequestThread {
    [CmdletBinding()]
    param(
        # The pull request id
        [nullable[int]]$pullRequestId,

        [nullable[int]]$threadId,

        # The pull request repository - if not specified, this will be determined by fetching the pull request
        [string]$repo,
        
        # The organization
        [string]$organization = $script:defaultOrganization,
        
        # The project
        [string]$project = $script:defaultProject,

        # The thread body
        $body
    )
    if ([string]::IsNullOrWhitespace($organization)) { throw "-organization must not be null or whitespace" }
    if ([string]::IsNullOrWhitespace($project)) { throw "-project must not be null or whitespace" }
    if ($null -eq $pullRequestId) { throw "-pullRequestId must be specified" }
    if ($null -eq $threadId) { throw "-threadId must be specified" }

    if ([string]::IsNullOrWhiteSpace($repo)) {
        $pr = Get-PullRequest -pullRequestId $pullRequestId -organization $organization -project $project
        $repo = $pr.repository.name
    }

    return Invoke-RestMethodWithRetry -method "Patch" -uri "https://dev.azure.com/$organization/$project/_apis/git/repositories/$repo/pullRequests/$pullRequestId/threads/$threadId`?api-version=7.0" -body $body -logBody
}
Export-ModuleMember -Function Update-PullRequestThread

# See https://learn.microsoft.com/rest/api/azure/devops/git/pull-request-thread-comments/update?view=azure-devops-rest-7.0
function Update-PullRequestThreadComment {
    [CmdletBinding()]
    param(
        # The pull request id
        [nullable[int]]$pullRequestId,

        # The thread id
        [nullable[int]]$threadId,

        # The comment id
        [nullable[int]]$commentId,

        # The pull request repository - if not specified, this will be determined by fetching the pull request
        [string]$repo,
        
        # The organization
        [string]$organization = $script:defaultOrganization,
        
        # The project
        [string]$project = $script:defaultProject,

        # The thread comment body
        $body
    )
    if ([string]::IsNullOrWhitespace($organization)) { throw "-organization must not be null or whitespace" }
    if ([string]::IsNullOrWhitespace($project)) { throw "-project must not be null or whitespace" }
    if ($null -eq $pullRequestId) { throw "-pullRequestId must be specified" }
    if ($null -eq $threadId) { throw "-threadId must be specified" }

    if ([string]::IsNullOrWhiteSpace($repo)) {
        $pr = Get-PullRequest -pullRequestId $pullRequestId -organization $organization -project $project
        $repo = $pr.repository.name
    }

    return Invoke-RestMethodWithRetry -method "Patch" -uri "https://dev.azure.com/$organization/$project/_apis/git/repositories/$repo/pullRequests/$pullRequestId/threads/$threadId/comments/$commentId`?api-version=7.0" -body $body -logBody
}
Export-ModuleMember -Function Update-PullRequestThreadComment

# See https://learn.microsoft.com/rest/api/azure/devops/git/pull-request-thread-comments/create?view=azure-devops-rest-7.0&tabs=HTTP
function New-PullRequestThreadComment {
    [CmdletBinding()]
    param(
        # The pull request id
        [nullable[int]]$pullRequestId,

        # The pull request thread id
        [nullable[int]]$threadId,

        # The pull request repository - if not specified, this will be determined by fetching the pull request
        [string]$repo,
        
        # The organization
        [string]$organization = $script:defaultOrganization,
        
        # The project
        [string]$project = $script:defaultProject,
        
        # The thread comment body
        $body
    )
    if ([string]::IsNullOrWhitespace($organization)) { throw "-organization must not be null or whitespace" }
    if ([string]::IsNullOrWhitespace($project)) { throw "-project must not be null or whitespace" }
    if ($null -eq $pullRequestId) { throw "-pullRequestId must be specified" }
    if ($null -eq $threadId) { throw "-threadId must be specified" }

    if ([string]::IsNullOrWhiteSpace($repo)) {
        $pr = Get-PullRequest -pullRequestId $pullRequestId -organization $organization -project $project
        $repo = $pr.repository.name
    }

    return Invoke-RestMethodWithRetry -method "Post" -uri "https://dev.azure.com/$organization/$project/_apis/git/repositories/$repo/pullRequests/$pullRequestId/threads/$threadId/comments?api-version=7.0" -organization $organization -body $body -logBody
}
Export-ModuleMember -Function New-PullRequestThreadComment

# See https://learn.microsoft.com/rest/api/azure/devops/policy/evaluations?view=azure-devops-rest-7.0
function Get-PullRequestEvaluations {
    [CmdletBinding()]
    param(
        # The pull request id
        [int]$pullRequestId,

        [string]$organization = $script:defaultOrganization,
        [string]$project = $script:defaultProject
    )

    if ([string]::IsNullOrWhitespace($organization)) { throw "The `$organization must not be null or whitespace" }
    if ([string]::IsNullOrWhitespace($project)) { throw "The `$project must not be null or whitespace" }
    if ($pullRequestId -le 0) { throw "The `$pullRequestId must be a positive number" }

    $projectId = [guid]::Empty
    if (-not [Guid]::TryParse($project, [ref]$projectId)) {
        $projectId = (Get-Projects -organization $organization | Where-Object { $_.name -eq $project }).id
    }
    $artifactId = "vstfs:///CodeReview/CodeReviewId/$projectId/$pullRequestId"
    return Invoke-RestMethodWithRetry -method "GET" -uri "https://dev.azure.com/$organization/$project/_apis/policy/evaluations?artifactId=$artifactId&api-version=5.0-preview.1"
    
}
Export-ModuleMember -Function Get-PullRequestEvaluations

# See https://learn.microsoft.com/rest/api/azure/devops/policy/Evaluations/Requeue%2520Policy%2520Evaluation?view=azure-devops-rest-5.0
function Invoke-Evaluation {
    [CmdletBinding()]
    param(
        [string]$organization = $script:defaultOrganization,
        [string]$project = $script:defaultProject,
        [string]$evaluationId
    )

    if ([string]::IsNullOrWhitespace($organization)) { throw "The `$organization must not be null or whitespace" }
    if ([string]::IsNullOrWhitespace($project)) { throw "The `$project must not be null or whitespace" }
    if ([string]::IsNullOrWhitespace($evaluationId)) { throw "The `$project must not be null or whitespace" }

    return Invoke-RestMethodWithRetry -method "PATCH" -uri "https://dev.azure.com/$organization/$project/_apis/policy/evaluations/$evaluationId`?api-version=5.0-preview.1"
}
Export-ModuleMember -Function Invoke-Evaluation

# Sets the status details for a status check on a pull request
function Update-PullRequestStatus {
    [CmdletBinding()]
    param(
        [string]$organization = $script:defaultOrganization,
        [string]$project = $script:defaultProject,
        [string]$repo,
        [int]$pullRequestId = -1,
        [string]$name,
        
        [ValidateSet("Error","Failed","NotApplicable","NotSet","Pending","Succeeded")]
        [string]$state,

        [string]$description,
        [string]$targetUrl,
        
        [string]$genre,
        [nullable[int]]$iteration
    )

    if ([string]::IsNullOrWhitespace($organization)) { throw "The `$organization must not be null or whitespace" }
    if ([string]::IsNullOrWhitespace($project)) { throw "The `$project must not be null or whitespace" }
    if ([string]::IsNullOrWhitespace($repo)) { throw "The `$repo must not be null or whitespace" }
    if ($pullRequestId -lt 1) { throw "The `$pullRequestId must not be greater than 0" }
    if ([string]::IsNullOrWhitespace($name)) { throw "The `$name must not be null or whitespace" }
    if ([string]::IsNullOrWhitespace($state)) { throw "The `$state must not be null or whitespace" }
    if ([string]::IsNullOrWhitespace($description)) { throw "The `$description must not be null or whitespace" }
    if ([string]::IsNullOrWhitespace($targetUrl)) { throw "The `$targetUrl must not be null or whitespace" }
    
    $body = @{
        State = $state;
        Description = $description;
        TargetUrl = $targetUrl;
        Context = @{
            Name = $name;
        }
    }
    if (-not [string]::IsNullOrWhiteSpace($genre)) {
        $body.Context.Genre = $genre
    }
    if ($null -ne $iteration) {
        $body.IterationId = $iteration.ToString()
    }
    return Invoke-RestMethodWithRetry -method "POST" -uri "https://dev.azure.com/$organization/$project/_apis/git/repositories/$repo/pullRequests/$pullRequestId/statuses`?api-version=6.0-preview.1" -body $body
}
Export-ModuleMember -Function Update-PullRequestStatus

function Get-Wiki {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory=$false, ParameterSetName="single")]
        [Parameter(Mandatory=$false, ParameterSetName="all")]
        [string]$organization = $script:defaultOrganization,

        [Parameter(Mandatory=$false, ParameterSetName="single")]
        [Parameter(Mandatory=$false, ParameterSetName="all")]
        [string]$project = $script:defaultProject,
        
        [Parameter(Mandatory=$true,Position=0,ParameterSetName="single")]
        [string]$wikiIdentifier,

        [Parameter(Mandatory=$false,ParameterSetName="single")]
        [string]$path,

        [Parameter(Mandatory=$false,ParameterSetName="single")]
        [ValidateSet("full", "none", "oneLevel", "oneLevelPlusNestedEmptyFolders")]
        [string]$recursionLevel = "full",

        [Parameter(Mandatory=$false,ParameterSetName="single")]
        [switch]$includeContent
    )

    if ([string]::IsNullOrWhitespace($organization)) { throw "The `$organization must not be null or whitespace" }
    if ([string]::IsNullOrWhitespace($project)) { throw "The `$project must not be null or whitespace" }

    if ($PsCmdlet.ParameterSetName -eq "single") {
        if ([string]::IsNullOrWhitespace($wikiIdentifier)) { throw "The `$wikiIdentifier must not be null or whitespace" }
        $url = "https://dev.azure.com/$organization/$project/_apis/wiki/wikis/$wikiIdentifier/pages`?api-version=6.0"
        if ($includeContent) {
            $url += "&includeContent=true"
        }
        if (-not [string]::IsNullOrWhiteSpace($recursionLevel)) {
            $url += "&recursionLevel=$recursionLevel"
        }
        if (-not [string]::IsNullOrWhiteSpace($path)) {
            $url += "&path=$path"
        }
    } else {
        $url = "https://dev.azure.com/$organization/$project/_apis/wiki/wikis`?api-version=6.0"
    }

    return Invoke-RestMethodWithRetry -method "GET" -uri $url
}
Export-ModuleMember -Function Get-Wiki

# See https://learn.microsoft.com/rest/api/azure/devops/wit/classification%20nodes/get%20classification%20nodes?view=azure-devops-rest-6.0
function Get-WorkItemClassificationNodes {
    [CmdletBinding()]
    param(
        # Integer classification nodes ids. It's not required, if you want root nodes.
        [int[]]$ids,

        [int]$depth = 1000,

        [ValidateSet("fail", "omit")]
        [string]$errorPolicy = "omit",

        [switch]$flat,

        # The Azure DevOps organization
        [string]$organization = $script:defaultOrganization,
        
        # The Azure DevOps organization project
        [string]$project = $script:defaultProject
    )

    if ([string]::IsNullOrWhitespace($organization)) { throw "The `$organization must not be null or whitespace" }
    if ([string]::IsNullOrWhitespace($project)) { throw "The `$project must not be null or whitespace" }

    $url = "https://dev.azure.com/$organization/$project/_apis/wit/classificationnodes`?`$depth=$depth&errorPolicy=$errorPolicy&api-version=6.0"
    if ($PsBoundParameters.ContainsKey("ids")) {
        $url += "&ids=$($ids -join ",")"
    }

    $result = Invoke-RestMethodWithRetry -method "Get" -uri $url

    if ($flat) {
        function Get-ChildAreaPaths {
            [CmdletBinding()]
            param(
                $area
            )

            if ($null -eq $area) {
                return
            }

            $area.path -replace "^\\",""

            if (-not $area.hasChildren) {
                return
            }

            $area.children | ForEach-Object {
                Get-ChildAreaPaths $_
            }
        }
        $result | ForEach-Object {
            Get-ChildAreaPaths $_
        }
        return
    }
    return $result
}
Export-ModuleMember -Function Get-WorkItemClassificationNodes

# Gets work items - see https://learn.microsoft.com/rest/api/azure/devops/wit/work%20items?view=azure-devops-rest-6.0
# See also https://learn.microsoft.com/rest/api/azure/devops/wit/work%20items/list?view=azure-devops-rest-6.0
function Get-WorkItem {
    [CmdletBinding()]
    param(
        # The work item ids
        [int[]]$id,

        # The work item fields
        [string[]]$fields,

        # AsOf UTC date time string
        [DateTime]$asOf,

        # The expand parameters for work item attributes
        [ValidateSet("None","Relations","Fields","Links","All")]
        [string]$expand = "None",

        # The Azure DevOps organization
        [string]$organization = $script:defaultOrganization,
        
        # The Azure DevOps organization project
        [string]$project = $script:defaultProject
    )

    if ([string]::IsNullOrWhitespace($organization)) { throw "The `$organization must not be null or whitespace" }
    if ([string]::IsNullOrWhitespace($project)) { throw "The `$project must not be null or whitespace" }

    for ($i = 0; $i -lt $id.Count; $i += 200) {
        [array]$idPart = $id | Select-Object -First 200 -Skip ($i * 200)
        if ($idPart.Count -gt 1) {
            $url = "https://dev.azure.com/$organization/$project/_apis/wit/workitems?ids=$($idPart -join ",")&`$expand=$expand&api-version=6.0"
        } else {
            $url = "https://dev.azure.com/$organization/$project/_apis/wit/workitems/$idPart`?`$expand=$expand&api-version=6.0"
        }
        if ($PsBoundParameters.ContainsKey("fields")) {
            $url += "&fields=$($fields -join ",")"
        }
        if ($PsBoundParameters.ContainsKey("asOf")) {
            $url += "&asOf=$([System.Web.HttpUtility]::UrlEncode(($asOf.ToUniversalTime().ToString("o"))))"
        }

        return Invoke-RestMethodWithRetry -method "Get" -uri $url
    }
}
Export-ModuleMember -Function Get-WorkItem

# Gets work items - see https://learn.microsoft.com/rest/api/azure/devops/wit/work%20items?view=azure-devops-rest-6.0

# See https://learn.microsoft.com/rest/api/azure/devops/wit/wiql/query%20by%20wiql?view=azure-devops-rest-6.0
function Find-WorkItems {
    [CmdletBinding()]
    param(
        # The project team
        [string]$team = "CRM.Services.OmniChannel",

        # The max number of results to return
        [int]$top = 10000,

        # Whether or not to use time precision
        [switch]$timePrecision,

        # The work item wiql query
        [string]$query,

        # The Azure DevOps organization
        [string]$organization = $script:defaultOrganization,
        
        # The Azure DevOps organization project
        [string]$project = $script:defaultProject
    )

    if ([string]::IsNullOrWhitespace($organization)) { throw "-organization must not be null or whitespace" }
    if ([string]::IsNullOrWhitespace($project)) { throw "-project must not be null or whitespace" }
    if ([string]::IsNullOrWhitespace($team)) { throw "-team must not be null or whitespace" }
    if ([string]::IsNullOrWhitespace($query)) { throw "-query must not be null or whitespace" }
    if ($query.Length -gt 32000) { throw "-query must not exceed 32k characters"}

    $url = "https://dev.azure.com/$organization/$project/$team/_apis/wit/wiql?api-version=6.0"
    if ($top -gt 0) {
        $url += "&`$top=$top"
    }
    if ($timePrecision) {
        $url += "&timePrecision=$timePrecision"
    }

    Invoke-RestMethodWithRetry -method "Post" -uri $url -body @{
        query = $query;
    }
}
Export-ModuleMember -Function Find-WorkItems

# Creates a new work item - https://learn.microsoft.com/rest/api/azure/devops/wit/work%20items/create?view=azure-devops-rest-6.0
function New-WorkItem {
    [CmdletBinding()]
    param(
        # The work item type of the work item to create
        [string]$type,

        # Indicate if you only want to validate the changes without saving the work item
        [switch]$validateOnly,

        # Do not enforce the work item type rules on this update
        [switch]$bypassRules,

        # Do not fire any notifications for this change
        [switch]$suppressNotifications,

        # The expand parameters for work item attributes. Possible options are { None, Relations, Fields, Links, All }.
        [ValidateSet("None", "Relations", "Fields", "Links", "All")]
        [string]$expand = "All",

        # The work item fields
        $fields,

        # The work item links - see https://learn.microsoft.com/azure/devops/boards/queries/link-type-reference?view=azure-devops#example
        $links,

        # The Azure DevOps organization
        [string]$organization = $script:defaultOrganization,
        
        # The Azure DevOps organization project
        [string]$project = $script:defaultProject
    )

    if ([string]::IsNullOrWhiteSpace($organization)) { throw "-organization must not be null or whitespace" }
    if ([string]::IsNullOrWhiteSpace($project)) { throw "-project must not be null or whitespace" }
    if ([string]::IsNullOrWhiteSpace($type)) { throw "-type must not be null or whitespace" }
    if ($null -eq $fields) { throw "-fields must not be null" }
    if (-not $fields.Contains("System.Title")) { throw "-fields must specify System.Title" }

    $url = "https://dev.azure.com/$organization/$project/_apis/wit/workitems/`$$type`?api-version=6.0"
    if ($validateOnly) {
        $url += "&validateOnly=$validateOnly"
    }
    if ($bypassRules) {
        $url += "&bypassRules=$bypassRules"
    }
    if ($suppressNotifications) {
        $url += "&suppressNotifications=$suppressNotifications"
    }
    if ($expand) {
        $url += "&`$expand=$expand"
    }

    $body = @()
    $fields.Keys | ForEach-Object {
        $body += @{
            op = "add";
            path = "/fields/$_";
            value = $fields.$_;
        }
    }
    if ($null -ne $links) {
        $links.Keys | ForEach-Object {
            $body += @{
                op = "add";
                path = "/relations/-";
                value = @{
                    rel = $_
                    url = "https://dev.azure.com/$organization/$project/_apis/wit/workItems/$($links.$_)"
                }
            }
        }
    }
    return Invoke-RestMethodWithRetry -method "Post" -uri $url -body $body -contentType "application/json-patch+json"
}
Export-ModuleMember -Function New-WorkItem

# Updates a work item - https://learn.microsoft.com/rest/api/azure/devops/wit/work%20items/update?view=azure-devops-rest-6.0
function Update-WorkItem {
    [CmdletBinding()]
    param(
        # The work item type of the work item to create
        [int]$id,

        # Indicate if you only want to validate the changes without saving the work item
        [switch]$validateOnly,

        # Do not enforce the work item type rules on this update
        [switch]$bypassRules,

        # Do not fire any notifications for this change
        [switch]$suppressNotifications,

        # The expand parameters for work item attributes. Possible options are { None, Relations, Fields, Links, All }.
        [ValidateSet("None", "Relations", "Fields", "Links", "All")]
        [string]$expand = "All",

        # The work item fields
        $fields,

        # The work item links - see https://learn.microsoft.com/azure/devops/boards/queries/link-type-reference?view=azure-devops#example
        $links,

        # The Azure DevOps organization
        [string]$organization = $script:defaultOrganization,
        
        # The Azure DevOps organization project
        [string]$project = $script:defaultProject
    )

    if ([string]::IsNullOrWhiteSpace($organization)) { throw "-organization must not be null or whitespace" }
    if ([string]::IsNullOrWhiteSpace($project)) { throw "-project must not be null or whitespace" }
    if ($id -le 0) { throw "-id must be a positive number" }

    $url = "https://dev.azure.com/$organization/$project/_apis/wit/workitems/$id`?api-version=6.0"
    if ($validateOnly) {
        $url += "&validateOnly=$validateOnly"
    }
    if ($bypassRules) {
        $url += "&bypassRules=$bypassRules"
    }
    if ($suppressNotifications) {
        $url += "&suppressNotifications=$suppressNotifications"
    }
    if ($expand) {
        $url += "&`$expand=$expand"
    }

    $body = @()
    if ($null -ne $fields) {
        $fields.Keys | ForEach-Object {
            $body += @{
                op = "add";
                path = "/fields/$_";
                value = $fields.$_;
            }
        }
    }
    if ($null -ne $links) {
        $links.Keys | ForEach-Object {
            $body += @{
                op = "add";
                path = "/relations/-";
                value = @{
                    rel = $_
                    url = "https://dev.azure.com/$organization/$project/_apis/wit/workItems/$($links.$_)"
                }
            }
        }
    }

    return Invoke-RestMethodWithRetry -method "Patch" -uri $url -body $body -ContentType "application/json-patch+json"
}
Export-ModuleMember -Function Update-WorkItem

# Gets a work item comment - https://learn.microsoft.com/en-us/rest/api/azure/devops/wit/comments/get-comment?view=azure-devops-rest-7.0&tabs=HTTP
function Get-WorkItemComment {
    [CmdletBinding()]
    param(
        # The work item type of the work item to create
        [nullable[int]]$id,

        [nullable[int]]$commentId,

        # The Azure DevOps organization
        [string]$organization = $script:defaultOrganization,
        
        # The Azure DevOps organization project
        [string]$project = $script:defaultProject
    )

    if ([string]::IsNullOrWhiteSpace($organization)) { throw "-organization must not be null or whitespace" }
    if ([string]::IsNullOrWhiteSpace($project)) { throw "-project must not be null or whitespace" }
    if ($id -le 0) { throw "-id must be a positive number" }
    if ($commentId -le 0) { throw "-commentId must be a positive number" }

    $url = "https://dev.azure.com/$organization/$project/_apis/wit/workItems/$id/comments/$commentId`?api-version=7.0-preview.3"
    return Invoke-RestMethodWithRetry -method "Get" -uri $url
}
Export-ModuleMember -Function Get-WorkItemComment

<# Updates a work item - https://learn.microsoft.com/rest/api/azure/devops/wit/comments/update?view=azure-devops-rest-7.0&tabs=HTTP
.EXAMPLE
ocli
Connect-OcSubscriptions
$env:System_AccessToken = $env:System_AccessToken_dynamicscrm = (Get-AzAccessToken).token
$text = Get-WorkItemComment -id 4899352 -commentId 9781734
Update-WorkItemComment -id 4899352 -commentId 9781734 -text ($text.text -replace "<token>","[redacted]")
#>
function Update-WorkItemComment {
    [CmdletBinding()]
    param(
        # The work item type of the work item to create
        [nullable[int]]$id,

        [nullable[int]]$commentId,

        [string]$text,

        # The Azure DevOps organization
        [string]$organization = $script:defaultOrganization,
        
        # The Azure DevOps organization project
        [string]$project = $script:defaultProject
    )

    if ([string]::IsNullOrWhiteSpace($organization)) { throw "-organization must not be null or whitespace" }
    if ([string]::IsNullOrWhiteSpace($project)) { throw "-project must not be null or whitespace" }
    if ($id -le 0) { throw "-id must be a positive number" }
    if ($commentId -le 0) { throw "-commentId must be a positive number" }
    if ([string]::IsNullOrWhiteSpace($text)) { throw "-text must not be null or whitespace" }
    $body = @{ text = $text }

    $url = "https://dev.azure.com/$organization/$project/_apis/wit/workItems/$id/comments/$commentId`?api-version=7.0-preview.3"
    return Invoke-RestMethodWithRetry -method "Patch" -uri $url -body $body -ContentType "application/json"
}
Export-ModuleMember -Function Update-WorkItemComment

# Deletes a work item - https://learn.microsoft.com/rest/api/azure/devops/wit/work%20items/delete?view=azure-devops-rest-6.0
function Remove-WorkItem {
    [CmdletBinding()]
    param(
        # The work item type of the work item to create
        [int]$id,

        # Indicate if you only want to validate the changes without saving the work item
        [switch]$destroy,

        # The Azure DevOps organization
        [string]$organization = $script:defaultOrganization,
        
        # The Azure DevOps organization project
        [string]$project = $script:defaultProject
    )

    if ([string]::IsNullOrWhiteSpace($organization)) { throw "-organization must not be null or whitespace" }
    if ([string]::IsNullOrWhiteSpace($project)) { throw "-project must not be null or whitespace" }
    if ($id -le 0) { throw "-id must be a positive number" }

    $url = "https://dev.azure.com/$organization/$project/_apis/wit/workitems/$id`?api-version=6.0"
    if ($destroy) {
        $url += "&destroy=$destroy"
    }
    return Invoke-RestMethodWithRetry -method "Delete" -uri $url
}
Export-ModuleMember -Function Remove-WorkItem

function Close-WorkItem {
    [CmdletBinding()]
    param(
        # The ID of the work item to close
        [Parameter(Mandatory = $true)]
        [int]$id,

        # The Azure DevOps organization
        [string]$organization = $script:defaultOrganization,

        # The Azure DevOps project
        [string]$project = $script:defaultProject,

        # Optional comment to add when closing the work item
        [string]$comment = "Work item closed via script."
    )

    if ([string]::IsNullOrWhiteSpace($organization)) { throw "-organization must not be null or whitespace" }
    if ([string]::IsNullOrWhiteSpace($project)) { throw "-project must not be null or whitespace" }
    if ($id -le 0) { throw "-id must be a positive number" }

    $url = "https://dev.azure.com/$organization/$project/_apis/wit/workitems/$id`?api-version=6.0"

    $body = @(
        @{
            op    = "add";
            path  = "/fields/System.State";
            value = "Closed";
        },
        @{
            op    = "add";
            path  = "/fields/System.History";
            value = $comment;
        }
    )

    try {
        $response = Invoke-RestMethodWithRetry -method "PATCH" -uri $url -body $body -ContentType "application/json-patch+json"
        Write-Info "Work item $id successfully closed."
        return $response
    } catch {
        throw Write-PipelineIssue -errorMessage "Failed to close work item id: $id" -exception $_ 6>$null
    }
}
Export-ModuleMember -Function Close-WorkItem

function Invoke-WorkItem {
    [CmdletBinding()]
    param(
        [string]$title,
        [string]$description,
        [string]$areaPath,
        [string]$queryCondition,
        [string]$type = "Bug"
    )

    try {
        # Query existing work items
        $items = Find-WorkItems -query "SELECT Id FROM WorkItems WHERE $queryCondition"
        $fields = @{
            "System.Title" = $title
            "Microsoft.VSTS.TCM.ReproSteps" = $description
            "System.AreaPath" = $areaPath
        }

        if ($items.workItems.Count -eq 0) {
            # Create a new work item
            $workItem = New-WorkItem -fields $fields -Type $type
            return $workItem
        } else {
            # Update the existing work item
            $workItem = Get-WorkItem -id $items.workItems.id
            $combinedDescription = $workItem.fields."Microsoft.VSTS.TCM.ReproSteps" + "<br/><br/>" + $description
            $workItem = Update-WorkItem -id $items.workItems.id -fields @{
                "Microsoft.VSTS.TCM.ReproSteps" = $combinedDescription
                "System.Title" = $title
            }
            return $workItem
        }
    } catch {
        throw Write-PipelineIssue -errorMessage "Failed to handle work item" -exception $_ 6>$null
    }
}
Export-ModuleMember -Function Invoke-WorkItem

# Punches through the cache to force update known package versions - see https://www.1eswiki.com/wiki/How_to_Add_Upstream_Sources_to_Azure_Artifacts_Feed
function Reset-NpmPackage {
    [CmdletBinding()]
    param(
        [string]$feedId = "3c4e535e-714c-4dae-bdba-23aa323b47c9" <# OneCrm.Omnichannel #>,
        [string]$packageScope,
        [string]$packageName,
        [string]$packageVersion,

        [string]$organization = $script:defaultOrganization,
        [string]$project
    )

    if ([string]::IsNullOrWhitespace($feedId)) { throw "-feedId must not be null or whitespace" }
    if ([string]::IsNullOrWhitespace($packageName)) { throw "-packageName must not be null or whitespace" }
    if ([string]::IsNullOrWhitespace($packageVersion)) { throw "-packageVersion must not be null or whitespace" }
    if ([string]::IsNullOrWhitespace($organization)) { throw "-organization must not be null or whitespace" }

    $url = "https://pkgs.dev.azure.com/$organization"
    if (-not [string]::IsNullOrWhiteSpace($project)) {
        $url += "/$project"
    }
    $url += "/_apis/packaging/feeds/$feedId/npm/packages/"
    if (-not [string]::IsNullOrWhiteSpace($packageScope)) {
        $url += "$packageScope/"
    }
    $url += "$([System.Web.HttpUtility]::UrlEncode($packageName))/versions/$packageVersion/content"
    return Invoke-RestMethodWithRetry -method "Head" -uri $url
}
Export-ModuleMember -Function Reset-NpmPackage

function Get-AdoPackages {
    [CmdletBinding()]
    param(
        [string]$feedId = "3c4e535e-714c-4dae-bdba-23aa323b47c9" <# OneCrm.Omnichannel #>,

        [string]$organization = $script:defaultOrganization,
        [string]$project
    )

    if ([string]::IsNullOrWhitespace($organization)) { throw "-organization must not be null or whitespace" }

    if ([string]::IsNullOrWhitespace($feedId)) { throw "-feedId must not be null or whitespace" }

    $url = "https://feeds.dev.azure.com/$organization"
    if (-not [string]::IsNullOrWhiteSpace($project)) {
        $url += "/$project"
    }
    $url += "/_apis/packaging/feeds/$feedId/packages?api-version=6.1-preview.1&`$top=100000"
    return Invoke-RestMethodWithRetry -method "Get" -uri $url
}
Export-ModuleMember -Function Get-AdoPackages

<#
.SYNOPSIS
See https://www.1eswiki.com/wiki/Azure_Artifacts-_Upstream_Behavior_change - this will allow external versions of packages with "internal" versions (including upstreamed packages from other internal feeds which may be external)
.EXAMPLE
Get-AdoPackages | Where-Object {
    $_.protocolType -eq "Npm"
} | Invoke-ForEachSection { param($package) "$($package.name)" } {
    param($package)
    Update-AdoPackageUpstreaming -packageSource $package.protocolType -packageName $package.normalizedName -versionsFromExternalUpstreams allowExternalVersions
}
#>
function Update-AdoPackageUpstreaming {
    [CmdletBinding()]
    param(
        [string]$feedId = "3c4e535e-714c-4dae-bdba-23aa323b47c9" <# OneCrm.Omnichannel #>,
        
        # The package source. maven and pypi are also supported, but not by this cmdlet right now - https://www.1eswiki.com/wiki/Azure_Artifacts-_Upstream_Behavior_change
        [ValidateSet("npm","nuget")]
        [string]$packageSource,

        # The package name
        [string]$packageName,

        [ValidateSet("allowExternalVersions", "auto")]
        [string]$versionsFromExternalUpstreams = "allowExternalVersions",

        [string]$organization = $script:defaultOrganization,
        [string]$project
    )

    if ([string]::IsNullOrWhitespace($organization)) { throw "-organization must not be null or whitespace" }

    if ([string]::IsNullOrWhitespace($feedId)) { throw "-feedId must not be null or whitespace" }
    if ([string]::IsNullOrWhitespace($packageSource)) { throw "-packageSource must not be null or whitespace" }
    if ([string]::IsNullOrWhitespace($packageName)) { throw "-packageName must not be null or whitespace" }

    $url = "https://pkgs.dev.azure.com/$organization"
    if (-not [string]::IsNullOrWhiteSpace($project)) {
        $url += "/$project"
    }
    $url += "/_apis/packaging/feeds/$feedId/$packageSource/packages/$packageName/upstreaming?api-version=6.1-preview.1"
    $null =  Invoke-RestMethodWithRetry -method "Patch" -uri $url -body @{ versionsFromExternalUpstreams = $versionsFromExternalUpstreams }
}
Export-ModuleMember -Function Update-AdoPackageUpstreaming

# See https://learn.microsoft.com/rest/api/azure/devops/distributedtask/yamlschema/get?preserve-view=true&view=azure-devops-rest-5.1
function Get-YamlSchema {
    [CmdletBinding()]
    param(
        [string] $organization = $script:defaultOrganization
    )
    if ([string]::IsNullOrWhitespace($organization)) { throw "The `$organization must not be null or whitespace" }

    return Invoke-RestMethodWithRetry -method "Get" -uri "https://dev.azure.com/$organization/_apis/distributedtask/yamlschema?api-version=5.1"
}
Export-ModuleMember -Function Get-YamlSchema

# See https://learn.microsoft.com/rest/api/azure/devops/distributedtask/variablegroups/get?view=azure-devops-rest-6.0
function Get-VariableGroup {
    [CmdletBinding()]
    param(
        [string]$groupId,

        [string]$organization = $script:defaultOrganization,
        [string]$project = $script:defaultProject
    )

    if ([string]::IsNullOrWhitespace($organization)) { throw "-organization must not be null or whitespace" }
    if ([string]::IsNullOrWhitespace($project)) { throw "-project must not be null or whitespace" }
    if ([string]::IsNullOrWhitespace($groupId)) { throw "-groupId must not be null or whitespace" }

    return Invoke-RestMethodWithRetry -method "Get" -uri "https://dev.azure.com/$organization/$project/_apis/distributedtask/variablegroups/$groupId`?api-version=6.0-preview.2"
}
Export-ModuleMember -Function Get-VariableGroup

# See https://learn.microsoft.com/rest/api/azure/devops/distributedtask/variablegroups/update?view=azure-devops-rest-6.0
function Update-VariableGroup {
    [CmdletBinding()]
    param(
        $content,

        [string]$organization = $script:defaultOrganization,
        [string]$project = $script:defaultProject
    )

    if ([string]::IsNullOrWhitespace($organization)) { throw "-organization must not be null or whitespace" }
    if ([string]::IsNullOrWhitespace($project)) { throw "-project must not be null or whitespace" }

    $groupId = $content.id
    if ([string]::IsNullOrWhitespace($groupId)) { throw "-groupId must not be null or whitespace" }

    return Invoke-RestMethodWithRetry -method "Put" -uri "https://dev.azure.com/$organization/$project/_apis/distributedtask/variablegroups/$groupId`?api-version=6.0-preview.2" -body $content
}
Export-ModuleMember -Function Update-VariableGroup

# Gets the repo policies for the specified branch
# See # https://learn.microsoft.com/rest/api/azure/devops/git/policy-configurations/list?view=azure-devops-rest-5.0&viewFallbackFrom=azure-devops-rest-7.1
function Get-RepoPolicy {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory=$true, ParameterSetName="repo")]
        [string]$repo,

        [string]$branch,

        [string]$organization = $script:defaultOrganization,
        [string]$project = $script:defaultProject
    )

    $repoValue = Get-Repository -repo:$repo -organization:$organization -project:$project
    if ([string]::IsNullOrWhiteSpace($branch)) {
        $branch = $repo.defaultBranch
    }
    
    if ($branch -notmatch "^refs/"){
        $branch = "refs/heads/$branch"
    }

    $result = Invoke-RestMethodWithRetry -Method "Get" -uri "https://dev.azure.com/$organization/$project/_apis/git/policy/configurations?repositoryId=$($repoValue.id)&refName=$branch&api-version=5.0-preview.1"
    return $result
}
Export-ModuleMember -Function Get-RepoPolicy

# Creates a new repo policy
# Not documented
function New-RepoPolicy {
    [CmdletBinding()]
    param(
        <#
        e.g.
            {
                "type": {
                    "id": "cbdc66da-9728-4af8-aada-9a5a32e4a226"
                },
                "revision": 1,
                "isDeleted": false,
                "isBlocking": true,
                "isEnabled": true,
                "settings": {
                    "authorId": null,
                    "defaultDisplayName": null,
                    "invalidateOnSourceUpdate": false,
                    "policyApplicability": null,
                    "statusName": "codecoverage",
                    "statusGenre": "crm.omnichannel.sentimentanalysis",
                    "scope": [{
                            "repositoryId": "b13789f3-face-42d9-9a31-d79741c1efd9",
                            "refName": "refs/heads/master",
                            "matchKind": "Exact"
                        }
                    ]
                }
            }
        #>
        $body,

        [switch]$whatIf,

        [string]$organization = $script:defaultOrganization,
        [string]$project = $script:defaultProject
    )

    if ([string]::IsNullOrWhitespace($organization)) { throw "-organization must not be null or whitespace" }
    if ([string]::IsNullOrWhitespace($project)) { throw "-project must not be null or whitespace" }
    if ($null -eq $body) { throw "-body must be specified" }    
    
    return Invoke-RestMethodWithRetry -method "POST" -uri "https://dev.azure.com/$organization/$project/_apis/policy/Configurations?api-version=6.0" -body $body -logBody -whatIf:$whatIf
}
Export-ModuleMember -Function New-RepoPolicy

# Updates the specified repo policy
# Not documented
function Update-RepoPolicy {
    [CmdletBinding()]
    param(
        <#
        e.g.
            {
                "createdBy": {
                    "displayName": "Grant Borthwick 🍓",
                    "url": "https://spsprodwus21.vssps.visualstudio.com/A39971328-31a2-4457-896a-6cbe4d8b0aca/_apis/Identities/4fd263ad-41ad-6726-8f33-dff4353c1c89",
                    "_links": {
                        "avatar": {
                            "href": "https://dev.azure.com/dynamicscrm/_apis/GraphProfile/MemberAvatars/aad.NGZkMjYzYWQtNDFhZC03NzI2LThmMzMtZGZmNDM1M2MxYzg5"
                        }
                    },
                    "id": "4fd263ad-41ad-6726-8f33-dff4353c1c89",
                    "uniqueName": "SC-ey129@microsoft.com",
                    "imageUrl": "https://dev.azure.com/dynamicscrm/_api/_common/identityImage?id=4fd263ad-41ad-6726-8f33-dff4353c1c89",
                    "descriptor": "aad.NGZkMjYzYWQtNDFhZC03NzI2LThmMzMtZGZmNDM1M2MxYzg5"
                },
                "createdDate": "2023-03-22T23:05:18.563Z",
                "isEnabled": true,
                "isBlocking": true,
                "isDeleted": false,
                "settings": {
                    "statusName": "codecoverage",
                    "statusGenre": "crm.omnichannel.sentimentanalysis",
                    "authorId": null,
                    "invalidateOnSourceUpdate": false,
                    "defaultDisplayName": "SentimentAnalysis code coverage",
                    "policyApplicability": null,
                    "scope": [{
                            "refName": "refs/heads/master",
                            "matchKind": "Exact",
                            "repositoryId": "b13789f3-face-42d9-9a31-d79741c1efd9"
                        }
                    ]
                },
                "isEnterpriseManaged": false,
                "_links": {
                    "self": {
                        "href": "https://dev.azure.com/dynamicscrm/b276c3e1-2902-46bd-a686-484157b97f48/_apis/policy/configurations/91600"
                    },
                    "policyType": {
                        "href": "https://dev.azure.com/dynamicscrm/b276c3e1-2902-46bd-a686-484157b97f48/_apis/policy/types/cbdc66da-9728-4af8-aada-9a5a32e4a226"
                    }
                },
                "revision": 1,
                "id": 91600,
                "url": "https://dev.azure.com/dynamicscrm/b276c3e1-2902-46bd-a686-484157b97f48/_apis/policy/configurations/91600",
                "type": {
                    "id": "cbdc66da-9728-4af8-aada-9a5a32e4a226",
                    "url": "https://dev.azure.com/dynamicscrm/b276c3e1-2902-46bd-a686-484157b97f48/_apis/policy/types/cbdc66da-9728-4af8-aada-9a5a32e4a226",
                    "displayName": "Status"
                }
            }
        #>
        $body,

        [switch]$whatIf,

        [string]$organization = $script:defaultOrganization,
        [string]$project = $script:defaultProject
    )

    if ([string]::IsNullOrWhitespace($organization)) { throw "-organization must not be null or whitespace" }
    if ([string]::IsNullOrWhitespace($project)) { throw "-project must not be null or whitespace" }
    if ($null -eq $body) { throw "-body must be specified" }    

    $global:p = $body
    $policyId = $body.id

    return Invoke-RestMethodWithRetry -method "Put" -uri "https://dev.azure.com/$organization/$project/_apis/policy/Configurations/$policyId`?api-version=6.0" -body $body -logBody -whatIf:$whatIf
}
Export-ModuleMember -Function Update-RepoPolicy

# Removes the specified repo policy
# Not documented
function Remove-RepoPolicy {
    [CmdletBinding()]
    param(
        $policyId,

        [switch]$whatIf,

        [string]$organization = $script:defaultOrganization,
        [string]$project = $script:defaultProject
    )

    if ([string]::IsNullOrWhitespace($organization)) { throw "-organization must not be null or whitespace" }
    if ([string]::IsNullOrWhitespace($project)) { throw "-project must not be null or whitespace" }
    if ([string]::IsNullOrWhitespace($policyId)) { throw "-policyId must not be null or whitespace" }

    return Invoke-RestMethodWithRetry -method "Delete" -uri "https://dev.azure.com/$organization/$project/_apis/policy/Configurations/$policyId`?api-version=6.0" -whatIf:$whatIf
}
Export-ModuleMember -Function Update-RepoPolicy

function Update-RepoPolicyOrCreate {
    [CmdletBinding()]
    param(
        $body,

        $identifier,

        [scriptBlock]$matching,

        $policies,

        [string]$repo,

        [string[]]$branch,

        # Skips updating but logs the operation
        [switch]$whatIf,

        [string]$organization = $script:defaultOrganization,
        [string]$project = $script:defaultProject
    )

    if ([string]::IsNullOrWhitespace($organization)) { throw "-organization must not be null or whitespace" }
    if ([string]::IsNullOrWhitespace($project)) { throw "-project must not be null or whitespace" }
    if ([string]::IsNullOrWhitespace($identifier)) { throw "-identifier must not be null or whitespace" }

    if ($null -eq $body) {
        throw "-body must be specified"
    }

    if ($null -eq $policies) {
        $repoValue = Get-Repository -repo:$repo -organization:$organization -project:$project
        $policies = Get-RepoPolicy -repo $repoValue.name -branch $branch -organization:$organization -project:$project
    }

    $existing = $policies | Where-Object { & $matching $_ } | Select-Object -First 1
    if ($null -eq $existing) {
        Write-Info "Creating $identifier repo policy"
        try {
            $null = New-RepoPolicy -organization $organization -project $project -body $body -whatIf:$whatIf
        } catch {
            Write-PipelineIssue -warningMessage "Failed to create $identifier repo policy" -exception $_
        }
    } else {
        $before = ConvertTo-Json $existing -depth 100 -compress
        
        # Recursively update property values
        function Update-PropertyValues($value, $target) {
            $value.Keys | ForEach-Object {
                $key = $_
                if (($null -ne $value.$key) -and ($null -eq ($target.PSObject.Properties | Where-Object { $_.Name -eq $key }))) {
                    $null = $target | Add-Member -MemberType NoteProperty -Name $key -Value $value.$key -Force
                } elseif ($null -eq $value.$key) { # Ignore nulls
                } elseif ($value.$key -is [hashtable]) {
                    Update-PropertyValues $value.$key $target.$key
                } elseif ($value.$key -is [string] -or $value.$key.GetType().IsValueType) {
                    $target.$key = $value.$key
                } elseif ($value.$key -is [array]) {
                    if (($value.$key.Count -ne $target.$key.Count) -or ($value.$key[0] -is [string])) {
                        $target.$key = $value.$key
                    } else {
                        for ($i = 0; $i -lt $value.$key.Count; ++$i) {
                            Update-PropertyValues $value.$key[$i] $target.$key[$i]
                        }
                    }
                } else {
                    throw "Unknown type $($value.$key.GetType().FullName)"
                }
            }
        }
        $revision = $existing.revision
        Update-PropertyValues $body $existing

        $after = ConvertTo-Json $existing -depth 100 -compress
        if ($before -eq $after) {
            Write-Info "$identifier policy up-to-date"
        } else {
            $existing.revision = $revision + 1
            Write-Info "Updating $identifier policy"
            try {
                $null = Update-RepoPolicy -organization $organization -project $project -body $existing -whatIf:$whatIf
            } catch {
                Write-PipelineIssue -warningMessage "Failed to create $identifier repo policy" -exception $_
            }
        }
    }
}

$script:loadedYaml = $false
<#
.Synopsis
Sets up requirements for branch policies
Adds the pr validation pipeline
Adds code coverage check for every build
.Example
Get-Repository | Where-Object {
    -not $_.isDisabled
} | Where-Object {
    $_.name -match "((Omnichannel)|(UnifiedRouting.DecisionEngine))"
| | Where-Object {
   # Ignore A&E repos
   $_.name -notmatch "ConversationControl|CRM\.Solutions\.CustomControlsExtended|CRM\.Solutions\.MAgE|CRM\.OmniChannel\.CallingSDK|CRM\.Solutions\.CustomerServiceTrial|CRM\.OmniChannel\.LiveChatWidget|CRM\.Omnichannel\.ProductivityTools|CRM\.Solutions\.CEC|CRM\.Solutions\.ChannelApiFramework"
} | Sort-Object {
    $_.name
} | Update-OcBranchPolicies -branch master,main,releases/* -whatIf
#>
function Update-OcBranchPolicies {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory=$true, ValueFromPipeline=$true, ValueFromPipelineByPropertyName=$true)]
        [Alias("repo")]
        [string[]]$name,

        [string[]]$branch,

        # Skips updating but logs the operation
        [switch]$whatIf,

        [string]$organization = $script:defaultOrganization,
        [string]$project = $script:defaultProject
    )
    Begin {
        if ([string]::IsNullOrWhitespace($organization)) { throw "-organization must not be null or whitespace" }
        if ([string]::IsNullOrWhitespace($project)) { throw "-project must not be null or whitespace" }
        if ($null -eq $branch) { throw "-branch must not be specified" }
        if ($branch.Count -eq 0) { throw "-branch must not be specified" }

        $buildTypeId = "0609b952-1397-4640-95ec-e00a01b2c241"
        $statusTypeId = "cbdc66da-9728-4af8-aada-9a5a32e4a226"

        # [Crm.OmniChannel.PrValidation](https://dev.azure.com/dynamicscrm/OneCRM/_build?definitionId=18255)
        $prValidationDefinitionId = 18255
        $prValidationDefinition = Get-BuildDefinition -organization $organization -project $project -definitionId $prValidationDefinitionId
    }

    Process {
        if ([string]::IsNullOrWhiteSpace($name)) { throw "-repo must be specified" }
        $name | ForEach-Object {
            Get-Repository -repo $_ -organization:$organization -project:$project
        } | Invoke-ForEachSection { param($repoValue) $repoValue.name } {
            param($repoValue)

            $branch | ForEach-Object {
                if (-not $_.StartsWith("refs/heads/")) {
                    "refs/heads/$_"
                } else {
                    $_
                }
            } | Invoke-ForEachSection { param($branchName) "$($repoValue.name)|$branchName" } {
                param($branchName)

                $policies = Get-RepoPolicy -repo $repoValue.name -branch $branchName -organization:$organization -project:$project
                
                if ($branchName.EndsWith("*")) {
                    $matchKind = "Prefix"
                    $refName = $branchName -replace "\*$",""
                } else {
                    $matchKind = "Exact"
                    $refName = $branchName
                }
                $scope = @(@{
                    refName = $refName
                    matchKind = $matchKind
                    repositoryId = $repoValue.id
                })

                # Update pr path list
                $shortBranchName = $branchName -replace "refs/","" -replace "heads/",""
                if ($branchName -eq "refs/heads/releases/*") {
                    [array]$references = Get-RepoRef -repo $repoValue.id -filter "heads/releases/"
                    if ($null -eq $references -or $references.Count -eq 0) {
                        $shortBranchName = $repoValue.defaultBranch -replace "refs/","" -replace "heads/",""
                    } else {
                        $reference = $references.name | Where-Object {
                            $_ -match "releases/release_"
                        } | Sort-Object -Descending | Select-Object -First 1
                        if ($null -eq $reference) {
                            $reference = $references[0].name
                        }
                        $shortBranchName = $reference -replace "refs/","" -replace "heads/",""
                    }
                } else {
                    $ref = Get-RepoRef -repo $repoValue.id -filter "heads/$shortBranchName"
                    if ($null -eq $ref) {
                        Write-PipelineIssue -warningMessage "Failed to find $($repoValue.name) branch $branchName - Skipping"
                        return
                    }
                }
                Get-RepoFile -repo $repoValue.name -branch $shortBranchName | Where-Object {
                    $_.gitObjectType -eq "blob"
                } | Where-Object {
                    $_.path -match "\.ya?ml$" -and $_.path -notMatch "pipeline.user"
                } | ForEach-Object {
                    $path = $_.Path
                    $content = Get-RepoFile -repo $repoValue.id -branch $shortBranchName -repoFilePath $path
                    if ($content -notmatch "pr:") {
                        return
                    }
                    $originalContent = $content
                    $pipeline = $null
                    $firstException = $null
                    if (-not $script:loadedYaml) {
                        if ($null -eq (Get-Command ConvertFrom-Yaml -ErrorAction SilentlyContinue)) {
                            Write-Command "Install-Module -Name powershell-yaml -requiredVersion 0.4.4 -Force -Repository PSGallery -Scope CurrentUser -AllowClobber"
                            Install-Module -Name powershell-yaml -requiredVersion 0.4.4 -Force -Repository PSGallery -Scope CurrentUser -AllowClobber
                        }
                        $script:loadedYaml = $true
                    }
                    for ($i = 0; $i -lt 4; ++$i) {
                        try {
                            $pipeline = $content | ConvertFrom-Yaml -Ordered
                            break
                        } catch {
                            # Try removing BOM
                            $content = $content.Substring(1)
                            if ($null -ne $firstException) {
                                $firstException = $_
                            }
                        }
                    }
                    if ($null -eq $pipeline) {
                        Write-PipelineIssue -warningMessage "Failed to deserialize $path |`n`n$originalContent" -exception:$firstException
                    } else {
                        if ($pipeline.Contains("pr") -and $pipeline.pr.Contains("branches") -and $pipeline.pr.branches.Contains("include")) {
                            if ($pipeline.pr.branches.include -eq ($branchName -replace "refs/","" -replace "heads/","")) {
                                [array]$filenamePatterns = $null
                                if ($pipeline.pr.Contains("paths")) {
                                    [array]$filenamePatterns = ,$pipeline.pr.paths.include
                                    if ($pipeline.pr.paths.Contains("exclude")) {
                                        $filenamePatterns += ($pipeline.pr.paths.exclude | ForEach-Object { "!$_" })
                                    }
                                }

                                $definition = Get-BuildDefinition -repo $repoValue.id -yamlFileName $path.Trim("/")
                                if ($null -eq $definition) {
                                    Write-PipelineIssue -warningMessage "$($repoValue.name) has yaml $path with pr policies that are not part of a build definition"
                                    return
                                }
                                $null = Update-RepoPolicyOrCreate -body @{
                                    isEnabled = $true
                                    isBlocking = $true
                                    isDeleted = $false
                                    settings = @{
                                        buildDefinitionId = $definition.id
                                        queueOnSourceUpdateOnly = $false
                                        manualQueueOnly = $false
                                        displayName = $definition.name
                                        validDuration = [decimal]"0.0"
                                        scope = $scope
                                        filenamePatterns = $filenamePatterns
                                    }
                                    type = @{ id = $buildTypeId }
                                } -identifier "[$($repoValue.name)|$branchName] Build $($definition.name)" -matching {
                                    ($_.type.id -eq $buildTypeId) -and
                                    ($_.settings.buildDefinitionId -eq $definition.id)
                                } -policies $policies -repo $repoValue.name -repo $repoValue.id -branch $branch -whatIf:$whatIf -organization:$organization -project:$project
                            }
                        }
                    }
                }
                # Get latest policies
                $policies = Get-RepoPolicy -repo $repoValue.id -branch $branchName -organization:$organization -project:$project

                # Get build definitions
                [array]$buildPolicies = $policies | Where-Object {
                    ($_.type.id -eq $buildTypeId) -and 
                    # Skip code coverage check for the pr validation pipeline
                    ($_.settings.buildDefinitionId -ne $prValidationDefinitionId)
                } | ForEach-Object {
                    $policy = $_
                    try {
                    @{
                        policy = $policy
                        definition = Get-BuildDefinition -definitionId $policy.settings.buildDefinitionId -organization $organization -project $project -ErrorAction SilentlyContinue
                    }
                    } catch {
                        Write-PipelineIssue -warningMessage "Failed to find build definition $($policy.settings.buildDefinitionId) for policy $($policy)"
                        @{
                            policy = $policy
                            definition = $null
                        }
                    }
                } | Where-Object {
                    $null -ne $_.definition # skip invalid builds
                }
                
                # Add pr validation build - [Crm.OmniChannel.PrValidation](https://dev.azure.com/dynamicscrm/OneCRM/_build?definitionId=18255)
                # $null = Update-RepoPolicyOrCreate -body @{
                #     isEnabled = $true
                #     isBlocking = $true
                #     isDeleted = $false
                #     settings = @{
                #         buildDefinitionId = $prValidationDefinitionId
                #         queueOnSourceUpdateOnly = $false
                #         manualQueueOnly = $false
                #         displayName = $prValidationDefinition.name
                #         validDuration = [decimal]"0.0"
                #         scope = $scope
                #     }
                #     type = @{ id = $buildTypeId }
                # } -identifier "[$($repoValue.name)|$branchName] Build $($prValidationDefinition.name)" -matching {
                #     ($_.type.id -eq $buildTypeId) -and
                #     ($_.settings.buildDefinitionId -eq $prValidationDefinition.id)
                # } -policies $policies -repo $repoValue.name -branch $branch -whatIf:$whatIf -organization:$organization -project:$project

                # Add code coverage check for each build
                if (($null -ne $buildPolicies) -and ($buildPolicies.Count -gt 0)) {
                    $buildPolicies | Invoke-ForEachSection { param($value) "$($repoValue.name)|$branchName|$($value.definition.name)" } {
                        param($value)
                        $buildPolicy = $value.policy
                        $buildDefinition = $value.definition

                        [array]$filenamePatterns = if ($null -ne ($buildPolicy.settings.PSObject.Properties | Where-Object { $_.Name -eq "filenamePatterns" })) {
                            $buildPolicy.settings.filenamePatterns
                        } else {
                            $null
                        }

                        $null = Update-RepoPolicyOrCreate -body @{
                            type = @{ id = $statusTypeId }
                            isDeleted = $buildPolicy.isDeleted
                            isBlocking = $false # $buildPolicy.isBlocking
                            isEnabled = $buildPolicy.isEnabled
                            settings = @{
                                authorId = $null
                                defaultDisplayName = "CodeCoverage: $($buildDefinition.name)"
                                invalidateOnSourceUpdate = -not ($buildPolicy.settings.queueOnSourceUpdateOnly)
                                policyApplicability = $null
                                statusName = "codecoverage"
                                statusGenre = $buildDefinition.name.ToLowerInvariant()
                                scope = $scope
                                filenamePatterns = [array]$filenamePatterns
                            }
                        } -identifier "[$($repoValue.name)|$branchName] Code Coverage: $($buildDefinition.name)" -matching {
                            ($_.type.id -eq $statusTypeId)  -and
                            ($_.settings.statusName -eq "codecoverage") -and
                            ($_.settings.statusGenre -eq $buildDefinition.name)
                        } -policies $policies -repo $repoValue.name -repo $repoValue.id -branch $branch -whatIf:$whatIf -organization:$organization -project:$project
                    }
                }

                # Disable code coverage policies that don't have a build
                [array]$script:codeCoveragePolicies = $policies | Where-Object {
                    $policy = $_

                    ($_.type.id -eq $statusTypeId) -and
                    ($_.settings.statusName -eq "codecoverage") -and
                    ($null -eq ($buildPolicies | Where-Object {
                        ($_.definition.name -eq $policy.settings.statusGenre) -and
                        ($_.policy.settings.buildDefinitionId -ne $prValidationDefinitionId)
                    }))
                } | ForEach-Object {
                    Write-PipelineIssue -warningMessage "Removing code coverage check with missing/invalid build: $($_.settings.defaultDisplayName) ($($_.id))"
                    $null = Remove-RepoPolicy -organization $organization -project $project -policyId $_.id -whatIf:$whatIf
                }
            }
        }
    }
    End {
    }
}
Export-ModuleMember -Function Update-OcBranchPolicies

$script:jwtRegex = '^[A-Za-z0-9-_]+\.[A-Za-z0-9-_]+\.[A-Za-z0-9-_]*$'

# Returns an access token from either $env:System_AccessToken_<organization> if specified, or $env:System_AccessToken
function Get-AzureDevOpsHeaders {
    [CmdletBinding()]
    param(
        # The organization
        [string]$organization,
        # The request url - used to determine the org if not specified for which access token to use
        [string]$url,
        [string]$accessToken = $null
    )

    if ([string]::IsNullOrWhiteSpace($organization) -and -not [string]::IsNullOrWhiteSpace($url)) {
        $organization = (Get-OrganizationFromAdoUrl $url $script:defaultOrganization).ToUpper()
    }
    if (-not [string]::IsNullOrWhiteSpace($organization) -and [string]::IsNullOrWhiteSpace($accessToken)) {
        $accessToken = [Environment]::GetEnvironmentVariable("SYSTEM_ACCESSTOKEN_$organization")
        if (([string]::IsNullOrWhiteSpace($accessToken) -or [Environment]::GetEnvironmentVariable("SYSTEM_ACCESSTOKEN_${organization}_valid") -ne $accessToken) -and $script:interactiveSession -and $env:Ocl_Imaging_DevBox -ne "true") {
            $url = "https://dev.azure.com/$organization/_usersSettings/tokens"
            $openedTokenPage = $false
            while ([string]::IsNullOrWhiteSpace($accessToken) -or [Environment]::GetEnvironmentVariable("SYSTEM_ACCESSTOKEN_${organization}_valid") -ne $accessToken) {
                if ([string]::IsNullOrWhiteSpace($accessToken)) {
                    if (-not $openedTokenPage) {
                        Write-Command $url
                        $null = Start-Process $url
                        $openedTokenPage = $true
                    }
                    Write-PipelineIssue -warningMessage "Generate an access token via $url"
                    $accessToken = Read-Host "$organization access token"
                }
                [Environment]::SetEnvironmentVariable("SYSTEM_ACCESSTOKEN_$organization", $accessToken)
                [Environment]::SetEnvironmentVariable("SYSTEM_ACCESSTOKEN_${organization}_valid", $accessToken)
                try {
                    $null = Get-Projects -organization $organization
                    [Environment]::SetEnvironmentVariable("SYSTEM_ACCESSTOKEN_${organization}_valid", $accessToken)
                } catch {
                    Write-PipelineIssue -warningMessage "Invalid access token for $organization" -exception $_
                    $accessToken = $null
                    [Environment]::SetEnvironmentVariable("SYSTEM_ACCESSTOKEN_$organization", "")
                    [Environment]::SetEnvironmentVariable("SYSTEM_ACCESSTOKEN_${organization}_valid", "")
                }
            }
        }
    }

    if ([string]::IsNullOrWhiteSpace($accessToken)) {
        $accessToken = $env:SYSTEM_ACCESSTOKEN
    }
    if ([string]::IsNullOrWhitespace($accessToken)) {
        if ([string]::IsNullOrWhiteSpace($organization)) {
            throw "`$env`:SYSTEM_ACCESSTOKEN_ must not be null or whitespace"
        } else {
            throw "Either `$env`:SYSTEM_ACCESSTOKEN_ or `$env`:SYSTEM_ACCESSTOKEN_$organization must not be null or whitespace"
        }
    }
    return @{ Authorization = (Format-AccessTokenHeader $accessToken) }
}
Export-ModuleMember -Function Get-AzureDevOpsHeaders

# Formats the specified access token to the way it needs to be sent as an http header
function Format-AccessTokenHeader {
    [CmdletBinding()]
    param(
        [string]$accessToken
    )
    if ([string]::IsNullOrWhiteSpace($accessToken)) { throw "-accessToken must be specified" }
    if ($accessToken -match $script:jwtRegex) {
        return "Bearer $accessToken"
    } else {
        return "Basic $([System.Convert]::ToBase64String([System.Text.Encoding]::ASCII.GetBytes("`:$accessToken")))"
    }
}
export-ModuleMember -Function Format-AccessTokenHeader

function Invoke-RestMethodWithRetry {
    [CmdletBinding()]
    param(
        [string]$method,
        [string]$uri,
        [string]$organization,
        [string]$accessToken = $null,
        $headers = (Get-AzureDevOpsHeaders -organization $organization -url $uri -accessToken $accessToken),
        $body,
        [string]$contentType = "application/json; charset=utf-8",
        [string]$outFile,
        [switch]$raw,
        [switch]$logBody,
        [switch]$throwOriginalError,
        [switch]$returnNullOn404,
        [switch]$whatIf
    )

    $inputs = @{
        method = $method
        uri = $uri
        headers = $headers
        UseBasicParsing = [switch]$true
        TimeoutSec = 12000
    }
    $maybeOutFile = ""
    if (-not [string]::IsNullOrWhiteSpace($outFile)) {
        $inputs.outFile = $outFile
        $maybeOutFile = " -outFile $outFile"
    }
    $maybeBody = ""
    if ($null -ne $body) {
        if ($body -is [string]) {
            $inputs.Body = $body
        } else {
            $inputs.Body = ConvertTo-Json $body -Depth 100 -Compress
        }
        $inputs.ContentType = $contentType
        if ($logBody) {
            $maybeBody = " $($inputs.Body)"
        }
    }
    $maybeOutFile = if (-not [string]::IsNullOrWhiteSpace($outFile)) {
        "-outFile $outFile"
    }
    $maxTries = 3

    $callingFrame = (Get-PSCallStack)[1]
    $oldProgressPreference = $global:ProgressPreference
    $global:ProgressPreference = "SilentlyContinue"
    $oldErrorPreference = $global:ErrorActionPreference
    $global:ErrorActionPreference = "Stop"
    try {
        for ($i = 0; $i -lt $maxTries; ++$i) {
            try {
                Write-Command "$method $uri$maybeBody$maybeOutFile" -callingFrame $callingFrame
                if ($whatIf) {
                    Write-PipelineIssue -warningMessage "[WhatIf] Skipping $method $uri$maybeBody$maybeOutFile"
                    return
                }
                $response = Invoke-WebRequest @inputs
                if ($response.StatusCode -eq [System.Net.HttpStatusCode]::NonAuthoritativeInformation) { # Bad access token
                    throw "$method $uri$maybeBody$maybeOutFile returned $([int]$response.StatusCode): $([string]$response.StatusCode) |`n`n$($response.Content)"
                }
                if ($raw) {
                    return $response
                } else {
                    $content = $response.Content
                    if ($content -is [string]) {
                        try {
                            $content = $content | ConvertFrom-Json
                        } catch {
                            Write-PipelineIssue -warningMessage "Failed to deserialize $method $uri response content. Returning a string" -exception $_
                            return $content
                        }
                        if ($content -is [PSCustomObject] -and $null -ne ($content.PSObject.Properties | Where-Object { $_.Name -eq "value" } | Select-Object -First 1)) {
                            $content = $content.value
                        }
                    }
                    return $content
                }
            } catch {
                if ($_ -contains [System.Net.HttpStatusCode]::NonAuthoritativeInformation.ToString()) {
                    throw Write-PipelineIssue -errorMessage "Failed to invoke $method $uri$maybeBody$maybeOutFile" -exception $_ 6>$null
                }
                if (Test-WebException $_.Exception) {
                    # Don't retry for known response statuses that will fail on subsequent invocations
                    if (($_.Exception.Response.StatusCode -eq [System.Net.HttpStatusCode]::NotFound) -and $returnNullOn404) {
                        return $null
                    }
                    if (@(
                        [System.Net.HttpStatusCode]::BadRequest
                        [System.Net.HttpStatusCode]::Unauthorized
                        [System.Net.HttpStatusCode]::Forbidden
                        [System.Net.HttpStatusCode]::Conflict
                        [System.Net.HttpStatusCode]::NotFound
                    ) -eq $_.Exception.Response.StatusCode) {
                        if ($throwOriginalError) {
                            Write-PipelineIssue -errorMessage "Failed to invoke $method $uri$maybeBody$maybeOutFile" -exception $_
                            throw
                        } else {
                            throw Write-PipelineIssue -errorMessage "Failed to invoke $method $uri$maybeBody$maybeOutFile" -exception $_ 6>$null
                        }
                    }
                }

                if ($i -eq ($maxTries - 1)) {
                    if ($throwOriginalError) {
                        Write-PipelineIssue -errorMessage "[$($i + 1)/$maxTries] Failed to invoke $method $uri$maybeBody$maybeOutFile" -exception $_
                        throw
                    } else {
                        throw Write-PipelineIssue -errorMessage "[$($i + 1)/$maxTries] Failed to invoke $method $uri$maybeBody$maybeOutFile" -exception $_
                    }
                } else {
                    $sleepTime = [System.Math]::Pow(2, $i)
                    Write-PipelineIssue -warningMessage "[$($i + 1)/$maxTries] Failed to invoke $method $uri$maybeBody$maybeOutFile. Trying again after $sleepTime seconds" -exception $_
                    Start-Sleep -Seconds $sleepTime
                }
            }
        }
    } finally {
        $global:ProgressPreference = $oldProgressPreference
        $global:ErrorActionPreference = $oldErrorPreference
    }
}

# Export all aliases defined in this module
Export-ModuleMember -alias *