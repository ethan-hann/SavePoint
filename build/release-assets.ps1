#Requires -Version 7.4
<#
.SYNOPSIS
Puts a tagged build's packages on the Forgejo release for that tag. Used by .forgejo/workflows/release.yml.

.DESCRIPTION
Three actions, each run by the workflow:

  Verify     Before anything is built: the tag has to be "v" plus the <Version> in Directory.Build.props, the release for the tag (if
             there is one) has to still be a draft, and the sources release of the bundled git has to exist.
  Upload     Adds every file of -Directory to the release for the tag. The release is created as a draft pre-release with the tag's
             message as a placeholder body when it does not exist yet. A file over 95 MB is split into parts of 90 MiB named
             <file>.part1, <file>.part2 and so on, because the proxy in front of Forgejo refuses bigger uploads. A file of the same
             name already on the release is replaced, so a failed run can be run again.
  Checksums  After every package job: downloads what the release holds, writes SHA256SUMS.txt (one line per file and per part, plus
             one for each file the parts make up) and uploads it, and adds a link to the sources release of the bundled git.

Nothing here publishes a release or takes it out of draft; a person does that after reading it.

The token is RELEASE_TOKEN when that is set, otherwise GITHUB_TOKEN (the workflow's own). It is sent from stdin, never as an argument.
Everything else comes from the variables a Forgejo Actions job sets: GITHUB_REPOSITORY, GITHUB_SERVER_URL, GITHUB_API_URL, GITHUB_SHA.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('Verify', 'Upload', 'Checksums', 'Load')]
    [string]$Action,

    # The tag being released, "v0.9.6" for example. Defaults to the tag that started the job.
    [string]$Tag = $env:GITHUB_REF_NAME,

    # Upload: the folder whose files are added to the release.
    [string]$Directory
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$repoRoot = Split-Path -Parent $PSScriptRoot
# A file above this is split. Cloudflare refuses a request body over 100 MB, and the form around the file adds to the body, so the
# cut sits a little below the decimal figure.
$splitAbove = 95000000
$partSize = 90MB
$checksumName = 'SHA256SUMS.txt'
$sourcesLinkPrefix = 'Source code of the bundled git'

function Get-Token {
    if ($env:RELEASE_TOKEN) { return @{ Name = 'RELEASE_TOKEN'; Value = $env:RELEASE_TOKEN } }
    if ($env:GITHUB_TOKEN) { return @{ Name = 'GITHUB_TOKEN'; Value = $env:GITHUB_TOKEN } }
    throw 'No token: the job has neither RELEASE_TOKEN nor GITHUB_TOKEN.'
}

function Get-Repository {
    if (-not $env:GITHUB_REPOSITORY -or $env:GITHUB_REPOSITORY -notmatch '^[^/]+/[^/]+$') {
        throw 'GITHUB_REPOSITORY is not set to owner/name.'
    }
    $env:GITHUB_REPOSITORY
}

function Get-ApiBase {
    if ($env:GITHUB_API_URL) { return $env:GITHUB_API_URL.TrimEnd('/') }
    if ($env:GITHUB_SERVER_URL) { return $env:GITHUB_SERVER_URL.TrimEnd('/') + '/api/v1' }
    throw 'Neither GITHUB_API_URL nor GITHUB_SERVER_URL is set.'
}

