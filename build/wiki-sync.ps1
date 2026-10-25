#Requires -Version 7.4
<#
.SYNOPSIS
Mirrors the guides in docs/ onto the wiki pages. Used by .forgejo/workflows/wiki.yml.

.DESCRIPTION
Two actions:

  Render   Writes the text of every wiki page to -OutDirectory (<page name>.md), changing nothing else. It needs no token and no network.
  Publish  Renders the pages, reads each one from the wiki, and updates (or creates) the ones whose text differs.

A page is its docs/ file with the first heading dropped (the wiki shows the page title instead), a note that the repository holds the
source of truth, and the backticked names of the other guides turned into links between the pages, and relative images (images/...) pointed at the raw file on
main. Code blocks and code spans are left as they are.

The token comes from the variable named by -TokenVariable (WIKI_TOKEN). It is sent to curl on stdin, never as an argument.
Everything else comes from the variables a Forgejo Actions job sets: GITHUB_REPOSITORY, GITHUB_SERVER_URL, GITHUB_API_URL, GITHUB_SHA.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('Render', 'Publish', 'Load')]
    [string]$Action,

    # Render: the folder the pages are written to.
    [string]$OutDirectory,

    # Where the guides are read from.
    [string]$DocsDirectory = (Join-Path (Split-Path -Parent $PSScriptRoot) 'docs'),

    # Publish: the environment variable that holds the wiki token.
    [string]$TokenVariable = 'WIKI_TOKEN'
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$defaultServer = 'https://git.tortal.tech'
$defaultRepository = 'ethan-hann/SavePoint'

function Get-PageMap {
    # One entry per guide: the file in docs/, the page title the wiki shows, and the page name in its address.
    foreach ($entry in @(
            @('architecture.md', 'Architecture'),
            @('decisions.md', 'Decisions'),
            @('developer-guide.md', 'Developer guide'),
            @('testing-on-mac-and-linux.md', 'Testing on macOS and Linux'),
            @('ui-guide.md', 'UI guide'),
            @('user-guide.md', 'User guide'))) {
        [pscustomobject]@{ Doc = $entry[0]; Title = $entry[1]; Page = $entry[1] -replace ' ', '-' }
    }
}

function Get-Repository {
    if ($env:GITHUB_REPOSITORY) {
        if ($env:GITHUB_REPOSITORY -notmatch '^[^/]+/[^/]+$') { throw 'GITHUB_REPOSITORY is not set to owner/name.' }
        return $env:GITHUB_REPOSITORY
    }
    $defaultRepository
}

function Get-ServerUrl {
    if ($env:GITHUB_SERVER_URL) { return $env:GITHUB_SERVER_URL.TrimEnd('/') }
    $defaultServer
}

function Get-ApiBase {
    if ($env:GITHUB_API_URL) { return $env:GITHUB_API_URL.TrimEnd('/') }
    (Get-ServerUrl) + '/api/v1'
}

function Convert-DocToPage {
    # The page text for one guide. Pure: the same text gives the same page on every machine.
    param([string]$Text, [string]$Doc)

    $pages = @(Get-PageMap)
    $names = ($pages | ForEach-Object { [regex]::Escape(($_.Doc -replace '\.md$', '')) }) -join '|'
    $pattern = "``(?:docs/)?($names)\.md``"
    $byDoc = @{}
    foreach ($page in $pages) { $byDoc[$page.Doc] = $page }
    $link = [System.Text.RegularExpressions.MatchEvaluator] {
        param($match)
        $target = $byDoc["$($match.Groups[1].Value).md"]
        "[$($target.Title)]($($target.Page))"
    }

    # The wiki has no docs/images, so a relative image or link into it points at the file on main instead. Code spans are matched first so
    # they are returned as they are.
    $rawBase = "$(Get-ServerUrl)/$(Get-Repository)/raw/branch/main/docs/"
    $imagePattern = '(?<span>(?<ticks>`+).+?(?<!`)\k<ticks>(?!`))|\]\((?:\./)?(?<path>images/[^)\s]*)'
    $image = [System.Text.RegularExpressions.MatchEvaluator] {
        param($match)
        if ($match.Groups['span'].Success) { return $match.Value }
        "]($rawBase$($match.Groups['path'].Value)"
    }

    $lines = [System.Collections.Generic.List[string]]::new(($Text -replace "`r`n", "`n" -split "`n"))
    # The first heading is the guide's title; the wiki page already has one.
    if ($lines.Count -gt 0 -and $lines[0] -match '^# ') {
        $lines.RemoveAt(0)
        while ($lines.Count -gt 0 -and $lines[0].Trim() -eq '') { $lines.RemoveAt(0) }
    }

    $fence = $null
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $opening = [regex]::Match($lines[$i], '^ {0,3}(`{3,}|~{3,})')
        if ($null -eq $fence) {
            if ($opening.Success) { $fence = $opening.Groups[1].Value; continue }
            $lines[$i] = [regex]::Replace([regex]::Replace($lines[$i], $pattern, $link), $imagePattern, $image)
        }
        elseif ($opening.Success -and $opening.Groups[1].Value[0] -eq $fence[0] -and $opening.Groups[1].Value.Length -ge $fence.Length -and $lines[$i].Trim() -eq $opening.Groups[1].Value) {
            $fence = $null
        }
    }

    $source = "$(Get-ServerUrl)/$(Get-Repository)/src/branch/main/docs/$Doc"
    $note = "> This page mirrors [``docs/$Doc``]($source) in the repository, which is the source of truth. Edit the file there; this page is regenerated from it."
    $note + "`n`n" + ($lines -join "`n")
}

