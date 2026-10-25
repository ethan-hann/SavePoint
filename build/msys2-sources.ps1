#Requires -Version 7.4
<#
.SYNOPSIS
Writes build/msys2-sources.json from the package list of a MinGit bundle.

.DESCRIPTION
Run it by hand when the pinned MinGit changes. It reads etc/package-versions.txt in the bundle folder (resources/git/win-x64 once the
build has fetched it), finds the source archive of every package the way build-extra/get-sources.sh in Git for Windows does, downloads
each archive into a cache folder, hashes it, and writes the list the package scripts read. The list is written only when every package
has an archive; otherwise the packages without one are reported and nothing is written.

Where a package's source is looked for, in order, per candidate source package:

  1. The Git for Windows pacman-repo release that published that exact version (its x86_64/<package>.versions.json names the tag).
  2. repo.msys2.org: /msys/sources/ for MSYS packages, /mingw/sources/mingw-w64-<name> for MINGW packages, as .src.tar.zst, then .src.tar.gz.

A package built from another package's source (libatomic from gcc, curl-winssl from curl) is looked for under the source package. The
archives are named like the cache files package.ps1 downloads, so the default cache is shared with it.

Each archive is also checked against the .sig beside it, with gpg and only the keys pinned in build/keys: repo.msys2.org signs with the MSYS2
development key (Christoph Reiter's, from msys2-keyring), and the pacman-repo releases with the Git for Windows release key (Johannes
Schindelin's, from build-extra/git-for-windows-keyring). A bad, missing or foreign signature stops the run and removes what it downloaded.
The keys carry an expiry (the Git for Windows one runs out in 2027-03); replace the file in build/keys when the project publishes a new one.

-Action Load only defines the functions, so tests can call them.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('Generate', 'Load')]
    [string]$Action,

    # The extracted MinGit bundle (the folder that holds etc/package-versions.txt).
    [string]$BundleDirectory = (Join-Path (Split-Path -Parent $PSScriptRoot) 'resources/git/win-x64'),

    # Where the list is written. Pass a temporary file to compare it with the checked-in one first.
    [string]$OutFile = (Join-Path $PSScriptRoot 'msys2-sources.json'),

    # Where the archives are kept. An archive already here is hashed instead of downloaded again.
    [string]$CacheDirectory = (Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts/release/source/packages'),

    # The MinGit version the bundle is; build/git-manifest.json says which one the build fetches.
    [string]$BundleVersion,

    # The day the archives were downloaded, recorded in the list.
    [string]$Date = (Get-Date -Format 'yyyy-MM-dd'),

    # Skips checking each archive's .sig against the pinned keys in build/keys (for a machine without gpg).
    [switch]$SkipSignatures
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$pacmanRepoRaw = 'https://raw.githubusercontent.com/git-for-windows/pacman-repo/refs/heads/x86_64'
$pacmanRepoReleases = 'https://github.com/git-for-windows/pacman-repo/releases/download'
$msysSources = 'https://repo.msys2.org/msys/sources'
$mingwSources = 'https://repo.msys2.org/mingw/sources'

# Who signs the archives at each address. The fingerprint is the signing key's primary key, so a signature by one of its subkeys counts.
$keyDirectory = Join-Path $PSScriptRoot 'keys'
$signers = @(
    [pscustomobject]@{
        Name        = 'the MSYS2 development key'
        Fingerprint = '5F944B027F7FE2091985AA2EFA11531AA0AA7F57'
        KeyFile     = 'msys2-development.asc'
        UrlPrefixes = @('https://repo.msys2.org/')
    }
    [pscustomobject]@{
        Name        = 'the Git for Windows release key'
        Fingerprint = 'E8325679DFFF09668AD8D7B67115A57376871B1C'
        KeyFile     = 'gfw-release.asc'
        UrlPrefixes = @("$pacmanRepoReleases/")
    }
)

# Packages built as a secondary output of another package's source, as in get-sources.sh, plus the ones its SDK lookup finds by grepping PKGBUILDs.
$msysSourceOf = @{
    'gcc-libs'     = 'gcc'
    'heimdal-libs' = 'heimdal'
    'libdb'        = 'db'
    'libpcre'      = 'pcre'
    'libreadline'  = 'readline'
    'libserf'      = 'serf'
    'libsqlite'    = 'sqlite'
    'libintl'      = 'gettext'
    'libsasl'      = 'cyrus-sasl'
    'libpcre2_8'   = 'pcre2'
    'libnghttp2'   = 'nghttp2'
    'libopenssl'   = 'openssl'
}
$mingwSourceOf = @{
    'cc-libs'            = 'gcc'
    'gcc-libs'           = 'gcc'
    'gcc-libgfortran'    = 'gcc'
    'libatomic'          = 'gcc'
    'libgcc'             = 'gcc'
    'libgomp'            = 'gcc'
    'libquadmath'        = 'gcc'
    'libstdc++'          = 'gcc'
    'gitk'               = 'git'
    'libwinpthread'      = 'winpthreads'
    'libwinpthread-git'  = 'winpthreads'
}

function Read-PackageVersions {
    <# One object per distinct "name version" line of a package-versions.txt, in file order. A line without a version is an error. #>
    param([Parameter(Mandatory)][string]$Path)
    $seen = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($line in [System.IO.File]::ReadAllLines($Path)) {
        $text = $line.Trim()
        if (-not $text) { continue }
        if ($text -notmatch '^(?<name>\S+)\s+(?<version>\S+)$') {
            throw "$Path has a line that is not '<package> <version>': $text"
        }
        if ($seen.Add("$($Matches['name']) $($Matches['version'])")) {
            [pscustomobject]@{ Name = $Matches['name']; Version = $Matches['version'] }
        }
    }
}

function Get-UrlText {
    # The text at a URL, or $null when there is none (404).
    param([Parameter(Mandatory)][string]$Url)
    $headers = @{}
    # raw.githubusercontent.com limits anonymous requests tightly.
    if ($env:GITHUB_TOKEN -and $Url.StartsWith('https://raw.githubusercontent.com/', [StringComparison]::Ordinal)) {
        $headers['Authorization'] = "Bearer $env:GITHUB_TOKEN"
    }
    # Reading the status ourselves keeps a 404 from being retried as if it were a hiccup.
    $response = Invoke-WebRequest -Uri $Url -Headers $headers -SkipHttpErrorCheck
    if ($response.StatusCode -eq 404) { return $null }
    if ($response.StatusCode -ge 400) { throw "$Url answered $($response.StatusCode)." }
    $response.Content
}

function Invoke-UrlDownload {
    # Saves a URL to a file. $true when it did, $false when there is nothing at that address (404). Any other failure throws.
    param([Parameter(Mandatory)][string]$Url, [Parameter(Mandatory)][string]$OutFile)
    for ($attempt = 1; $attempt -le 3; $attempt++) {
        try {
            $response = Invoke-WebRequest -Uri $Url -OutFile $OutFile -PassThru -SkipHttpErrorCheck
            if ($response.StatusCode -lt 400) { return $true }
            Remove-Item -LiteralPath $OutFile -Force -ErrorAction SilentlyContinue
            if ($response.StatusCode -eq 404) { return $false }
            $failure = "$Url answered $($response.StatusCode)."
        }
        catch {
            Remove-Item -LiteralPath $OutFile -Force -ErrorAction SilentlyContinue
            $failure = $_.Exception.Message
        }
        Start-Sleep -Seconds 2
    }
    throw $failure
}

function Get-ReleaseTag {
    # The pacman-repo release tag that published this exact version of a package, or $null.
    param([string]$Package, [string]$Version, [scriptblock]$GetText, [hashtable]$Cache)
    $url = "$pacmanRepoRaw/$Package.versions.json"
    if (-not $Cache.ContainsKey($url)) { $Cache[$url] = & $GetText $url }
    $text = $Cache[$url]
    if (-not $text) { return $null }
    $versions = $text | ConvertFrom-Json -AsHashtable
    if ($versions.ContainsKey($Version)) { return [string]$versions[$Version] }
    $null
}

function Get-SourceBases {
    # The source packages a package may be built from, most likely first. A package built from another's source is listed by that name only.
    param([Parameter(Mandatory)][string]$Bare, [Parameter(Mandatory)][bool]$IsMingw)
    $known = if ($IsMingw) { $mingwSourceOf } else { $msysSourceOf }
    if ($known.ContainsKey($Bare)) { return @($known[$Bare]) }

    $bases = [System.Collections.Generic.List[string]]::new()
    $bases.Add($Bare)
    # curl-winssl is built from curl, git-gui from git.
    $shorter = $Bare
    while ($shorter.Contains('-')) {
        $shorter = $shorter.Substring(0, $shorter.LastIndexOf('-'))
        $bases.Add($shorter)
    }
    if ($Bare.StartsWith('lib', [StringComparison]::Ordinal) -and $Bare.Length -gt 3) { $bases.Add($Bare.Substring(3)) }
    $bases | Select-Object -Unique
}

function Get-SourceCandidates {
    <#
    The addresses a package's source archive may be at, in the order to try them: for each possible source package, its pacman-repo
    release first (only when the release list names this version), then the msys2.org mirror.
    #>
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$Version,
        [scriptblock]$GetText = { param($Url) Get-UrlText $Url },
        [hashtable]$Cache = @{}
    )
    $isMingw = $Name -match '^(?<prefix>mingw-w64-(?:ucrt-x86_64|clang-x86_64|clang-aarch64|x86_64|i686)-)(?<bare>.+)$'
    $prefix = if ($isMingw) { $Matches['prefix'] } else { '' }
    $bare = if ($isMingw) { $Matches['bare'] } else { $Name }

    foreach ($base in Get-SourceBases -Bare $bare -IsMingw $isMingw) {
        $stem = if ($isMingw) { "mingw-w64-$base-$Version" } else { "$base-$Version" }
        $tag = Get-ReleaseTag -Package "$prefix$base" -Version $Version -GetText $GetText -Cache $Cache
        if ($tag) {
            [pscustomobject]@{ Base = $base; File = "$stem.src.tar.gz"; Url = "$pacmanRepoReleases/$tag/$stem.src.tar.gz" }
        }
        $mirror = if ($isMingw) { $mingwSources } else { $msysSources }
        foreach ($extension in 'zst', 'gz') {
            [pscustomobject]@{ Base = $base; File = "$stem.src.tar.$extension"; Url = "$mirror/$stem.src.tar.$extension" }
        }
    }
}