function Invoke-Api {
    # Returns @{ Status; Text }. curl does the talking: Cloudflare in front of Forgejo refuses the default user agent of .NET's clients.
    param(
        [string]$Method = 'GET',
        [string]$Path,
        [string]$Url,
        [string[]]$Extra = @(),
        [string]$OutFile,
        [switch]$Follow
    )
    $token = (Get-Token).Value
    $curl = if ($IsWindows) { 'curl.exe' } else { 'curl' }
    $headers = [System.IO.Path]::GetTempFileName()
    # The token's header is read from a file the user alone can read (the temp file is created private) and removed below. Piping it
    # would end the line with CRLF on Windows, and the CR would become part of the header.
    $headerFile = [System.IO.Path]::GetTempFileName()
    $body = if ($OutFile) { $OutFile } else { [System.IO.Path]::GetTempFileName() }
    try {
        [System.IO.File]::WriteAllText($headerFile, "Authorization: token $token", [System.Text.UTF8Encoding]::new($false))
        if (-not $Url) { $Url = (Get-ApiBase) + $Path }
        $arguments = @('--silent', '--show-error', '--request', $Method, '--dump-header', $headers, '--output', $body, '--header', "@$headerFile", '--max-time', '3600')
        if ($Follow) { $arguments += '--location' }
        $arguments += $Extra
        $arguments += $Url
        & $curl @arguments
        if ($LASTEXITCODE -ne 0) { throw "curl failed with exit code $LASTEXITCODE for $Method $Url" }
        $status = 0
        foreach ($line in Get-Content -LiteralPath $headers) {
            if ($line -match '^HTTP/\S+\s+(\d{3})') { $status = [int]$Matches[1] }
        }
        $text = if ($OutFile) { '' } else { Get-Content -LiteralPath $body -Raw }
        @{ Status = $status; Text = "$text" }
    }
    finally {
        Remove-Item -LiteralPath $headers, $headerFile -Force -ErrorAction SilentlyContinue
        if (-not $OutFile) { Remove-Item -LiteralPath $body -Force -ErrorAction SilentlyContinue }
    }
}

function Invoke-ApiJson {
    param([string]$Method = 'GET', [string]$Path, $Body, [int[]]$Expect = @(200))
    $extra = @()
    $bodyFile = $null
    try {
        if ($null -ne $Body) {
            $bodyFile = [System.IO.Path]::GetTempFileName()
            [System.IO.File]::WriteAllText($bodyFile, ($Body | ConvertTo-Json -Compress -Depth 5), [System.Text.UTF8Encoding]::new($false))
            $extra = @('--header', 'Content-Type: application/json', '--data-binary', "@$bodyFile")
        }
        $response = Invoke-Api -Method $Method -Path $Path -Extra $extra
    }
    finally {
        if ($bodyFile) { Remove-Item -LiteralPath $bodyFile -Force -ErrorAction SilentlyContinue }
    }
    if ($Expect -notcontains $response.Status) {
        throw "$Method $Path answered $($response.Status): $($response.Text)"
    }
    if ($response.Text) { $response.Text | ConvertFrom-Json } else { $null }
}

function Get-ProjectVersion {
    $version = (Select-Xml -Path (Join-Path $repoRoot 'Directory.Build.props') -XPath '//Version').Node.InnerText.Trim()
    if (-not $version) { throw 'Directory.Build.props has no <Version>.' }
    $version
}

function Get-BundledGitVersion {
    # The Windows bundle's MinGit version names the sources release, as in "git-sources-2.56.0.2".
    $manifest = Get-Content -LiteralPath (Join-Path $repoRoot 'build/git-manifest.json') -Raw | ConvertFrom-Json -AsHashtable
    $version = "$($manifest['win-x64']['version'])".Trim()
    if (-not $version) { throw 'build/git-manifest.json has no version for win-x64.' }
    $version
}

function Get-SourcesRelease {
    $name = 'git-sources-' + (Get-BundledGitVersion)
    $response = Invoke-Api -Path "/repos/$(Get-Repository)/releases/tags/$name"
    if ($response.Status -ne 200) {
        throw "The sources release $name does not exist ($($response.Status)). Publish it first: the app release links to it for the source code of the bundled git."
    }
    $response.Text | ConvertFrom-Json
}

function Find-Release {
    # Drafts are listed for a token that can write, and a draft has no tag page, so the list is searched.
    param([string]$TagName)
    foreach ($query in 'draft=true&', '') {
        for ($page = 1; $page -le 40; $page++) {
            $releases = @(Invoke-ApiJson -Path "/repos/$(Get-Repository)/releases?${query}limit=50&page=$page")
            if ($releases.Count -eq 0 -or $null -eq $releases[0]) { break }
            $found = $releases | Where-Object { $_.tag_name -eq $TagName } | Select-Object -First 1
            if ($found) { return $found }
            if ($releases.Count -lt 50) { break }
        }
    }
    $null
}