function Get-Pages {
    # Every page as @{ Title; Page; Text }, read from DocsDirectory.
    foreach ($entry in Get-PageMap) {
        $path = Join-Path $DocsDirectory $entry.Doc
        if (-not (Test-Path -LiteralPath $path)) { throw "The page map names $($entry.Doc), but $path does not exist." }
        $text = [System.IO.File]::ReadAllText($path, [System.Text.UTF8Encoding]::new($false))
        [pscustomobject]@{ Title = $entry.Title; Page = $entry.Page; Text = (Convert-DocToPage -Text $text -Doc $entry.Doc) }
    }
}

function Invoke-Api {
    # Returns @{ Status; Text }. curl does the talking: Cloudflare in front of Forgejo refuses the default user agent of .NET's clients.
    param([string]$Method = 'GET', [string]$Url, [string]$BodyFile)

    $token = [Environment]::GetEnvironmentVariable($TokenVariable)
    if (-not $token) { throw "$TokenVariable is not set." }
    $headers = [System.IO.Path]::GetTempFileName()
    $body = [System.IO.Path]::GetTempFileName()
    try {
        $info = [System.Diagnostics.ProcessStartInfo]::new($(if ($IsWindows) { 'curl.exe' } else { 'curl' }))
        $info.UseShellExecute = $false
        $info.RedirectStandardInput = $true
        foreach ($argument in @('--silent', '--show-error', '--request', $Method, '--dump-header', $headers, '--output', $body, '--user-agent', 'curl/8.8.0', '--max-time', '120', '--header', '@-')) {
            $info.ArgumentList.Add($argument)
        }
        if ($BodyFile) {
            foreach ($argument in @('--header', 'Content-Type: application/json', '--data-binary', "@$BodyFile")) { $info.ArgumentList.Add($argument) }
        }
        $info.ArgumentList.Add($Url)
        $process = [System.Diagnostics.Process]::Start($info)
        # The header goes in with no line ending of its own, so no carriage return can end up inside the token's header.
        $process.StandardInput.Write("Authorization: token $token")
        $process.StandardInput.Close()
        $process.WaitForExit()
        if ($process.ExitCode -ne 0) { throw "curl failed with exit code $($process.ExitCode) for $Method $Url" }
        $status = 0
        foreach ($line in Get-Content -LiteralPath $headers) {
            if ($line -match '^HTTP/\S+\s+(\d{3})') { $status = [int]$Matches[1] }
        }
        @{ Status = $status; Text = "$(Get-Content -LiteralPath $body -Raw)" }
    }
    finally {
        Remove-Item -LiteralPath $headers, $body -Force -ErrorAction SilentlyContinue
    }
}

function Publish-Page {
    # Returns 'unchanged', 'updated' or 'created'.
    param($Page)

    $wiki = "$(Get-ApiBase)/repos/$(Get-Repository)/wiki"
    $current = Invoke-Api -Url "$wiki/page/$($Page.Page)"
    if ($current.Status -ne 200 -and $current.Status -ne 404) { throw "Reading $($Page.Page) answered $($current.Status): $($current.Text)" }
    if ($current.Status -eq 200) {
        $existing = [System.Text.Encoding]::UTF8.GetString([Convert]::FromBase64String(($current.Text | ConvertFrom-Json).content_base64))
        if ($existing -ceq $Page.Text) { return 'unchanged' }
    }

    $sha = if ($env:GITHUB_SHA) { $env:GITHUB_SHA.Substring(0, [Math]::Min(7, $env:GITHUB_SHA.Length)) } else { 'a manual run' }
    $bodyFile = [System.IO.Path]::GetTempFileName()
    try {
        $json = @{
            title          = $Page.Title
            content_base64 = [Convert]::ToBase64String([System.Text.UTF8Encoding]::new($false).GetBytes($Page.Text))
            message        = "Mirror docs/ at $sha"
        } | ConvertTo-Json -Compress
        [System.IO.File]::WriteAllText($bodyFile, $json, [System.Text.UTF8Encoding]::new($false))
        if ($current.Status -eq 200) {
            $result = Invoke-Api -Method PATCH -Url "$wiki/page/$($Page.Page)" -BodyFile $bodyFile
            if ($result.Status -ne 200) { throw "Updating $($Page.Page) answered $($result.Status): $($result.Text)" }
            return 'updated'
        }
        $result = Invoke-Api -Method POST -Url "$wiki/new" -BodyFile $bodyFile
        if ($result.Status -ne 201) { throw "Creating $($Page.Page) answered $($result.Status): $($result.Text)" }
        'created'
    }
    finally {
        Remove-Item -LiteralPath $bodyFile -Force -ErrorAction SilentlyContinue
    }
}

# Load only defines the functions above, so tests can call them.
if ($Action -eq 'Load') { return }

switch ($Action) {
    'Render' {
        if (-not $OutDirectory) { throw 'Render needs -OutDirectory.' }
        New-Item -ItemType Directory -Force -Path $OutDirectory | Out-Null
        foreach ($page in Get-Pages) {
            [System.IO.File]::WriteAllText((Join-Path $OutDirectory "$($page.Page).md"), $page.Text, [System.Text.UTF8Encoding]::new($false))
            Write-Host "==> rendered $($page.Page)"
        }
    }

    'Publish' {
        foreach ($page in Get-Pages) {
            Write-Host "==> $($page.Page): $(Publish-Page $page)"
        }
    }
}