function Find-PackageSources {
    <#
    Finds, downloads (into the cache folder) and hashes the source archive of every package. Returns the archives, one per file with the
    packages it provides, and the packages no address had an archive for. Packages built from one source share its archive.
    #>
    param(
        [Parameter(Mandatory)][object[]]$Packages,
        [Parameter(Mandatory)][string]$CacheDirectory,
        [scriptblock]$Download = { param($Url, $OutFile) Invoke-UrlDownload -Url $Url -OutFile $OutFile },
        [scriptblock]$GetText = { param($Url) Get-UrlText $Url },
        # Called with the archive, its signature file and the archive's address; returns $null when the signature is good, else the reason it is not.
        [scriptblock]$VerifySignature
    )
    New-Item -ItemType Directory -Force -Path $CacheDirectory | Out-Null
    $textCache = @{}
    $found = @{}
    $failed = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $missing = [System.Collections.Generic.List[object]]::new()

    foreach ($package in $Packages) {
        $label = "$($package.Name) $($package.Version)"
        $tried = [System.Collections.Generic.List[string]]::new()
        $archive = $null
        foreach ($candidate in Get-SourceCandidates -Name $package.Name -Version $package.Version -GetText $GetText -Cache $textCache) {
            if ($found.ContainsKey($candidate.File)) { $archive = $found[$candidate.File]; break }
            if ($failed.Contains($candidate.Url)) { $tried.Add($candidate.Url); continue }

            $target = Join-Path $CacheDirectory $candidate.File
            $signature = "$target.sig"
            $cached = (Test-Path -LiteralPath $target -PathType Leaf) -and (Get-Item -LiteralPath $target).Length -gt 0
            if (-not $cached) {
                # The archive takes its name only once it is whole.
                $partial = "$target.part"
                Write-Verbose "downloading $($candidate.Url)"
                if (& $Download $candidate.Url $partial) {
                    Move-Item -LiteralPath $partial -Destination $target -Force
                }
                else {
                    Remove-Item -LiteralPath $partial -Force -ErrorAction SilentlyContinue
                    [void]$failed.Add($candidate.Url)
                    $tried.Add($candidate.Url)
                    continue
                }
            }
            if ($VerifySignature) {
                $problem = $null
                if (-not (Test-Path -LiteralPath $signature -PathType Leaf)) {
                    $partialSignature = "$signature.part"
                    if (& $Download "$($candidate.Url).sig" $partialSignature) {
                        Move-Item -LiteralPath $partialSignature -Destination $signature -Force
                    }
                    else {
                        Remove-Item -LiteralPath $partialSignature -Force -ErrorAction SilentlyContinue
                        $problem = "it has no signature at $($candidate.Url).sig"
                    }
                }
                if (-not $problem) { $problem = & $VerifySignature $target $signature $candidate.Url }
                if ($problem) {
                    # Only what this run fetched is removed; an archive that was already in the folder stays for a person to look at.
                    if (-not $cached) { Remove-Item -LiteralPath $target, $signature -Force -ErrorAction SilentlyContinue }
                    throw "$($candidate.File) from $($candidate.Url) is not trusted: $problem."
                }
            }
            $archive = [pscustomobject]@{
                File     = $candidate.File
                Url      = $candidate.Url
                Sha256   = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash.ToLowerInvariant()
                Size     = (Get-Item -LiteralPath $target).Length
                Provides = [System.Collections.Generic.List[string]]::new()
            }
            $found[$candidate.File] = $archive
            break
        }
        if ($archive) {
            $archive.Provides.Add($label)
            Write-Verbose "$label -> $($archive.File)"
        }
        else {
            $missing.Add([pscustomobject]@{ Package = $label; Tried = $tried.ToArray() })
        }
    }

    $archives = foreach ($file in (Get-OrdinalSorted $found.Keys)) {
        $archive = $found[$file]
        $archive.Provides = [System.Collections.Generic.List[string]](Get-OrdinalSorted $archive.Provides)
        $archive
    }
    [pscustomobject]@{ Archives = @($archives); Missing = $missing.ToArray() }
}

