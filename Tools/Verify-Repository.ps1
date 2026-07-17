[CmdletBinding()]
param(
    [string] $RepositoryRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $scriptPath = $MyInvocation.MyCommand.Path
    if ([string]::IsNullOrWhiteSpace($scriptPath)) {
        throw 'Could not resolve the verifier script path. Pass -RepositoryRoot explicitly.'
    }

    $RepositoryRoot = Split-Path -Parent (Split-Path -Parent $scriptPath)
}

$root = [IO.Path]::GetFullPath($RepositoryRoot)
$issues = [Collections.Generic.List[string]]::new()

function Add-Issue([string] $Message) {
    $issues.Add($Message)
    Write-Host "[FAIL] $Message" -ForegroundColor Red
}

function Add-Pass([string] $Message) {
    Write-Host "[ OK ] $Message" -ForegroundColor Green
}

function Get-PublishableFiles {
    $roots = @(
        'Assets',
        'Packages',
        'ProjectSettings',
        'Samples~',
        'cloudscript-azure-functions-monetization',
        '.github',
        'Tools'
    )

    $extensions = @(
        '.asmdef', '.asmref', '.asset', '.cs', '.csproj', '.h', '.json',
        '.md', '.mm', '.plist', '.props', '.ps1', '.shader', '.sh', '.slnx',
        '.txt', '.xml', '.yaml', '.yml'
    )

    foreach ($relativeRoot in $roots) {
        $candidate = Join-Path $root $relativeRoot
        if (-not (Test-Path -LiteralPath $candidate)) {
            continue
        }

        Get-ChildItem -LiteralPath $candidate -Recurse -File -Force -ErrorAction Stop |
            Where-Object {
                $extensions -contains $_.Extension.ToLowerInvariant() -and
                $_.FullName -notmatch '[\\/](Library|Temp|Logs|obj|bin)[\\/]'
            }
    }

    foreach ($relativePath in @(
        'README.md',
        'TEMPLATE_README.md',
        'LICENSE',
        'CHANGELOG.md',
        'CONTRIBUTING.md',
        'SECURITY.md',
        'CODE_OF_CONDUCT.md',
        'THIRD_PARTY_NOTICES.md',
        'Directory.Build.props',
        '.gitignore',
        '.gitattributes'
    )) {
        $candidate = Join-Path $root $relativePath
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            Get-Item -LiteralPath $candidate -Force
        }
    }
}

Write-Host "Serhat Forge repository verification" -ForegroundColor Cyan
Write-Host "Root: $root"

$requiredFiles = @(
    'README.md',
    'LICENSE',
    'CHANGELOG.md',
    'CONTRIBUTING.md',
    'SECURITY.md',
    'CODE_OF_CONDUCT.md',
    'THIRD_PARTY_NOTICES.md',
    '.gitignore',
    '.gitattributes',
    'ProjectSettings/ProjectVersion.txt',
    'ProjectSettings/Packages/com.unity.services.core/Settings.json',
    'Packages/manifest.json',
    'Assets/AddressableAssetsData/AddressableAssetSettings.asset',
    'Assets/link.xml',
    'Assets/Tests/EditMode/Serhat.Forge.Tests.EditMode.asmdef',
    'Assets/Tests/EditMode/CompositionAssetTests.cs',
    'Assets/Tests/PlayMode/Serhat.Forge.Tests.PlayMode.asmdef',
    'Assets/Tests/PlayMode/CompositionPlayModeTests.cs',
    '.github/workflows/cloud-tests.yml'
)

foreach ($relativePath in $requiredFiles) {
    if (-not (Test-Path -LiteralPath (Join-Path $root $relativePath))) {
        Add-Issue "Required public-repository file is missing: $relativePath"
    }
}

$publishableFiles = @(Get-PublishableFiles)
$textCache = @{}

foreach ($file in $publishableFiles) {
    try {
        $textCache[$file.FullName] = [IO.File]::ReadAllText($file.FullName)
    }
    catch {
        Add-Issue "Could not read text file: $($file.FullName)"
    }
}

$projectSettingsPath = Join-Path $root 'ProjectSettings/ProjectSettings.asset'
if (Test-Path -LiteralPath $projectSettingsPath) {
    $settings = [IO.File]::ReadAllText($projectSettingsPath)
    if ($settings -match '(?m)^[ \t]*ps4Passcode:[ \t]*\S+[ \t]*$') {
        Add-Issue 'ProjectSettings contains a non-empty PS4 passcode.'
    }

    foreach ($field in @('switchApplicationID', 'ps4ContentID')) {
        if ($settings -match "(?m)^[ \t]*$field[ \t]*:[ \t]*\S+[ \t]*$") {
            Add-Issue "ProjectSettings contains a non-empty platform identity: $field."
        }
    }

    if ($settings -match '(?m)^[ \t]*scriptingDefineSymbols:.*\bUNITY_PURCHASING\b') {
        Add-Issue 'UNITY_PURCHASING must remain an explicit opt-in in the public template.'
    }

    if ($settings -match '(?ms)^[ \t]*cloudServicesEnabled:[ \t]*\r?\n[ \t]+Purchasing:[ \t]*1[ \t]*$') {
        Add-Issue 'Legacy Purchasing cloud service must be disabled in the public template.'
    }
}