function Get-DraftRelease {
    # Finds the release for the tag and refuses one that is already published: this script only ever fills drafts.
    param([string]$TagName, [switch]$Create)
    $release = Find-Release $TagName
    if (-not $release -and $Create) {
        $message = (git for-each-ref "refs/tags/$TagName" '--format=%(contents)') -join "`n"
        if ($LASTEXITCODE -ne 0 -or -not $message.Trim()) { $message = "Release $TagName" }
        $request = @{ tag_name = $TagName; name = $TagName; body = $message.Trim(); draft = $true; prerelease = $true }
        if ($env:GITHUB_SHA) { $request['target_commitish'] = $env:GITHUB_SHA }
        $path = "/repos/$(Get-Repository)/releases"
        $bodyFile = New-JsonFile $request
        try {
            $response = Invoke-Api -Method POST -Path $path -Extra @('--header', 'Content-Type: application/json', '--data-binary', "@$bodyFile")
        }
        finally { Remove-Item -LiteralPath $bodyFile -Force -ErrorAction SilentlyContinue }
        if ($response.Status -eq 201) {
            $release = $response.Text | ConvertFrom-Json
            Write-Host "created the draft release for $TagName"
        }
        else {
            # The other package job may have created it a moment ago.
            $release = Find-Release $TagName
            if (-not $release) { throw "POST $path answered $($response.Status): $($response.Text)" }
        }
    }
    if ($release -and -not $release.draft) {
        throw "The release for $TagName is already published. Nothing is added to a published release; take it back to a draft by hand first."
    }
    $release
}

function New-JsonFile {
    param($Value)
    $file = [System.IO.Path]::GetTempFileName()
    [System.IO.File]::WriteAllText($file, ($Value | ConvertTo-Json -Compress -Depth 5), [System.Text.UTF8Encoding]::new($false))
    $file
}

function Remove-ReleaseAsset {
    param($Release, [string]$Name)
    $existing = @(Invoke-ApiJson -Path "/repos/$(Get-Repository)/releases/$($Release.id)/assets") | Where-Object { $_ -and $_.name -eq $Name }
    foreach ($asset in $existing) {
        Invoke-ApiJson -Method DELETE -Path "/repos/$(Get-Repository)/releases/$($Release.id)/assets/$($asset.id)" -Expect @(204) | Out-Null
        Write-Host "    replaced the earlier $Name"
    }
}

function Send-ReleaseAsset {
    param($Release, [string]$Path, [string]$Name)
    $size = (Get-Item -LiteralPath $Path).Length
    Write-Host ('==> {0} ({1:N0} bytes)' -f $Name, $size)
    Remove-ReleaseAsset $Release $Name
    $query = '?name=' + [uri]::EscapeDataString($Name)
    $apiPath = "/repos/$(Get-Repository)/releases/$($Release.id)/assets$query"
    for ($attempt = 1; ; $attempt++) {
        try {
            $response = Invoke-Api -Method POST -Path $apiPath -Extra @('--form', "attachment=@$Path")
            if ($response.Status -ne 201) { throw "answered $($response.Status): $($response.Text)" }
            $asset = $response.Text | ConvertFrom-Json
            if ($asset.size -ne $size) { throw "the release holds $($asset.size) bytes of it, not $size" }
            return
        }
        catch {
            if ($attempt -ge 3) { throw "Uploading $Name failed: $($_.Exception.Message)" }
            Write-Host "    attempt $attempt failed ($($_.Exception.Message)); trying again"
            Remove-ReleaseAsset $Release $Name
            Start-Sleep -Seconds (5 * $attempt)
        }
    }
}

function Split-Asset {
    # Cuts a file into parts of $partSize, named <name>.part1 ... in $Folder, and returns the part files in order.
    param([string]$Path, [string]$Folder, [long]$PartSize = $partSize)
    $name = Split-Path -Leaf $Path
    $buffer = [byte[]]::new(1MB)
    $parts = @()
    $source = [System.IO.File]::OpenRead($Path)
    try {
        $number = 1
        while ($source.Position -lt $source.Length) {
            $target = Join-Path $Folder "$name.part$number"
            $output = [System.IO.File]::Create($target)
            try {
                $written = 0L
                while ($written -lt $PartSize) {
                    $read = $source.Read($buffer, 0, [int][Math]::Min($buffer.Length, $PartSize - $written))
                    if ($read -le 0) { break }
                    $output.Write($buffer, 0, $read)
                    $written += $read
                }
            }
            finally { $output.Dispose() }
            $parts += $target
            $number++
        }
    }
    finally { $source.Dispose() }
    $parts
}

