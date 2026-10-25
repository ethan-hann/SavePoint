# Functions for the source-archive step of build/package.ps1. Dot-sourced by it and by the tests, never run on its own.

function Get-PackageSourceArchives {
    # The MSYS2 and MINGW packages inside the Windows git bundle are listed, with their hashes, in build/msys2-sources.json.
    param([Parameter(Mandatory)][string]$ManifestPath)
    $listed = (Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json -AsHashtable)['archives']
    if (-not $listed -or $listed.Count -eq 0) {
        throw 'build/msys2-sources.json lists no source archives in "archives".'
    }
    foreach ($archive in $listed) {
        [pscustomobject]@{ File = $archive['file']; Url = $archive['url']; Sha256 = "$($archive['sha256'])".Trim().ToLowerInvariant() }
    }
}

function Get-TagSourcePins {
    # The three tags whose source archives a release publishes, pinned in the "sources" of build/git-manifest.json, sorted by key.
    param([Parameter(Mandatory)][string]$ManifestPath)
    $listed = (Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json -AsHashtable)['sources']
    if (-not $listed -or $listed.Count -ne 3) {
        $count = if ($listed) { $listed.Count } else { 0 }
        throw "build/git-manifest.json has $count source entries in `"sources`"; expected 3 (git, Git for Windows, dugite-native)."
    }
    [string[]]$keys = $listed.Keys
    [Array]::Sort($keys, [System.StringComparer]::Ordinal)
    foreach ($key in $keys) {
        [pscustomobject]@{ Key = $key; Repository = $listed[$key].repository; Tag = $listed[$key].tag; Sha256 = "$($listed[$key].sha256)".Trim().ToLowerInvariant() }
    }
}

function Remove-StalePackageSources {
    # Deletes the regular files directly inside the folder that the list does not name, and returns their names. Links, folders and
    # anything below a folder stay. An empty list is refused, so a list that failed to read cannot empty the folder.
    param([Parameter(Mandatory)][string]$Directory, [Parameter(Mandatory)][AllowEmptyCollection()][object[]]$Archives)
    if ($Archives.Count -eq 0) {
        throw "Refusing to clean $Directory against an empty list of source archives."
    }
    if (-not (Test-Path -LiteralPath $Directory -PathType Container)) { return }
    $listed = [System.Collections.Generic.HashSet[string]]::new([string[]]@($Archives | ForEach-Object { $_.File }), [System.StringComparer]::Ordinal)
    foreach ($item in Get-ChildItem -LiteralPath $Directory -File -Force) {
        if ($item.LinkType -or $listed.Contains($item.Name)) { continue }
        Remove-Item -LiteralPath $item.FullName -Force
        $item.Name
    }
}