$unityConnectSettingsPath = Join-Path $root 'ProjectSettings/UnityConnectSettings.asset'
if (Test-Path -LiteralPath $unityConnectSettingsPath) {
    $unityConnectSettings = [IO.File]::ReadAllText($unityConnectSettingsPath)
    if ($unityConnectSettings -match '(?ms)^[ \t]*UnityPurchasingSettings:[ \t]*\r?\n[ \t]+m_Enabled:[ \t]*1[ \t]*$') {
        Add-Issue 'Unity Purchasing automatic service initialization must be disabled.'
    }

    if ($unityConnectSettings -match '(?ms)^[ \t]*UnityAdsSettings:.*?^[ \t]+m_InitializeOnStartup:[ \t]*1[ \t]*$') {
        Add-Issue 'Unity Ads automatic initialization must be disabled.'
    }
}

$addressablesSettingsPath = Join-Path $root 'Assets/AddressableAssetsData/AddressableAssetSettings.asset'
if (Test-Path -LiteralPath $addressablesSettingsPath) {
    $addressablesSettings = [IO.File]::ReadAllText($addressablesSettingsPath)
    if ($addressablesSettings -notmatch '(?m)^[ \t]*m_BuildRemoteCatalog:[ \t]*0[ \t]*$') {
        Add-Issue 'Remote Addressables catalog builds must remain disabled in the public template.'
    }

    if ($addressablesSettings -notmatch '(?m)^[ \t]*m_BuildAddressablesWithPlayerBuild:[ \t]*1[ \t]*$') {
        Add-Issue 'Addressables content must build deterministically with player builds.'
    }
}

$ugsSettingsPath = Join-Path $root 'ProjectSettings/Packages/com.unity.services.core/Settings.json'
if (Test-Path -LiteralPath $ugsSettingsPath) {
    $ugs = Get-Content -LiteralPath $ugsSettingsPath -Raw | ConvertFrom-Json
    if (-not [string]::IsNullOrWhiteSpace([string] $ugs.EnvironmentName)) {
        Add-Issue 'Unity Gaming Services EnvironmentName must be unset in the public template.'
    }

    $environmentId = [string] $ugs.EnvironmentId
    if (-not [string]::Equals(
        $environmentId,
        '00000000-0000-0000-0000-000000000000',
        [StringComparison]::OrdinalIgnoreCase)) {
        Add-Issue 'Unity Gaming Services EnvironmentId must be unset (Guid.Empty) in the public template.'
    }
}

$secretPatterns = [ordered]@{
    'private key material' = '-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----'
    'GitHub token' = '\bgh[pousr]_[A-Za-z0-9_]{36,255}\b'
    'Google API key' = '\bAIza[0-9A-Za-z_-]{35}\b'
    'Azure Storage account key' = '(?i)AccountKey=[A-Za-z0-9+/]{40,}={0,2}'
    'AWS access key' = '\b(?:AKIA|ASIA)[A-Z0-9]{16}\b'
    'Slack credential' = '(?:https://hooks\.slack\.com/services/[A-Z0-9]{8,}/[A-Z0-9]{8,}/[A-Za-z0-9]{20,}|\bxox[baprs]-[A-Za-z0-9-]{20,})'
    'Stripe live secret' = '\bsk_live_[0-9A-Za-z]{24,}\b'
    'generic connection-string password' = '(?i)(?:^|[;\s])Password=[^;\s"''<>]{8,}'
}
foreach ($entry in $textCache.GetEnumerator()) {
    if ($entry.Value.Contains([char] 0xFFFD)) {
        Add-Issue "Invalid UTF-8 replacement character detected in $($entry.Key.Substring($root.Length + 1))."
    }

    foreach ($secretPattern in $secretPatterns.GetEnumerator()) {
        if ($entry.Value -match $secretPattern.Value) {
            Add-Issue "$($secretPattern.Key) detected in $($entry.Key.Substring($root.Length + 1))."
        }
    }
}