function Get-StreamHash {
    # SHA-256 of the files one after the other, as if they were one: for the parts of a split file.
    param([string[]]$Paths)
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $buffer = [byte[]]::new(1MB)
        foreach ($path in $Paths) {
            $stream = [System.IO.File]::OpenRead($path)
            try {
                while (($read = $stream.Read($buffer, 0, $buffer.Length)) -gt 0) { $sha.TransformBlock($buffer, 0, $read, $null, 0) | Out-Null }
            }
            finally { $stream.Dispose() }
        }
        $sha.TransformFinalBlock([byte[]]::new(0), 0, 0) | Out-Null
        [System.Convert]::ToHexString($sha.Hash).ToLowerInvariant()
    }
    finally { $sha.Dispose() }
}

function Get-AssetNames {
    # The names a file goes up under: its own, or <name>.part1 ... when it is over the split limit.
    param([string]$Name, [long]$Length, [long]$SplitAbove = $splitAbove, [long]$PartSize = $partSize)
    if ($Length -le $SplitAbove) { return @($Name) }
    $count = [int][Math]::Ceiling($Length / $PartSize)
    @(1..$count | ForEach-Object { "$Name.part$_" })
}

function Get-StaleAssetNames {
    # Assets a rerun must delete before it uploads: ones of a file this run sends (whole or in parts) that this run does not send under that name.
    param([string[]]$Existing, [string[]]$Sending)
    $bases = @($Sending | ForEach-Object { $_ -replace '\.part\d+$', '' } | Sort-Object -Unique)
    @($Existing | Where-Object { $_ -and ($bases -contains ($_ -replace '\.part\d+$', '')) -and ($Sending -notcontains $_) })
}

function Assert-PartsNumbered {
    param([string]$Base, [int[]]$Numbers)
    $sorted = @($Numbers | Sort-Object)
    if (($sorted -join ',') -ne ((1..$sorted.Count) -join ',')) { throw "The parts of $Base are not numbered 1 to $($sorted.Count)." }
}

# Load only defines the functions above, so tests can call them.
if ($Action -eq 'Load') { return }

if (-not $Tag -or $Tag -notmatch '^v.+') { throw "The tag is '$Tag'; a release tag starts with v." }