function Get-OrdinalSorted {
    # Sort-Object compares by culture, which skips hyphens; the list is ordered by code point.
    param([string[]]$Values)
    $copy = [string[]]@($Values)
    [Array]::Sort($copy, [StringComparer]::Ordinal)
    $copy
}

function Get-SignatureVerdict {
    <#
    Reads the --status-fd output of gpg --verify. Returns $null when the archive has a valid signature by the key with this primary
    fingerprint, else the reason it does not. An expired key still counts: the signature was made while it was valid.
    #>
    param([string[]]$Status, [Parameter(Mandatory)][string]$Fingerprint)
    $lines = @($Status | Where-Object { $_ })
    if ($lines | Where-Object { $_ -match '^\[GNUPG:\] BADSIG ' }) { return 'bad signature: the archive is not what was signed' }
    if ($lines | Where-Object { $_ -match '^\[GNUPG:\] REVKEYSIG ' }) { return 'the signing key was revoked' }
    if ($lines | Where-Object { $_ -match '^\[GNUPG:\] ERRSIG ' }) { return 'the signature could not be checked (signed by a key that is not pinned, or gpg failed)' }
    $valid = @($lines | Where-Object { $_ -match '^\[GNUPG:\] VALIDSIG ' })
    if ($valid.Count -eq 0) { return 'no valid signature' }
    foreach ($line in $valid) {
        # The last field is the primary key's fingerprint, whichever of its subkeys signed.
        if (($line -split ' ')[-1] -ieq $Fingerprint) { return $null }
    }
    "signed by key $(($valid[0] -split ' ')[-1]), not the pinned $Fingerprint"
}

