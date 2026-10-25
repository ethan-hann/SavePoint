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
             Before sending, it hashes every file, every part and each split file whole, and sends that list as SHA256SUMS-<folder>.txt
             too; the job also writes the list to its step output "sums" for the workflow to hand on.
  Checksums  After every package job: merges the lists the jobs handed over (SUMS_* variables) after checking them against the
             names and sizes of the release's assets, writes SHA256SUMS.txt (one line per file and per part, plus one for each file
             the parts make up) and uploads it, removes the per-job lists, and adds a link to the sources release of the bundled
             git. No file is downloaded from the draft: the workflow's token can upload to it but not read it back.

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
# Each package job uploads SHA256SUMS-<folder>.txt; the last job merges them and removes them.
$sumsListPrefix = 'SHA256SUMS-'
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

function Get-SumEntries {
    # Hashes a file as it will go up: one entry for the file, or one per part (cut into $Folder, Path set) plus a joined entry (no Path)
    # for a file over the split limit. Path is what to send; the joined entry has none because no such asset exists.
    param([string]$Path, [string]$Folder, [long]$SplitAbove = $splitAbove, [long]$PartSize = $partSize)
    $file = Get-Item -LiteralPath $Path
    if ($file.Length -le $SplitAbove) {
        return [pscustomobject]@{ Name = $file.Name; Hash = (Get-StreamHash @($file.FullName)); Size = $file.Length; Path = $file.FullName }
    }
    $parts = @(Split-Asset $file.FullName $Folder $PartSize)
    foreach ($part in $parts) {
        $info = Get-Item -LiteralPath $part
        [pscustomobject]@{ Name = $info.Name; Hash = (Get-StreamHash @($part)); Size = $info.Length; Path = $info.FullName }
    }
    [pscustomobject]@{ Name = $file.Name; Hash = (Get-StreamHash $parts); Size = $file.Length; Path = $null }
}

function Sort-Ordinal {
    # Name order that does not depend on the culture of the machine that runs the job.
    param([Parameter(ValueFromPipeline)]$Entry, [string]$Property = 'Name')
    begin { $all = [System.Collections.Generic.List[object]]::new() }
    process { $all.Add($Entry) }
    end {
        $all.Sort([Comparison[object]] { param($left, $right) [string]::CompareOrdinal($left.$Property, $right.$Property) })
        $all.ToArray()
    }
}

function Format-SumList {
    # One line per entry, "<hash> <size> <name>": the size lets the last job check the list against what the release holds.
    param($Entries)
    $text = (@($Entries | Sort-Ordinal) | ForEach-Object { "$($_.Hash) $($_.Size) $($_.Name)" }) -join "`n"
    $text + "`n"
}

function Read-SumList {
    param([string]$Text)
    foreach ($line in ($Text -split "\r?\n")) {
        if (-not $line.Trim()) { continue }
        if ($line -notmatch '^(\S+)\s+(\d+)\s+(.+?)\s*$') { throw "A hash list has a line that is not '<hash> <size> <name>': $line" }
        [pscustomobject]@{ Name = $Matches[3]; Hash = $Matches[1]; Size = [long]$Matches[2] }
    }
}

function Merge-Sums {
    # Checks the hash lists of the package jobs against the assets the release holds and returns the lines of SHA256SUMS.txt.
    # Nothing is downloaded: a list is the word of the job that uploaded the file, and the release's own sizes must agree with it.
    param([string[]]$Lists, $Assets)
    $entries = @{}
    foreach ($list in $Lists) {
        foreach ($entry in Read-SumList $list) {
            if ($entries.ContainsKey($entry.Name)) { throw "$($entry.Name) is in more than one hash list." }
            $entries[$entry.Name] = $entry
        }
    }
    $held = @{}
    foreach ($asset in $Assets) { $held[$asset.name] = $asset }
    foreach ($asset in @($Assets | Sort-Ordinal -Property name)) {
        if (-not $entries.ContainsKey($asset.name)) { throw "$($asset.name) is on the release but in none of the hash lists." }
        if ([long]$asset.size -ne $entries[$asset.name].Size) {
            throw "$($asset.name) is $($asset.size) bytes on the release but its list says $($entries[$asset.name].Size)."
        }
    }
    # A joined entry (a split file's own line) has no asset; every other entry must.
    $partBases = @(@($entries.Keys) + @($held.Keys) | Where-Object { $_ -match '\.part\d+$' } | ForEach-Object { $_ -replace '\.part\d+$', '' } | Sort-Object -Unique)
    foreach ($name in @($entries.Keys | Sort-Object)) {
        if (-not $held.ContainsKey($name) -and $partBases -notcontains $name) { throw "$name is in a hash list but not on the release." }
    }
    foreach ($base in $partBases) {
        $numbers = @($held.Keys | Where-Object { $_ -match '^(.+)\.part(\d+)$' -and $Matches[1] -eq $base } | ForEach-Object { [int]($_ -replace '^.*\.part', '') })
        Assert-PartsNumbered $base $numbers
    }
    @($entries.Values | Sort-Ordinal) | ForEach-Object { "$($_.Hash)  $($_.Name)" }
}

