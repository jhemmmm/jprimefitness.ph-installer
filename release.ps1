<#
.SYNOPSIS
  Cuts a JPrimePanel release: tags the current commit as v<Version> and pushes the tag.
  GitHub Actions (.github/workflows/release.yml) then builds JPrimePanel.exe and attaches it to the release.

.EXAMPLE
  .\release.ps1 -Version 1.0.0            # tag + push; CI builds and publishes the release
  .\release.ps1 -Version 1.0.0 -Local     # build here and upload with the gh CLI instead of waiting for CI
#>
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [switch]$Local
)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Version must look like 1.2.3 (got '$Version')" }
$tag = "v$Version"

$dirty = git status --porcelain
if ($dirty) { throw "Working tree has uncommitted changes; commit or stash them first:`n$($dirty -join "`n")" }
if (git tag -l $tag) { throw "Tag $tag already exists. Delete it first: git tag -d $tag; git push origin :refs/tags/$tag" }

$branch = git rev-parse --abbrev-ref HEAD
git fetch origin --quiet
$behind = git rev-list --count "HEAD..origin/$branch" 2>$null
if ($behind -and [int]$behind -gt 0) { throw "Local $branch is behind origin/$branch; pull first." }
$ahead = git rev-list --count "origin/$branch..HEAD" 2>$null
if ($ahead -and [int]$ahead -gt 0) {
    Write-Host "Pushing $ahead local commit(s) on $branch"
    git push origin $branch
    if ($LASTEXITCODE -ne 0) { throw 'git push failed' }
}

git tag -a $tag -m "JPrime Panel $tag"
git push origin $tag
if ($LASTEXITCODE -ne 0) { throw 'git push (tag) failed' }
Write-Host "Tagged $tag at $(git rev-parse --short HEAD)"

if ($Local) {
    if (-not (Get-Command gh -ErrorAction SilentlyContinue)) { throw 'gh CLI not found (winget install GitHub.cli; gh auth login)' }
    & (Join-Path $PSScriptRoot 'publish.ps1') -Version $Version
    $exe = Join-Path $PSScriptRoot 'dist\JPrimePanel.exe'
    $hash = (Get-FileHash $exe -Algorithm SHA256).Hash.ToLower()
    "$hash  JPrimePanel.exe" | Set-Content "$exe.sha256" -Encoding ascii
    gh release create $tag $exe "$exe.sha256" --title "JPrime Panel $tag" --notes "JPrime Panel $tag"
    if ($LASTEXITCODE -ne 0) { throw 'gh release create failed' }
    Write-Host "Published https://github.com/jhemmmm/jprimefitness.ph-installer/releases/tag/$tag (CI run for the same tag will attach nothing new)"
}
else {
    Write-Host 'GitHub Actions is building the release:'
    Write-Host '  https://github.com/jhemmmm/jprimefitness.ph-installer/actions'
    Write-Host "  https://github.com/jhemmmm/jprimefitness.ph-installer/releases/tag/$tag  (ready in ~3 minutes)"
}