switch ($Action) {
    'Verify' {
        $version = Get-ProjectVersion
        if ($Tag -ne "v$version") {
            throw "The tag is $Tag but Directory.Build.props says $version, so the packages would not be the version the tag names. Tag v$version, or change the version and tag again."
        }
        $token = Get-Token
        Write-Host "==> $Tag matches version $version; using $($token.Name)"
        $repository = Invoke-ApiJson -Path "/repos/$(Get-Repository)"
        if ($repository.permissions -and -not $repository.permissions.push) {
            Write-Warning "$($token.Name) cannot write to the repository. If the upload fails, add a repository secret RELEASE_TOKEN: an access token with write access to releases."
        }
        Get-DraftRelease $Tag | Out-Null
        $sources = Get-SourcesRelease
        Write-Host "==> the sources release $($sources.tag_name) exists"
    }

    'Upload' {
        if (-not $Directory -or -not (Test-Path -LiteralPath $Directory -PathType Container)) { throw "-Directory '$Directory' is not a folder." }
        $files = @(Get-ChildItem -LiteralPath $Directory -File | Sort-Object Name)
        if ($files.Count -eq 0) { throw "$Directory holds no files to upload." }
        $release = Get-DraftRelease $Tag -Create
        $scratch = Join-Path ([System.IO.Path]::GetTempPath()) ("release-parts-" + [guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $scratch | Out-Null
        try {
            # A rerun may send a file differently than the run before (whole instead of in parts, or in fewer parts); what the earlier
            # run left under the old names would be taken for part of the file.
            $sending = @($files | ForEach-Object { Get-AssetNames $_.Name $_.Length })
            $existing = @(@(Invoke-ApiJson -Path "/repos/$(Get-Repository)/releases/$($release.id)/assets") | Where-Object { $_ } | ForEach-Object { $_.name })
            foreach ($stale in Get-StaleAssetNames $existing $sending) {
                Write-Host "==> removing $stale, which this run does not send"
                Remove-ReleaseAsset $release $stale
            }
            foreach ($file in $files) {
                if ($file.Length -le $splitAbove) {
                    Send-ReleaseAsset $release $file.FullName $file.Name
                    continue
                }
                Write-Host ('==> {0} is {1:N0} bytes, over the upload limit: sending it in parts' -f $file.Name, $file.Length)
                foreach ($part in Split-Asset $file.FullName $scratch) {
                    Send-ReleaseAsset $release $part (Split-Path -Leaf $part)
                    Remove-Item -LiteralPath $part -Force
                }
            }
        }
        finally { Remove-Item -LiteralPath $scratch -Recurse -Force -ErrorAction SilentlyContinue }
        Write-Host "==> $($files.Count) file(s) are on the draft release for $Tag"
    }

    'Checksums' {
        $release = Get-DraftRelease $Tag
        if (-not $release) { throw "There is no draft release for $Tag; the package jobs should have made it." }
        $release = Invoke-ApiJson -Path "/repos/$(Get-Repository)/releases/$($release.id)"
        $apiOrigin = ([uri](Get-ApiBase)).GetLeftPart([System.UriPartial]::Authority)
        $assets = @($release.assets | Where-Object { $_.name -ne $checksumName -and $_.name -notlike "$sourcesLinkPrefix*" } | Sort-Object name)

        # A release without one of the three downloads would be published by mistake as a finished one.
        $whole = $assets.name | ForEach-Object { $_ -replace '\.part\d+$', '' } | Sort-Object -Unique
        foreach ($required in 'SavePoint.AppImage', 'SavePoint-win-Setup.exe', 'SavePoint-win-Portable.zip') {
            if ($whole -notcontains $required) { throw "The release has no $required (or its parts), so a package job did not finish." }
        }

        $scratch = Join-Path ([System.IO.Path]::GetTempPath()) ("release-sums-" + [guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $scratch | Out-Null
        try {
            $lines = @()
            $partFiles = @{}
            foreach ($asset in $assets) {
                $target = Join-Path $scratch $asset.name
                Write-Host ('==> reading {0} ({1:N0} bytes)' -f $asset.name, $asset.size)
                $url = $apiOrigin + ([uri]$asset.browser_download_url).PathAndQuery
                $response = Invoke-Api -Url $url -OutFile $target -Follow
                if ($response.Status -ne 200) { throw "Downloading $($asset.name) answered $($response.Status)." }
                if ((Get-Item -LiteralPath $target).Length -ne $asset.size) { throw "$($asset.name) came back with a different size than the release lists." }
                $lines += [pscustomobject]@{ Name = $asset.name; Hash = (Get-StreamHash @($target)) }
                if ($asset.name -match '^(.+)\.part(\d+)$') {
                    if (-not $partFiles.ContainsKey($Matches[1])) { $partFiles[$Matches[1]] = @{} }
                    $partFiles[$Matches[1]][[int]$Matches[2]] = $target
                }
            }
            # A split file also gets a line of its own, the hash of the parts joined in order.
            foreach ($base in $partFiles.Keys) {
                $numbers = @($partFiles[$base].Keys | Sort-Object)
                Assert-PartsNumbered $base $numbers
                $lines += [pscustomobject]@{ Name = $base; Hash = (Get-StreamHash @($numbers | ForEach-Object { $partFiles[$base][$_] })) }
            }
            $sums = Join-Path $scratch $checksumName
            $text = ($lines | Sort-Object Name | ForEach-Object { "$($_.Hash)  $($_.Name)" }) -join "`n"
            [System.IO.File]::WriteAllText($sums, $text + "`n", [System.Text.UTF8Encoding]::new($false))
            Write-Host $text
            Send-ReleaseAsset $release $sums $checksumName
        }
        finally { Remove-Item -LiteralPath $scratch -Recurse -Force -ErrorAction SilentlyContinue }

        # The source code of the bundled git is its own release; this row in the downloads list points at it.
        $sources = Get-SourcesRelease
        $linkName = "$sourcesLinkPrefix $(Get-BundledGitVersion)"
        Remove-ReleaseAsset $release $linkName
        $link = Invoke-Api -Method POST -Path "/repos/$(Get-Repository)/releases/$($release.id)/assets?name=$([uri]::EscapeDataString($linkName))" -Extra @('--form', "external_url=$($sources.html_url)")
        if ($link.Status -ne 201) { throw "Adding the link to the sources release answered $($link.Status): $($link.Text)" }
        Write-Host "==> linked $($sources.html_url) as '$linkName'"
        Write-Host "The release for $Tag is still a draft: read it, then publish it by hand."
    }
}