function New-SignatureVerifier {
    <#
    A verifier over a private gpg home that holds only the pinned keys, so the person's own keyring neither helps nor is touched. Its Verify
    method takes the archive, the signature file and the archive's address and returns $null or a reason; call Dispose when done.
    #>
    param([string]$KeyDirectory = $keyDirectory, [object[]]$Signers = $signers)
    if (-not (Get-Command gpg -ErrorAction SilentlyContinue)) {
        throw 'gpg is not installed (Gpg4win on Windows, gnupg elsewhere). Install it, or pass -SkipSignatures to go without the check.'
    }
    $gnupgHome = Join-Path ([System.IO.Path]::GetTempPath()) "savepoint-gnupg-$([guid]::NewGuid().ToString('N'))"
    New-Item -ItemType Directory -Path $gnupgHome | Out-Null
    try {
        foreach ($signer in $Signers) {
            & gpg --homedir $gnupgHome --batch --quiet --import (Join-Path $KeyDirectory $signer.KeyFile) 2>$null
            if ($LASTEXITCODE -ne 0) { throw "gpg could not import $($signer.KeyFile) from $KeyDirectory." }
            # The file has to hold the key the fingerprint pins, not just some key.
            & gpg --homedir $gnupgHome --batch --quiet --list-keys $signer.Fingerprint 2>$null | Out-Null
            if ($LASTEXITCODE -ne 0) { throw "$($signer.KeyFile) does not hold $($signer.Name) ($($signer.Fingerprint))." }
        }
    }
    catch {
        Remove-Item -LiteralPath $gnupgHome -Recurse -Force -ErrorAction SilentlyContinue
        throw
    }
    $verifier = [pscustomobject]@{ Home = $gnupgHome; Signers = $Signers }
    $verifier | Add-Member -MemberType ScriptMethod -Name Verify -Value {
        param([string]$Archive, [string]$Signature, [string]$Url)
        $signer = $this.Signers | Where-Object { $prefix = $_.UrlPrefixes | Where-Object { $Url.StartsWith($_, [StringComparison]::Ordinal) }; $prefix } | Select-Object -First 1
        if (-not $signer) { return "no pinned key for archives at $Url" }
        $status = & gpg --homedir $this.Home --batch --no-tty --status-fd 1 --trust-model always --verify $Signature $Archive 2>$null
        Get-SignatureVerdict -Status @($status) -Fingerprint $signer.Fingerprint
    }
    $verifier | Add-Member -MemberType ScriptMethod -Name Dispose -Value {
        if (Get-Command gpgconf -ErrorAction SilentlyContinue) { & gpgconf --homedir $this.Home --kill gpg-agent 2>$null }
        Remove-Item -LiteralPath $this.Home -Recurse -Force -ErrorAction SilentlyContinue
    }
    $verifier
}

