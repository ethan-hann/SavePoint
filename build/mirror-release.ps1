#Requires -Version 7.4
<#
.SYNOPSIS
Copies a published Forgejo release, with its notes and files, to the release of the same tag on the GitHub mirror. Used by .forgejo/workflows/github-mirror.yml.

.DESCRIPTION
For the release of -Tag (or every published v* release when -Tag is "all", oldest first), on the GitHub repository -GitHubRepository:

  - waits a bounded time for the tag to arrive on GitHub (the push mirror sends it), and fails with a message when it does not;
  - creates the release, or brings the name, notes and prerelease flag of the existing one in line with Forgejo;
  - sends every file of the Forgejo release under its own name, skipping a file GitHub already holds with the same hash, replacing one
    that differs, and removing one Forgejo no longer has;
  - refuses a release with no SHA256SUMS.txt, or whose files the list does not all name;
  - checks each file: its bytes against that list before sending, and the digest GitHub reports for the upload afterwards (the size
    when GitHub reports none). A file already on GitHub counts as right only when its reported digest, or the hash of the bytes GitHub
    serves when it reports none, equals the list.

A draft is never mirrored. Running the script again changes nothing that is already right, so it doubles as the sync after a release is edited.

Two tokens, kept apart. GH_MIRROR_TOKEN (a fine-grained GitHub token with Contents read and write on the mirror alone) goes only to
api.github.com and uploads.github.com; every request checks its host first. GITHUB_TOKEN (the job's own) goes only to the Forgejo server
named by GITHUB_API_URL. Both reach curl on its standard input, never as an argument or in a file, and are never printed.
SAVEPOINT_MIRROR_ALLOW_LOOPBACK=1 lets the GitHub token go to a server on the local machine too; the tests of this script use it.
#>
[CmdletBinding()]
param(
    # "v0.9.21" for one release, or "all" to sync every published v* release.
    [string]$Tag = $env:MIRROR_TAG,

    [string]$GitHubRepository = 'ethan-hann/SavePoint',
    [string]$GitHubApiUrl = 'https://api.github.com',
    [string]$GitHubUploadUrl = 'https://uploads.github.com',

    # How long to wait for the push mirror to deliver the tag, and how often to look.
    [int]$TagWaitSeconds = 300,
    [int]$TagPollSeconds = 10
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

# Any failure ends the run with its message as one plain line on standard error; PowerShell's own error view adds colors, a file
# prefix and line wraps that differ between systems and split the message in the CI log.
trap {
    [Console]::Error.WriteLine("$($_.Exception.Message)")
    exit 1
}

$checksumName = 'SHA256SUMS.txt'
$sourcesLinkPrefix = 'Source code of the bundled git'

if (-not $Tag -or $Tag -notmatch '^(all|v[0-9A-Za-z._+-]+)$') { throw "The tag is '$Tag'; give a release tag such as v0.9.21, or all." }
if ($GitHubRepository -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$') { throw "GitHubRepository '$GitHubRepository' is not owner/name." }

function Test-Loopback {
    param([uri]$Uri)
    $env:SAVEPOINT_MIRROR_ALLOW_LOOPBACK -eq '1' -and $Uri.Scheme -eq 'http' -and $Uri.Host -in @('127.0.0.1', 'localhost', '[::1]', '::1')
}

function Assert-GitHubUrl {
    # The one gate in front of the GitHub token.
    param([string]$Url)
    $uri = $null
    $github = [uri]::TryCreate($Url, [System.UriKind]::Absolute, [ref]$uri) -and $uri.Scheme -eq 'https' -and $uri.Host -in @('api.github.com', 'uploads.github.com')
    if (-not ($github -or ($uri -and (Test-Loopback $uri)))) {
        $shown = if ($uri) { $uri.Host } else { $Url }
        throw "Refusing to send the GitHub token to ${shown}: it goes only to api.github.com and uploads.github.com."
    }
}

function Get-ForgejoOrigin {
    if (-not $env:GITHUB_API_URL -or -not $env:GITHUB_REPOSITORY) { throw 'GITHUB_API_URL and GITHUB_REPOSITORY are not set; run this in a Forgejo Actions job.' }
    ([uri]$env:GITHUB_API_URL).GetLeftPart([System.UriPartial]::Authority)
}

function Invoke-Http {
    # Returns @{ Status; Text }. The token is the first thing curl reads on standard input, as a header line with no line ending.
    param(
        [ValidateSet('GitHub', 'Forgejo', 'Signed')][string]$Target,
        [string]$Method = 'GET',
        [string]$Url,
        [string[]]$Extra = @(),
        [string]$OutFile,
        [string]$MediaType = 'application/vnd.github+json',
        [switch]$Follow
    )
    if ($Target -eq 'GitHub') {
        Assert-GitHubUrl $Url
        if (-not $env:GH_MIRROR_TOKEN) { throw 'GH_MIRROR_TOKEN is not set.' }
        $authorization = "Authorization: Bearer $($env:GH_MIRROR_TOKEN)"
        $accept = @('--header', "Accept: $MediaType", '--header', 'X-GitHub-Api-Version: 2022-11-28')
    }
    elseif ($Target -eq 'Signed') {
        # The address GitHub hands back for a file download carries its own signature, so no token goes with it.
        $uri = $null
        if (-not ([uri]::TryCreate($Url, [System.UriKind]::Absolute, [ref]$uri) -and ($uri.Scheme -eq 'https' -or (Test-Loopback $uri)))) { throw 'Refusing a download address that is not https.' }
        $authorization = $null
        $accept = @()
    }
    else {
        if (([uri]$Url).Authority -ne ([uri]$env:GITHUB_API_URL).Authority) { throw "Refusing to send the Forgejo token to $(([uri]$Url).Host)." }
        if (-not $env:GITHUB_TOKEN) { throw 'GITHUB_TOKEN is not set.' }
        $authorization = "Authorization: token $($env:GITHUB_TOKEN)"
        $accept = @()
    }
    $headers = [System.IO.Path]::GetTempFileName()
    $body = if ($OutFile) { $OutFile } else { [System.IO.Path]::GetTempFileName() }
    try {
        $info = [System.Diagnostics.ProcessStartInfo]::new($(if ($IsWindows) { 'curl.exe' } else { 'curl' }))
        $arguments = @('--silent', '--show-error', '--request', $Method, '--dump-header', $headers, '--output', $body, '--max-time', '3600') + $accept
        if ($authorization) { $arguments += @('--header', '@-') }
        if ($Follow) { $arguments += '--location' }
        foreach ($argument in ($arguments + $Extra + $Url)) { $info.ArgumentList.Add($argument) }
        $info.UseShellExecute = $false
        $info.RedirectStandardInput = $true
        $info.RedirectStandardError = $true
        $process = [System.Diagnostics.Process]::Start($info)
        $errors = $process.StandardError.ReadToEndAsync()
        if ($authorization) {
            $line = [System.Text.UTF8Encoding]::new($false).GetBytes($authorization)
            try { $process.StandardInput.BaseStream.Write($line, 0, $line.Length) } catch { }
        }
        $process.StandardInput.Close()
        $process.WaitForExit()
        if ($process.ExitCode -ne 0) { throw "curl failed with exit code $($process.ExitCode) for $Method $Url`: $($errors.GetAwaiter().GetResult())" }
        $status = 0
        $location = ''
        foreach ($headerLine in Get-Content -LiteralPath $headers) {
            if ($headerLine -match '^HTTP/\S+\s+(\d{3})') { $status = [int]$Matches[1]; $location = '' }
            elseif ($headerLine -match '^location:\s*(\S+)') { $location = $Matches[1] }
        }
        $text = if ($OutFile) { '' } else { Get-Content -LiteralPath $body -Raw }
        @{ Status = $status; Text = "$text"; Location = $location }
    }
    finally {
        Remove-Item -LiteralPath $headers -Force -ErrorAction SilentlyContinue
        if (-not $OutFile) { Remove-Item -LiteralPath $body -Force -ErrorAction SilentlyContinue }
    }
}

function Invoke-GitHubJson {
    param([string]$Method = 'GET', [string]$Path, $Body, [int[]]$Expect = @(200))
    $extra = @()
    $bodyFile = $null
    try {
        if ($null -ne $Body) {
            $bodyFile = [System.IO.Path]::GetTempFileName()
            [System.IO.File]::WriteAllText($bodyFile, ($Body | ConvertTo-Json -Compress -Depth 5), [System.Text.UTF8Encoding]::new($false))
            $extra = @('--header', 'Content-Type: application/json', '--data-binary', "@$bodyFile")
        }
        $response = Invoke-Http -Target GitHub -Method $Method -Url ($GitHubApiUrl.TrimEnd('/') + $Path) -Extra $extra
    }
    finally {
        if ($bodyFile) { Remove-Item -LiteralPath $bodyFile -Force -ErrorAction SilentlyContinue }
    }
    if ($Expect -notcontains $response.Status) { throw "$Method $Path answered $($response.Status): $($response.Text)" }
    if ($response.Text) { $response.Text | ConvertFrom-Json } else { $null }
}

function Get-ForgejoReleases {
    param([switch]$Drafts)
    $query = if ($Drafts) { 'draft=true&' } else { '' }
    for ($page = 1; $page -le 40; $page++) {
        $response = Invoke-Http -Target Forgejo -Url "$($env:GITHUB_API_URL.TrimEnd('/'))/repos/$($env:GITHUB_REPOSITORY)/releases?${query}limit=50&page=$page"
        if ($response.Status -ne 200) { throw "Listing the Forgejo releases answered $($response.Status): $($response.Text)" }
        $releases = @($response.Text | ConvertFrom-Json)
        if ($releases.Count -eq 0 -or $null -eq $releases[0]) { break }
        $releases
        if ($releases.Count -lt 50) { break }
    }
}

function Get-ForgejoRelease {
    # The published release for a tag, or $null for a draft (nothing to mirror yet).
    param([string]$TagName)
    $found = @(Get-ForgejoReleases | Where-Object { $_.tag_name -eq $TagName }) | Select-Object -First 1
    if ($found -and -not $found.draft) { return $found }
    $draft = $found -or (@(Get-ForgejoReleases -Drafts | Where-Object { $_.tag_name -eq $TagName }).Count -gt 0)
    if ($draft) {
        Write-Host "==> the Forgejo release for $TagName is a draft; nothing is mirrored until it is published"
        return $null
    }
    throw "Forgejo has no release for $TagName."
}

function Wait-GitHubTag {
    param([string]$TagName)
    $deadline = [datetime]::UtcNow.AddSeconds($TagWaitSeconds)
    while ($true) {
        $response = Invoke-Http -Target GitHub -Url ($GitHubApiUrl.TrimEnd('/') + "/repos/$GitHubRepository/git/ref/tags/$([uri]::EscapeDataString($TagName))")
        if ($response.Status -eq 200) { return }
        if ($response.Status -ne 404) { throw "Looking for the tag $TagName on GitHub answered $($response.Status): $($response.Text)" }
        if ([datetime]::UtcNow.AddSeconds($TagPollSeconds) -gt $deadline) {
            throw "The tag $TagName is not on GitHub after $TagWaitSeconds seconds. The push mirror from Forgejo has not delivered it; check the mirror in the repository settings, then run this again."
        }
        Write-Host "    waiting for the tag $TagName to reach GitHub"
        Start-Sleep -Seconds $TagPollSeconds
    }
}

function Get-FileSha256 {
    param([string]$Path)
    (Get-FileHash -Algorithm SHA256 -LiteralPath $Path).Hash.ToLowerInvariant()
}

function Save-ForgejoAsset {
    param($Asset, [string]$Path)
    # The download link names the public host; the request goes to the Forgejo server this job talks to.
    $url = (Get-ForgejoOrigin) + ([uri]$Asset.browser_download_url).PathAndQuery
    $response = Invoke-Http -Target Forgejo -Url $url -OutFile $Path -Follow
    if ($response.Status -ne 200) { throw "Downloading $($Asset.name) from Forgejo answered $($response.Status)." }
    if ((Get-Item -LiteralPath $Path).Length -ne $Asset.size) { throw "$($Asset.name) came down as $((Get-Item -LiteralPath $Path).Length) bytes, not the $($Asset.size) Forgejo lists." }
}

function Get-GitHubAssetHash {
    # The digest GitHub reports for an asset, or, when it reports none, the hash of the bytes it serves.
    param($Asset, [string]$Scratch)
    if ("$($Asset.digest)" -match '^sha256:([0-9a-fA-F]{64})$') { return $Matches[1].ToLowerInvariant() }
    $path = Join-Path $Scratch 'github-asset'
    try {
        $url = $GitHubApiUrl.TrimEnd('/') + "/repos/$GitHubRepository/releases/assets/$($Asset.id)"
        $response = Invoke-Http -Target GitHub -Url $url -OutFile $path -MediaType 'application/octet-stream'
        if ($response.Status -in 301, 302, 303, 307, 308 -and $response.Location) {
            $response = Invoke-Http -Target Signed -Url $response.Location -OutFile $path
        }
        if ($response.Status -ne 200) { throw "Downloading $($Asset.name) from GitHub answered $($response.Status)." }
        Get-FileSha256 $path
    }
    finally { Remove-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue }
}

function Read-ChecksumList {
    param([string]$Path)
    $sums = @{}
    foreach ($line in Get-Content -LiteralPath $Path) {
        if ($line -match '^([0-9a-fA-F]{64})\s+\*?(.+?)\s*$') { $sums[$Matches[2]] = $Matches[1].ToLowerInvariant() }
    }
    $sums
}

function Remove-GitHubAsset {
    param($Asset)
    Invoke-GitHubJson -Method DELETE -Path "/repos/$GitHubRepository/releases/assets/$($Asset.id)" -Expect @(204) | Out-Null
}

function Send-GitHubAsset {
    # Uploads one file, then checks what GitHub holds against the hash of the file.
    param($Release, [string]$Path, [string]$Name, [string]$Hash)
    $size = (Get-Item -LiteralPath $Path).Length
    Write-Host ('==> {0} ({1:N0} bytes)' -f $Name, $size)
    $url = $GitHubUploadUrl.TrimEnd('/') + "/repos/$GitHubRepository/releases/$($Release.id)/assets?name=$([uri]::EscapeDataString($Name))"
    for ($attempt = 1; ; $attempt++) {
        try {
            $response = Invoke-Http -Target GitHub -Method POST -Url $url -Extra @('--header', 'Content-Type: application/octet-stream', '--upload-file', $Path)
            if ($response.Status -ne 201) { throw "answered $($response.Status): $($response.Text)" }
            $uploaded = $response.Text | ConvertFrom-Json
            break
        }
        catch {
            if ($attempt -ge 3) { throw "Uploading $Name failed: $($_.Exception.Message)" }
            Write-Host "    attempt $attempt failed ($($_.Exception.Message)); trying again"
            $held = @((Invoke-GitHubJson -Path "/repos/$GitHubRepository/releases/$($Release.id)").assets | Where-Object { $_.name -eq $Name })
            foreach ($partial in $held) { Remove-GitHubAsset $partial }
            Start-Sleep -Seconds (5 * $attempt)
        }
    }
    $problem = $null
    if ($uploaded.size -ne $size) { $problem = "GitHub holds $($uploaded.size) bytes of $Name, not $size." }
    elseif ($uploaded.digest -and $uploaded.digest -ne "sha256:$Hash") { $problem = "GitHub reports the digest $($uploaded.digest) for $Name, not sha256:$Hash." }
    if ($problem) {
        Remove-GitHubAsset $uploaded
        throw $problem
    }
}

function Sync-Assets {
    param($ForgejoRelease, $GitHubRelease, [string]$Scratch)
    $assets = @($ForgejoRelease.assets | Where-Object { $_.type -ne 'external' -and $_.name -notlike "$sourcesLinkPrefix*" } | Sort-Object { $_.name })
    $held = @{}
    foreach ($asset in @($GitHubRelease.assets)) { if ($asset) { $held[$asset.name] = $asset } }

    # The list of hashes the release's own job wrote; every file is checked against it before it is sent, so a release without it, or with a file it omits, is refused.
    $listAsset = $assets | Where-Object { $_.name -eq $checksumName }
    if (-not $listAsset) { throw "The Forgejo release has no $checksumName, so its files cannot be checked. Nothing was sent." }
    $listPath = Join-Path $Scratch 'sums'
    Save-ForgejoAsset $listAsset $listPath
    $sums = Read-ChecksumList $listPath
    $sums[$checksumName] = Get-FileSha256 $listPath
    $unlisted = @($assets | Where-Object { -not $sums.ContainsKey($_.name) } | ForEach-Object { $_.name })
    if ($unlisted.Count -gt 0) { throw "$checksumName does not list $($unlisted -join ', '), so those files cannot be checked. Nothing was sent." }

    $index = 0
    foreach ($asset in $assets) {
        $index++
        $expected = $sums[$asset.name]
        $current = $held[$asset.name]
        # What GitHub holds is right when its size and its hash (reported, or worked out from the file) agree with the list.
        if ($current -and $current.size -eq $asset.size -and (Get-GitHubAssetHash $current $Scratch) -eq $expected) {
            Write-Host "    $($asset.name) is already on GitHub"
            continue
        }
        $path = Join-Path $Scratch "asset-$index"
        Save-ForgejoAsset $asset $path
        $hash = Get-FileSha256 $path
        if ($hash -ne $expected) {
            throw "Checksum mismatch for $($asset.name): Forgejo serves $hash but its $checksumName says $expected. Nothing was sent for it."
        }
        if ($current) {
            Write-Host "    replacing the earlier $($asset.name)"
            Remove-GitHubAsset $current
        }
        Send-GitHubAsset $GitHubRelease $path $asset.name $hash
        Remove-Item -LiteralPath $path -Force
    }

    $names = @($assets | ForEach-Object { $_.name })
    foreach ($name in @($held.Keys | Where-Object { $names -notcontains $_ } | Sort-Object)) {
        Write-Host "    removing $name, which Forgejo no longer has"
        Remove-GitHubAsset $held[$name]
    }
}

function ConvertTo-ReleaseVersion {
    # "v0.9.21" as a version, or $null for a tag that is not one.
    param([string]$TagName)
    if ($TagName -notmatch '^v(\d+(?:\.\d+){0,3})') { return $null }
    $parts = @($Matches[1] -split '\.')
    while ($parts.Count -lt 2) { $parts += '0' }
    [version]($parts -join '.')
}

function Get-LatestTag {
    # The highest-numbered published release that is not a prerelease: the only one GitHub should show as the latest.
    if (-not $script:latestTag) {
        $newest = Get-ForgejoReleases | Where-Object { -not $_.draft -and -not $_.prerelease -and $null -ne (ConvertTo-ReleaseVersion $_.tag_name) } |
            Sort-Object { ConvertTo-ReleaseVersion $_.tag_name } -Descending | Select-Object -First 1
        $script:latestTag = if ($newest) { $newest.tag_name } else { '-' }
    }
    $script:latestTag
}

function Sync-Release {
    param($ForgejoRelease)
    $name = $ForgejoRelease.tag_name
    Write-Host "==> $name"
    Wait-GitHubTag $name
    $response = Invoke-Http -Target GitHub -Url ($GitHubApiUrl.TrimEnd('/') + "/repos/$GitHubRepository/releases/tags/$([uri]::EscapeDataString($name))")
    if ($response.Status -notin 200, 404) { throw "Looking for the GitHub release of $name answered $($response.Status): $($response.Text)" }
    $title = "$($ForgejoRelease.name)"
    $notes = "$($ForgejoRelease.body)"
    $prerelease = [bool]$ForgejoRelease.prerelease
    # A release copied late must not become GitHub's latest over a newer one.
    $makeLatest = if (-not $prerelease -and (Get-LatestTag) -eq $name) { 'true' } else { 'false' }
    if ($response.Status -eq 404) {
        $release = Invoke-GitHubJson -Method POST -Path "/repos/$GitHubRepository/releases" -Expect @(201) -Body @{ tag_name = $name; name = $title; body = $notes; prerelease = $prerelease; draft = $false; make_latest = $makeLatest }
        Write-Host "    created the GitHub release"
    }
    else {
        $release = $response.Text | ConvertFrom-Json
        # GitHub may store the notes with other line endings than were sent.
        $same = $release.name -eq $title -and ("$($release.body)" -replace '\r\n', "`n").TrimEnd() -eq ($notes -replace '\r\n', "`n").TrimEnd() -and [bool]$release.prerelease -eq $prerelease -and -not $release.draft
        if (-not $same) {
            $release = Invoke-GitHubJson -Method PATCH -Path "/repos/$GitHubRepository/releases/$($release.id)" -Body @{ name = $title; body = $notes; prerelease = $prerelease; draft = $false; make_latest = $makeLatest }
            Write-Host "    brought the name, notes and prerelease flag in line"
        }
    }
    $scratch = Join-Path ([System.IO.Path]::GetTempPath()) ('release-mirror-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $scratch | Out-Null
    try { Sync-Assets $ForgejoRelease $release $scratch }
    finally { Remove-Item -LiteralPath $scratch -Recurse -Force -ErrorAction SilentlyContinue }
}

# A release published before the secret exists is not an error; a manual run, which someone asked for, still fails.
if (-not $env:GH_MIRROR_TOKEN) {
    if ($env:MIRROR_EVENT -eq 'release') {
        Write-Host 'GH_MIRROR_TOKEN is not set, so this release was not copied to GitHub; add the secret and run the GitHub mirror workflow by hand.'
        return
    }
    throw 'GH_MIRROR_TOKEN is not set. Add it as a repository secret (a fine-grained GitHub token with Contents read and write on the mirror).'
}

# Refuse a GitHub address that is not GitHub before anything is sent.
Assert-GitHubUrl $GitHubApiUrl
Assert-GitHubUrl $GitHubUploadUrl

if ($Tag -eq 'all') {
    $releases = @(Get-ForgejoReleases | Where-Object { -not $_.draft -and $_.tag_name -like 'v*' } | Sort-Object { [datetime]$_.created_at })
    $failures = @()
    foreach ($release in $releases) {
        try { Sync-Release $release }
        catch {
            Write-Host "    FAILED: $($_.Exception.Message)"
            $failures += "$($release.tag_name): $($_.Exception.Message)"
        }
    }
    if ($failures.Count -gt 0) { throw "$($failures.Count) of $($releases.Count) releases did not sync:`n$($failures -join "`n")" }
    Write-Host "==> $($releases.Count) release(s) are on GitHub"
}
else {
    $release = Get-ForgejoRelease $Tag
    if ($release) { Sync-Release $release }
}