$localeDirectory = Join-Path $root 'Assets/StreamingAssets/Localization/Locales'
$localePaths = [ordered]@{
    'en' = Join-Path $localeDirectory 'en.json'
    'tr' = Join-Path $localeDirectory 'tr.json'
}
$localeKeySets = @{}
foreach ($locale in $localePaths.GetEnumerator()) {
    if (-not (Test-Path -LiteralPath $locale.Value)) {
        Add-Issue "Localization catalog is missing: $($locale.Value.Substring($root.Length + 1))."
        continue
    }

    try {
        $catalog = [IO.File]::ReadAllText($locale.Value) | ConvertFrom-Json
        $localeKeySets[$locale.Key] = @($catalog.psobject.Properties.Name | Sort-Object -Unique)
    }
    catch {
        # Invalid JSON is reported by the generic JSON gate below.
    }
}

if ($localeKeySets.ContainsKey('en') -and $localeKeySets.ContainsKey('tr')) {
    $localeDifference = @(Compare-Object $localeKeySets['en'] $localeKeySets['tr'])
    if ($localeDifference.Count -gt 0) {
        $details = $localeDifference | ForEach-Object { "$($_.InputObject) ($($_.SideIndicator))" }
        Add-Issue "Localization keys differ between en.json and tr.json: $($details -join ', ')."
    }
}

$localizationCsvPath = Join-Path $localeDirectory 'Localization.csv'
if (Test-Path -LiteralPath $localizationCsvPath) {
    try {
        $csvRows = @([IO.File]::ReadAllText($localizationCsvPath) | ConvertFrom-Csv)
        $catalogRows = @($csvRows | Where-Object {
            -not [string]::IsNullOrWhiteSpace([string] $_.key) -and
            -not ([string] $_.key).StartsWith('#', [StringComparison]::Ordinal)
        })
        $duplicateCsvKeys = @($catalogRows | Group-Object key | Where-Object Count -gt 1)
        foreach ($duplicate in $duplicateCsvKeys) {
            Add-Issue "Localization.csv contains duplicate key: $($duplicate.Name)."
        }

        if ($localeKeySets.ContainsKey('en')) {
            $csvKeys = @($catalogRows.key | Sort-Object -Unique)
            $csvDifference = @(Compare-Object $localeKeySets['en'] $csvKeys)
            if ($csvDifference.Count -gt 0) {
                $details = $csvDifference | ForEach-Object { "$($_.InputObject) ($($_.SideIndicator))" }
                Add-Issue "Localization.csv keys differ from JSON catalogs: $($details -join ', ')."
            }
        }
    }
    catch {
        Add-Issue 'Localization.csv could not be parsed.'
    }
}
else {
    Add-Issue 'Localization.csv is missing.'
}

$riskyNames = @(
    '.env',
    'google-services.json',
    'GoogleService-Info.plist',
    'service-account.json'
)
foreach ($name in $riskyNames) {
    Get-ChildItem -LiteralPath $root -Recurse -File -Force -Filter $name -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '[\\/](Library|Temp|Logs|obj|bin)[\\/]' } |
        ForEach-Object { Add-Issue "Risky configuration file is present: $($_.FullName.Substring($root.Length + 1))." }
}

$legacyDiPatterns = @(
    '\bServiceContainer\b',
    '\bMonoInjector\b',
    '\bInjectableBehaviour\b',
    '\bIServiceModule\b',
    'Serhat.Forge.DI.InjectAttribute'
)
$firstPartyCode = $publishableFiles | Where-Object {
    $_.Extension -eq '.cs' -and $_.FullName -notmatch '[\\/]Assets[\\/]Plugins[\\/]Zenject[\\/]'
}
foreach ($file in $firstPartyCode) {
    $text = $textCache[$file.FullName]
    foreach ($pattern in $legacyDiPatterns) {
        if ($text -match $pattern) {
            Add-Issue "Legacy custom DI symbol detected in $($file.FullName.Substring($root.Length + 1)): $pattern"
        }
    }
}

$embeddedPackagesRoot = Join-Path $root 'Packages'
Get-ChildItem -LiteralPath $embeddedPackagesRoot -Directory -Filter 'com.serhat.*' -ErrorAction Stop |
    ForEach-Object {
        foreach ($requiredPackageFile in @('package.json', 'LICENSE.md')) {
            if (-not (Test-Path -LiteralPath (Join-Path $_.FullName $requiredPackageFile))) {
                Add-Issue "Embedded package $($_.Name) is missing $requiredPackageFile."
            }
        }
    }