function ConvertTo-JsonText {
    param([Parameter(Mandatory)][string]$Text)
    $builder = [System.Text.StringBuilder]::new('"')
    foreach ($char in $Text.ToCharArray()) {
        switch ($char) {
            '"' { [void]$builder.Append('\"') }
            '\' { [void]$builder.Append('\\') }
            default {
                if ([int]$char -lt 0x20) { [void]$builder.AppendFormat('\u{0:x4}', [int]$char) }
                else { [void]$builder.Append($char) }
            }
        }
    }
    [void]$builder.Append('"')
    $builder.ToString()
}

function ConvertTo-SourceListJson {
    <# The text of build/msys2-sources.json: two-space indent, Unix line endings, a final newline, archives ordered by file name. #>
    param([Parameter(Mandatory)][object[]]$Archives, [Parameter(Mandatory)][string]$BundleVersion, [Parameter(Mandatory)][string]$Date)
    $hashSource = "downloaded once on $Date and hashed; the msys2 and mingw entries come from repo.msys2.org, the rest from Git for Windows' pacman-repo releases, where build-extra/get-sources.sh looks first"
    $ordered = foreach ($file in (Get-OrdinalSorted @($Archives | ForEach-Object { $_.File }))) { $Archives | Where-Object { $_.File -ceq $file } | Select-Object -First 1 }

    $entries = foreach ($archive in $ordered) {
        @(
            '    {'
            "      `"file`": $(ConvertTo-JsonText $archive.File),"
            "      `"url`": $(ConvertTo-JsonText $archive.Url),"
            "      `"sha256`": $(ConvertTo-JsonText $archive.Sha256),"
            "      `"size`": $([long]$archive.Size),"
            "      `"provides`": $(ConvertTo-JsonText (@($archive.Provides) -join ', '))"
            '    }'
        ) -join "`n"
    }
    @(
        '{'
        '  "bundle": "win-x64",'
        "  `"bundleVersion`": $(ConvertTo-JsonText $BundleVersion),"
        "  `"listedIn`": $(ConvertTo-JsonText "etc/package-versions.txt inside MinGit-$BundleVersion-64-bit.zip"),"
        "  `"hashSource`": $(ConvertTo-JsonText $hashSource),"
        '  "archives": ['
        ($entries -join ",`n")
        '  ]'
        '}'
    ) -join "`n" | ForEach-Object { "$_`n" }
}

function Invoke-SourceListGeneration {
    <# Reads the bundle's package list, finds every source archive and writes the list; throws, writing nothing, when a package has none. #>
    param(
        [Parameter(Mandatory)][string]$BundleDirectory,
        [Parameter(Mandatory)][string]$OutFile,
        [Parameter(Mandatory)][string]$CacheDirectory,
        [Parameter(Mandatory)][string]$BundleVersion,
        [Parameter(Mandatory)][string]$Date,
        [scriptblock]$Download = { param($Url, $OutFile) Invoke-UrlDownload -Url $Url -OutFile $OutFile },
        [scriptblock]$GetText = { param($Url) Get-UrlText $Url },
        [scriptblock]$VerifySignature
    )
    $listing = Join-Path $BundleDirectory 'etc/package-versions.txt'
    if (-not (Test-Path -LiteralPath $listing -PathType Leaf)) {
        throw "$BundleDirectory has no etc/package-versions.txt; pass the folder the MinGit bundle was extracted to."
    }
    $packages = @(Read-PackageVersions -Path $listing)
    if ($packages.Count -eq 0) { throw "$listing lists no packages." }

    $result = Find-PackageSources -Packages $packages -CacheDirectory $CacheDirectory -Download $Download -GetText $GetText -VerifySignature $VerifySignature
    if ($result.Missing.Count -gt 0) {
        $lines = foreach ($entry in $result.Missing) { "  $($entry.Package) (tried $($entry.Tried -join ', '))" }
        throw "$($result.Missing.Count) package(s) have no source archive:`n$($lines -join "`n")"
    }

    New-Item -ItemType Directory -Force -Path (Split-Path -Parent ([System.IO.Path]::GetFullPath($OutFile))) | Out-Null
    $text = ConvertTo-SourceListJson -Archives $result.Archives -BundleVersion $BundleVersion -Date $Date
    [System.IO.File]::WriteAllText([System.IO.Path]::GetFullPath($OutFile), $text, [System.Text.UTF8Encoding]::new($false))
    $result
}

# Load only defines the functions above, so tests can call them.
if ($Action -eq 'Load') { return }

if (-not $BundleVersion) {
    $manifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'git-manifest.json') -Raw | ConvertFrom-Json -AsHashtable
    $BundleVersion = $manifest['win-x64']['version']
}
$VerbosePreference = 'Continue'
$verifier = $null
try {
    $verify = $null
    if ($SkipSignatures) {
        Write-Warning 'Archive signatures are not being checked.'
    }
    else {
        $verifier = New-SignatureVerifier
        $verify = { param($Archive, $Signature, $Url) $verifier.Verify($Archive, $Signature, $Url) }
    }
    $result = Invoke-SourceListGeneration -BundleDirectory $BundleDirectory -OutFile $OutFile -CacheDirectory $CacheDirectory -BundleVersion $BundleVersion -Date $Date -VerifySignature $verify
    Write-Host "==> $(@($result.Archives).Count) archives for $(@($result.Archives | ForEach-Object { $_.Provides }).Count) packages written to $OutFile"
}
catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    exit 1
}
finally {
    if ($verifier) { $verifier.Dispose() }
}