function Get-ListsFromRelease {
    # Fallback for a run whose workflow did not hand the lists over: the lists are small, but a draft's files can be unreadable
    # to the workflow's token, so this is not the way the jobs normally pass them.
    param($Release, [string]$Scratch)
    $apiOrigin = ([uri](Get-ApiBase)).GetLeftPart([System.UriPartial]::Authority)
    foreach ($asset in @($Release.assets | Where-Object { $_.name -like "$sumsListPrefix*" })) {
        $target = Join-Path $Scratch $asset.name
        $response = Invoke-Api -Url ($apiOrigin + ([uri]$asset.browser_download_url).PathAndQuery) -OutFile $target -Follow
        if ($response.Status -ne 200) { throw "Reading $($asset.name) answered $($response.Status). Pass the lists through the workflow (SUMS_* variables) instead." }
        Get-Content -LiteralPath $target -Raw
    }
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
            # Hash everything first: the last job cannot read the draft's files back, so it trusts these lists, which it checks against
            # the sizes the release lists.
            $entries = @()
            foreach ($file in $files) {
                if ($file.Length -gt $splitAbove) {
                    Write-Host ('==> {0} is {1:N0} bytes, over the upload limit: sending it in parts' -f $file.Name, $file.Length)
                }
                $entries += @(Get-SumEntries $file.FullName $scratch)
            }
            $label = Split-Path -Leaf ($Directory.TrimEnd('/', '\'))
            $listName = "$sumsListPrefix$label.txt"
            $listText = Format-SumList $entries
            $listPath = Join-Path $scratch $listName
            [System.IO.File]::WriteAllText($listPath, $listText, [System.Text.UTF8Encoding]::new($false))
            Write-Host $listText
            foreach ($entry in $entries | Where-Object { $_.Path }) {
                Send-ReleaseAsset $release $entry.Path $entry.Name
                if ($entry.Path.StartsWith($scratch, [System.StringComparison]::Ordinal)) { Remove-Item -LiteralPath $entry.Path -Force }
            }
            Send-ReleaseAsset $release $listPath $listName
            # The next job reads the list from this output, not from the release.
            if ($env:GITHUB_OUTPUT) {
                $delimiter = 'EOF_' + [guid]::NewGuid().ToString('N')
                [System.IO.File]::AppendAllText($env:GITHUB_OUTPUT, "sums<<$delimiter`n$listText$delimiter`n", [System.Text.UTF8Encoding]::new($false))
            }
        }
        finally { Remove-Item -LiteralPath $scratch -Recurse -Force -ErrorAction SilentlyContinue }
        Write-Host "==> $($files.Count) file(s) are on the draft release for $Tag"
    }

    'Checksums' {
        $release = Get-DraftRelease $Tag
        if (-not $release) { throw "There is no draft release for $Tag; the package jobs should have made it." }
        $release = Invoke-ApiJson -Path "/repos/$(Get-Repository)/releases/$($release.id)"
        $listAssets = @($release.assets | Where-Object { $_.name -like "$sumsListPrefix*" })
        $assets = @($release.assets | Where-Object { $_.name -ne $checksumName -and $_.name -notlike "$sumsListPrefix*" -and $_.name -notlike "$sourcesLinkPrefix*" } | Sort-Object name)

        # A release without one of the three downloads would be published by mistake as a finished one.
        $whole = $assets.name | ForEach-Object { $_ -replace '\.part\d+$', '' } | Sort-Object -Unique
        foreach ($required in 'SavePoint.AppImage', 'SavePoint-win-Setup.exe', 'SavePoint-win-Portable.zip') {
            if ($whole -notcontains $required) { throw "The release has no $required (or its parts), so a package job did not finish." }
        }

        $scratch = Join-Path ([System.IO.Path]::GetTempPath()) ("release-sums-" + [guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $scratch | Out-Null
        try {
            # The package jobs hand their lists over as SUMS_* variables; only a run without them reads the lists off the release.
            $lists = @(Get-ChildItem env: | Where-Object { $_.Name -like 'SUMS_*' -and $_.Value.Trim() } | ForEach-Object { $_.Value })
            if ($lists.Count -eq 0) { $lists = @(Get-ListsFromRelease $release $scratch) }
            if ($lists.Count -eq 0) { throw 'No package job handed over a hash list, and the release holds none.' }
            $lines = @(Merge-Sums $lists $assets)
            $sums = Join-Path $scratch $checksumName
            $text = $lines -join "`n"
            [System.IO.File]::WriteAllText($sums, $text + "`n", [System.Text.UTF8Encoding]::new($false))
            Write-Host $text
            Send-ReleaseAsset $release $sums $checksumName
            # The per-job lists have done their work; a token that cannot delete leaves them, and they are harmless there.
            foreach ($listAsset in $listAssets) {
                try { Remove-ReleaseAsset $release $listAsset.name }
                catch { Write-Warning "Could not remove $($listAsset.name) from the release: $($_.Exception.Message)" }
            }
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