# Embedded UPM packages must declare dependencies for cross-package asmdef references.
$embeddedPackageRoots = @(Get-ChildItem -LiteralPath $embeddedPackagesRoot -Directory -Filter 'com.serhat.*' -ErrorAction Stop)
$assemblyOwners = @{}
foreach ($packageRoot in $embeddedPackageRoots) {
    Get-ChildItem -LiteralPath $packageRoot.FullName -Recurse -File -Filter '*.asmdef' -ErrorAction Stop |
        ForEach-Object {
            try {
                $asmdef = [IO.File]::ReadAllText($_.FullName) | ConvertFrom-Json
                if (-not [string]::IsNullOrWhiteSpace([string] $asmdef.name)) {
                    $assemblyOwners[[string] $asmdef.name] = $packageRoot.Name
                }
            }
            catch {
                # Invalid JSON is reported by the generic JSON gate below.
            }
        }
}

foreach ($packageRoot in $embeddedPackageRoots) {
    $packageManifestPath = Join-Path $packageRoot.FullName 'package.json'
    if (-not (Test-Path -LiteralPath $packageManifestPath)) {
        continue
    }

    try {
        $packageManifest = [IO.File]::ReadAllText($packageManifestPath) | ConvertFrom-Json
    }
    catch {
        continue
    }

    $declaredDependencies = @{}
    if ($null -ne $packageManifest.dependencies) {
        foreach ($property in @($packageManifest.dependencies.psobject.Properties)) {
            if ($null -ne $property -and -not [string]::IsNullOrWhiteSpace([string] $property.Name)) {
                $declaredDependencies[[string] $property.Name] = $true
            }
        }
    }

    Get-ChildItem -LiteralPath $packageRoot.FullName -Recurse -File -Filter '*.asmdef' -ErrorAction Stop |
        ForEach-Object {
            try {
                $asmdef = [IO.File]::ReadAllText($_.FullName) | ConvertFrom-Json
                foreach ($reference in @($asmdef.references)) {
                    if ([string]::IsNullOrWhiteSpace([string] $reference)) {
                        continue
                    }

                    $ownerPackage = $assemblyOwners[[string] $reference]
                    if ($ownerPackage -and
                        $ownerPackage -ne $packageRoot.Name -and
                        -not $declaredDependencies.ContainsKey($ownerPackage)) {
                        Add-Issue "Embedded package $($packageRoot.Name) assembly $($asmdef.name) references $reference but package.json does not declare $ownerPackage."
                    }
                }
            }
            catch {
                # Invalid JSON is reported by the generic JSON gate below.
            }
        }
}
$jsonFiles = $publishableFiles | Where-Object {
    $_.Extension -in @('.json', '.asmdef', '.asmref')
}
foreach ($file in $jsonFiles) {
    try {
        $null = $textCache[$file.FullName] | ConvertFrom-Json
    }
    catch {
        Add-Issue "Invalid JSON: $($file.FullName.Substring($root.Length + 1))"
    }
}

$metaGuids = @{}
Get-ChildItem -LiteralPath $root -Recurse -File -Force -Filter '*.meta' -ErrorAction Stop |
    Where-Object { $_.FullName -notmatch '[\\/](Library|Temp|Logs|obj|bin)[\\/]' } |
    ForEach-Object {
        $match = Select-String -LiteralPath $_.FullName -Pattern '^guid:[ \t]*([0-9a-fA-F]{32})[ \t]*$' |
            Select-Object -First 1
        if ($null -eq $match) {
            Add-Issue "Meta file has no valid GUID: $($_.FullName.Substring($root.Length + 1))"
            return
        }

        $guid = $match.Matches[0].Groups[1].Value.ToLowerInvariant()
        if ($metaGuids.ContainsKey($guid)) {
            Add-Issue "Duplicate meta GUID $guid in $($metaGuids[$guid]) and $($_.FullName.Substring($root.Length + 1))"
        }
        else {
            $metaGuids[$guid] = $_.FullName.Substring($root.Length + 1)
        }
    }

if (Test-Path -LiteralPath (Join-Path $root '.git')) {
    $tracked = @(& git -C $root ls-files)
    $sourceProjectFiles = @(
        'Samples~/GameApiBackend/Serhat.Forge.CloudScript.csproj',
        'Samples~/GameApiBackend/tests/Serhat.Forge.CloudScript.Tests.csproj',
        'cloudscript-azure-functions-monetization/Serhat.Forge.Monetization.CloudScript.csproj'
    )
    foreach ($path in $tracked) {
        if ($path -match '(^|/)(Library|Temp|Logs|UserSettings|obj|bin)/' -or
            $path -match '\.(sln|user)$' -or
            ($path -match '\.csproj$' -and $path -notin $sourceProjectFiles)) {
            Add-Issue "Generated file is tracked: $path"
        }
    }
}

if ($issues.Count -gt 0) {
    Write-Host ""
    Write-Host "Repository verification failed with $($issues.Count) issue(s)." -ForegroundColor Red
    exit 1
}

Add-Pass "Required files, safe settings, secrets, localization, JSON, package dependencies, DI migration, and meta GUID checks passed."
exit 0
