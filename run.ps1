<#
    Task runner for this repository.

        .\run.ps1              list the tasks
        .\run.ps1 app          build and start the app
        .\run.ps1 test         run the tests
        .\run.ps1 check        everything that must pass before a merge

    Anything after the task name is passed straight through, so
    `.\run.ps1 test --filter Journal` works.

    Written for Windows PowerShell 5.1: no && chaining, no ternaries.
#>

[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [string]$Task = 'help',

    [Parameter(Position = 1, ValueFromRemainingArguments = $true)]
    [string[]]$Rest
)

$ErrorActionPreference = 'Stop'

$Root = $PSScriptRoot
$App = Join-Path $Root 'src\MetrykiPali'
$Tests = Join-Path $Root 'tests\MetrykiPali.Tests'
$PublishDir = Join-Path $Root 'publish'
$Exe = Join-Path $PublishDir 'MetrykiPali.exe'
$DataDir = Join-Path $env:APPDATA 'MetrykiPali'

function Write-Step($text) {
    Write-Host ''
    Write-Host "==> $text" -ForegroundColor Cyan
}

function Write-Done($text) {
    Write-Host ''
    Write-Host $text -ForegroundColor Green
}

# Runs a command and stops the whole script if it fails, so `check` cannot
# report success after a failing step.
function Invoke-Step {
    param([string]$Exe, [string[]]$Arguments)

    & $Exe @Arguments
    if ($LASTEXITCODE -ne 0) {
        Write-Host ''
        Write-Host "FAILED: $Exe $($Arguments -join ' ')  (exit $LASTEXITCODE)" -ForegroundColor Red
        exit $LASTEXITCODE
    }
}

function Show-Help {
    Write-Host ''
    Write-Host 'Metryki pali - zadania' -ForegroundColor White
    Write-Host ''
    $tasks = [ordered]@{
        'app'     = 'Build and start the app from source'
        'watch'   = 'Start the app and rebuild it whenever a file changes'
        'test'    = 'Run the test suite  (.\run.ps1 test --filter Journal)'
        'build'   = 'Compile everything'
        'exe'     = 'Build the standalone offline MetrykiPali.exe'
        'check'   = 'Build (warnings are errors) + test + publish. Run before merging.'
        'testcopy' = 'Build a test copy with its own journal:  .\run.ps1 testcopy <folder>'
        'data'    = 'Open the folder holding the saved journal'
        'clean'   = 'Delete bin, obj and publish'
        'help'    = 'This list'
    }
    foreach ($name in $tasks.Keys) {
        Write-Host ('  {0,-8} {1}' -f $name, $tasks[$name])
    }
    Write-Host ''
}

switch ($Task.ToLowerInvariant()) {

    { $_ -in 'app', 'run', 'start' } {
        Write-Step 'Starting the app'
        Invoke-Step 'dotnet' (@('run', '--project', $App) + $Rest)
        break
    }

    'watch' {
        Write-Step 'Watching for changes - edit a file and the app restarts'
        Invoke-Step 'dotnet' (@('watch', '--project', $App, 'run') + $Rest)
        break
    }

    { $_ -in 'test', 'tests' } {
        Write-Step 'Running the tests'
        Invoke-Step 'dotnet' (@('test') + $Rest)
        Write-Done 'Tests passed.'
        break
    }

    'build' {
        Write-Step 'Building'
        Invoke-Step 'dotnet' (@('build', '--nologo') + $Rest)
        Write-Done 'Build succeeded.'
        break
    }

    { $_ -in 'exe', 'publish' } {
        Write-Step 'Building the standalone .exe'
        & (Join-Path $Root 'publish.ps1')
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

        $size = [math]::Round((Get-Item $Exe).Length / 1MB)
        Write-Done "Ready: $Exe  ($size MB, runs offline, no install)"
        break
    }

    'check' {
        # Both configurations: some warnings only appear in Release, and the
        # .exe people are given is built from Release.
        Write-Step 'Building Debug - warnings count as errors'
        Invoke-Step 'dotnet' @('build', '--nologo', '-c', 'Debug', '/p:TreatWarningsAsErrors=true')

        Write-Step 'Building Release - warnings count as errors'
        Invoke-Step 'dotnet' @('build', '--nologo', '-c', 'Release', '/p:TreatWarningsAsErrors=true')

        Write-Step 'Running the tests'
        Invoke-Step 'dotnet' @('test')

        Write-Step 'Building the standalone .exe'
        & (Join-Path $Root 'publish.ps1')
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

        Write-Done 'All checks passed - safe to merge.'
        Write-Host '  Still worth doing by hand if you changed the UI or the saved format:' -ForegroundColor DarkGray
        Write-Host '    - start the .exe and use it once (the tests drive the presenter, not the window)' -ForegroundColor DarkGray
        Write-Host '    - open an existing projekt.mpali with the new build' -ForegroundColor DarkGray
        break
    }

    'testcopy' {
        # A copy of the app to try a branch on real data. The 'dane' folder next
        # to the exe makes that copy keep its journal there, so nothing it does
        # can reach the real journal in %APPDATA%.
        if (-not $Rest -or -not $Rest[0]) {
            Write-Host 'Usage: .\run.ps1 testcopy <folder>' -ForegroundColor Red
            exit 1
        }
        $target = $Rest[0]
        $appDir = Join-Path $target 'aplikacja'
        $localData = Join-Path $appDir 'dane'

        Write-Step 'Building the standalone .exe'
        & (Join-Path $Root 'publish.ps1')
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

        Write-Step "Copying it to $appDir"
        New-Item -ItemType Directory -Force $localData | Out-Null
        Copy-Item $Exe $appDir -Force

        # Rebuilding must not wipe what the tester has entered since, so the
        # real journal is copied in only the first time.
        $journal = Join-Path $DataDir 'projekt.mpali'
        $localJournal = Join-Path $localData 'projekt.mpali'
        if ((Test-Path $journal) -and -not (Test-Path $localJournal)) {
            Copy-Item $journal $localJournal
            Write-Host '  copied the real journal into dane\ (the original is not touched)'
        }

        $shell = New-Object -ComObject WScript.Shell
        $link = $shell.CreateShortcut((Join-Path $target 'Uruchom Metryki pali (TEST).lnk'))
        $link.TargetPath = Join-Path $appDir 'MetrykiPali.exe'
        $link.WorkingDirectory = $appDir
        $link.Save()

        Write-Done "Test copy ready: $target"
        break
    }

    'data' {
        if (-not (Test-Path $DataDir)) {
            Write-Host "No saved project yet - $DataDir does not exist." -ForegroundColor Yellow
            break
        }
        Write-Step "Opening $DataDir"
        Start-Process explorer.exe $DataDir
        break
    }

    'clean' {
        Write-Step 'Removing bin, obj and publish'
        Get-ChildItem -Path $App, $Tests -Include bin, obj -Recurse -Directory -ErrorAction SilentlyContinue |
            ForEach-Object {
                Write-Host "  $($_.FullName)"
                Remove-Item $_.FullName -Recurse -Force
            }
        if (Test-Path $PublishDir) {
            Write-Host "  $PublishDir"
            Remove-Item $PublishDir -Recurse -Force
        }
        Write-Done 'Clean.'
        break
    }

    default {
        if ($Task -ne 'help') {
            Write-Host ''
            Write-Host "Unknown task: $Task" -ForegroundColor Red
        }
        Show-Help
        if ($Task -ne 'help') { exit 1 }
        break
    }
}
