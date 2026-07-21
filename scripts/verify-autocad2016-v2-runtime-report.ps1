[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $ManifestPath,

    [Parameter(Mandatory = $true)]
    [string] $EvidencePath
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

# Error codes
$ErrorCodes = @{
    manifest_invalid = 'manifest_invalid'
    evidence_invalid = 'evidence_invalid'
    unknown_field = 'unknown_field'
    duplicate_field = 'duplicate_field'
    candidate_identity_mismatch = 'candidate_identity_mismatch'
    commit_invalid = 'commit_invalid'
    artifact_sha_invalid = 'artifact_sha_invalid'
    schema_mismatch = 'schema_mismatch'
    coverage_incomplete = 'coverage_incomplete'
    count_mismatch = 'count_mismatch'
    complete_flag_mismatch = 'complete_flag_mismatch'
    dbmod_changed = 'dbmod_changed'
    plugin_save_observed = 'plugin_save_observed'
    cad_write_observed = 'cad_write_observed'
    runtime_binding_invalid = 'runtime_binding_invalid'
    sensitive_field_rejected = 'sensitive_field_rejected'
    sensitive_value_rejected = 'sensitive_value_rejected'
    prohibited_tool_behavior = 'prohibited_tool_behavior'
}

# Validation result class
class ValidationResult {
    [string] $Code
    [string] $Path
    [string] $Message

    ValidationResult([string] $code, [string] $path, [string] $message) {
        $this.Code = $code
        $this.Path = $path
        $this.Message = $message
    }
}

# Sensitive patterns - only flag actual sensitive information, not generic numbers or JSON
$SensitivePatterns = @(
    # File paths with drive letters
    '[A-Za-z]:\\(?:Users|Documents and Settings|Windows|Program Files)[^\s]*',
    # UNC paths
    '\\\\[a-zA-Z0-9]+\\[^\s]+',
    # TRUSTEDPATHS/SECURELOAD configuration
    'TRUSTEDPATHS\s*=',
    'SECURELOAD\s*=',
    # API Key patterns (with actual key values)
    'api[_-]?key\s*[=:]\s*\S+',
    'apikey\s*[=:]\s*\S+',
    'secret\s*[=:]\s*\S+',
    'token\s*[=:]\s*\S+',
    'password\s*[=:]\s*\S+',
    # Stack trace patterns with actual stack frames
    'at\s+\w+\.\w+\.\w+\.?\w*\(',
    'System\.\w+Exception:',
    'Stack Trace:',
    # DWG/DXF file paths
    '[A-Za-z]:\\[^\\/:*?"<>|\s]+\.dwg',
    '[A-Za-z]:\\[^\\/:*?"<>|\s]+\.dxf'
)

# Known manifest fields
$ManifestFields = @(
    '$schema'
    'schemaVersion'
    'candidateId'
    'commit'
    'hostDllSha256'
    'moduleVersion'
    'schema'
    'schemaVersion_target'
    'status'
    'recordedAtUtc'
    'testMatrix'
    'testMatrix.sampleA'
    'testMatrix.sampleA.line'
    'testMatrix.sampleA.circle'
    'testMatrix.sampleA.polyline'
    'testMatrix.sampleA.dbText'
    'testMatrix.sampleA.mText'
    'testMatrix.sampleA.blockReference'
    'testMatrix.sampleB'
    'testMatrix.sampleB.arc'
    'testMatrix.sampleB.ellipse'
    'testMatrix.sampleB.spline'
    'testMatrix.sampleB.point'
    'testMatrix.sampleB.ray'
    'testMatrix.sampleB.xline'
    'testMatrix.sampleC'
    'testMatrix.sampleC.polyline2d'
    'testMatrix.sampleC.polyline3d'
    'testMatrix.sampleC.dimension'
    'testMatrix.sampleD'
    'testMatrix.sampleD.hatch'
    'testMatrix.sampleD.leader'
    'testMatrix.sampleD.mLeader'
    'testMatrix.sampleD.table'
    'testMatrix.sampleE'
    'testMatrix.sampleE.unknownEntityType'
    'testMatrix.sampleE.entityReadFailed'
    'testMatrix.sampleE.entityDataLimit'
    'coverage'
    'coverage.strongTypeCount'
    'coverage.placeholderReasonCount'
    'coverage.mixedSelectionTested'
    'coverage.dbmodUnchanged'
    'coverage.documentSwitchClearsContext'
    'coverage.noCadWrite'
    'coverage.noSaveCall'
    'coverage.noSavetimeModification'
)

# Known evidence fields
$EvidenceFields = @(
    '$schema'
    'schemaVersion'
    'candidateId'
    'commit'
    'hostDllSha256'
    'moduleVersion'
    'schema'
    'schemaVersion_target'
    'status'
    'recordedAtUtc'
    'testId'
    'testType'
    'entityTypes'
    'placeholderReasons'
    'selection'
    'selection.entityCount'
    'selection.parsedEntityCount'
    'selection.unsupportedEntityCount'
    'selection.complete'
    'dbmod'
    'dbmod.before'
    'dbmod.after'
    'dbmod.unchanged'
    'documentSwitch'
    'documentSwitch.tested'
    'documentSwitch.contextCleared'
    'safety'
    'safety.noCadWrite'
    'safety.noSaveCall'
    'safety.noSavetimeModification'
    'safety.noAutoLisp'
    'safety.noComAutomation'
    'safety.noSendKeys'
    'safety.noScriptInjection'
    'safety.noNetload'
    'safety.noRegistryModification'
    'notes'
)

function Test-StrictUtf8NoBom {
    param([string] $Path)

    $bytes = [System.IO.File]::ReadAllBytes($Path)

    # Check for BOM
    if ($bytes.Length -ge 3 -and
        $bytes[0] -eq 0xEF -and
        $bytes[1] -eq 0xBB -and
        $bytes[2] -eq 0xBF) {
        return $false
    }

    # Validate UTF-8 encoding
    try {
        $utf8 = [System.Text.UTF8Encoding]::new($false, $true)
        $null = $utf8.GetString($bytes)
        return $true
    }
    catch {
        return $false
    }
}

function Test-ValidJson {
    param([string] $Path)

    try {
        $null = Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json
        return $true
    }
    catch {
        return $false
    }
}

function Get-JsonFields {
    param([PSCustomObject] $Object, [string] $Prefix = '')

    $fields = @()

    foreach ($prop in $Object.PSObject.Properties) {
        $fieldName = if ($Prefix) { "$Prefix.$($prop.Name)" } else { $prop.Name }
        $fields += $fieldName

        if ($prop.Value -is [PSCustomObject]) {
            $fields += Get-JsonFields -Object $prop.Value -Prefix $fieldName
        }
    }

    return $fields
}

function Test-CommitSha {
    param([string] $Sha)

    # Must be exactly 40 hex characters
    return $Sha -match '^[0-9a-f]{40}$'
}

function Test-ArtifactSha {
    param([string] $Sha)

    # Must be exactly 64 hex characters (SHA-256)
    return $Sha -match '^[0-9a-f]{64}$'
}

function Test-SensitiveValue {
    param([string] $Value)

    foreach ($pattern in $SensitivePatterns) {
        if ($Value -match $pattern) {
            return $true
        }
    }

    return $false
}

function Test-ValidEntityType {
    param([string] $Type)

    $validTypes = @(
        'line', 'circle', 'polyline', 'dbText', 'mText', 'blockReference',
        'arc', 'ellipse', 'spline', 'point', 'ray', 'xline',
        'polyline2d', 'polyline3d', 'dimension', 'hatch', 'leader', 'mLeader',
        'table', 'unsupported'
    )

    return $Type -in $validTypes
}

function Test-ValidPlaceholderReason {
    param([string] $Reason)

    $validReasons = @(
        'unknown-entity-type',
        'entity-read-failed',
        'entity-data-limit'
    )

    return $Reason -in $validReasons
}

function Validate-Manifest {
    param([PSCustomObject] $Manifest)

    $failures = @()

    # Check required fields
    $requiredFields = @('candidateId', 'commit', 'hostDllSha256', 'moduleVersion', 'schema', 'schemaVersion_target')
    foreach ($field in $requiredFields) {
        if (-not $Manifest.PSObject.Properties[$field]) {
            $failures += [ValidationResult]::new('manifest_invalid', $field, "Required field missing: $field")
        }
    }

    # Validate commit SHA
    if ($Manifest.commit -and -not (Test-CommitSha -Sha $Manifest.commit)) {
        $failures += [ValidationResult]::new('commit_invalid', 'commit', 'Commit SHA must be exactly 40 lowercase hex characters')
    }

    # Validate artifact SHA
    if ($Manifest.hostDllSha256 -and -not (Test-ArtifactSha -Sha $Manifest.hostDllSha256)) {
        $failures += [ValidationResult]::new('artifact_sha_invalid', 'hostDllSha256', 'Host DLL SHA-256 must be exactly 64 lowercase hex characters')
    }

    # Validate schema
    if ($Manifest.schema -ne 'codex.autocad.cad-context') {
        $failures += [ValidationResult]::new('schema_mismatch', 'schema', 'Schema must be codex.autocad.cad-context')
    }

    if ($Manifest.schemaVersion_target -ne 2) {
        $failures += [ValidationResult]::new('schema_mismatch', 'schemaVersion_target', 'Schema version must be 2')
    }

    # Validate coverage
    if ($Manifest.coverage) {
        if ($Manifest.coverage.strongTypeCount -ne 19) {
            $failures += [ValidationResult]::new('coverage_incomplete', 'coverage.strongTypeCount', 'Strong type count must be 19')
        }

        if ($Manifest.coverage.placeholderReasonCount -ne 3) {
            $failures += [ValidationResult]::new('coverage_incomplete', 'coverage.placeholderReasonCount', 'Placeholder reason count must be 3')
        }

        if (-not $Manifest.coverage.mixedSelectionTested) {
            $failures += [ValidationResult]::new('coverage_incomplete', 'coverage.mixedSelectionTested', 'Mixed selection must be tested')
        }

        if (-not $Manifest.coverage.dbmodUnchanged) {
            $failures += [ValidationResult]::new('coverage_incomplete', 'coverage.dbmodUnchanged', 'DBMOD unchanged must be verified')
        }

        if (-not $Manifest.coverage.documentSwitchClearsContext) {
            $failures += [ValidationResult]::new('coverage_incomplete', 'coverage.documentSwitchClearsContext', 'Document switch must clear context')
        }

        if (-not $Manifest.coverage.noCadWrite) {
            $failures += [ValidationResult]::new('coverage_incomplete', 'coverage.noCadWrite', 'No CAD write must be verified')
        }

        if (-not $Manifest.coverage.noSaveCall) {
            $failures += [ValidationResult]::new('coverage_incomplete', 'coverage.noSaveCall', 'No save call must be verified')
        }

        if (-not $Manifest.coverage.noSavetimeModification) {
            $failures += [ValidationResult]::new('coverage_incomplete', 'coverage.noSavetimeModification', 'No SAVETIME modification must be verified')
        }
    }

    # Validate test matrix
    if ($Manifest.testMatrix) {
        $samples = @('sampleA', 'sampleB', 'sampleC', 'sampleD', 'sampleE')
        foreach ($sample in $samples) {
            if ($Manifest.testMatrix.$sample) {
                foreach ($prop in $Manifest.testMatrix.$sample.PSObject.Properties) {
                    if ($prop.Value -eq $false) {
                        $failures += [ValidationResult]::new('coverage_incomplete', "testMatrix.$sample.$($prop.Name)", "Test not completed: $sample.$($prop.Name)")
                    }
                }
            }
        }
    }

    return $failures
}

function Validate-Evidence {
    param([PSCustomObject] $Evidence, [PSCustomObject] $Manifest)

    $failures = @()

    # Check required fields
    $requiredFields = @('candidateId', 'commit', 'hostDllSha256', 'moduleVersion', 'schema', 'schemaVersion_target', 'testId', 'testType')
    foreach ($field in $requiredFields) {
        if (-not $Evidence.PSObject.Properties[$field]) {
            $failures += [ValidationResult]::new('evidence_invalid', $field, "Required field missing: $field")
        }
    }

    # Validate candidate identity consistency
    if ($Evidence.candidateId -ne $Manifest.candidateId) {
        $failures += [ValidationResult]::new('candidate_identity_mismatch', 'candidateId', 'Candidate ID does not match manifest')
    }

    if ($Evidence.commit -ne $Manifest.commit) {
        $failures += [ValidationResult]::new('candidate_identity_mismatch', 'commit', 'Commit SHA does not match manifest')
    }

    if ($Evidence.hostDllSha256 -ne $Manifest.hostDllSha256) {
        $failures += [ValidationResult]::new('candidate_identity_mismatch', 'hostDllSha256', 'Host DLL SHA-256 does not match manifest')
    }

    if ($Evidence.moduleVersion -ne $Manifest.moduleVersion) {
        $failures += [ValidationResult]::new('candidate_identity_mismatch', 'moduleVersion', 'Module version does not match manifest')
    }

    # Validate schema
    if ($Evidence.schema -ne 'codex.autocad.cad-context') {
        $failures += [ValidationResult]::new('schema_mismatch', 'schema', 'Schema must be codex.autocad.cad-context')
    }

    if ($Evidence.schemaVersion_target -ne 2) {
        $failures += [ValidationResult]::new('schema_mismatch', 'schemaVersion_target', 'Schema version must be 2')
    }

    # Validate entity types
    if ($Evidence.entityTypes) {
        foreach ($type in $Evidence.entityTypes) {
            if (-not (Test-ValidEntityType -Type $type)) {
                $failures += [ValidationResult]::new('evidence_invalid', 'entityTypes', "Invalid entity type: $type")
            }
        }
    }

    # Validate placeholder reasons
    if ($Evidence.placeholderReasons) {
        foreach ($reason in $Evidence.placeholderReasons) {
            if (-not (Test-ValidPlaceholderReason -Reason $reason)) {
                $failures += [ValidationResult]::new('evidence_invalid', 'placeholderReasons', "Invalid placeholder reason: $reason")
            }
        }
    }

    # Validate selection counts
    if ($Evidence.selection) {
        $entityCount = $Evidence.selection.entityCount
        $parsedEntityCount = $Evidence.selection.parsedEntityCount
        $unsupportedEntityCount = $Evidence.selection.unsupportedEntityCount
        $complete = $Evidence.selection.complete

        if ($parsedEntityCount + $unsupportedEntityCount -ne $entityCount) {
            $failures += [ValidationResult]::new('count_mismatch', 'selection', 'parsedEntityCount + unsupportedEntityCount must equal entityCount')
        }

        if ($complete -ne ($unsupportedEntityCount -eq 0)) {
            $failures += [ValidationResult]::new('complete_flag_mismatch', 'selection.complete', 'complete must be true when unsupportedEntityCount is 0')
        }
    }

    # Validate DBMOD
    if ($Evidence.dbmod) {
        if ($Evidence.dbmod.before -ne $Evidence.dbmod.after) {
            $failures += [ValidationResult]::new('dbmod_changed', 'dbmod', 'DBMOD must be unchanged')
        }

        if (-not $Evidence.dbmod.unchanged) {
            $failures += [ValidationResult]::new('dbmod_changed', 'dbmod.unchanged', 'DBMOD unchanged flag must be true')
        }
    }

    # Validate safety flags
    if ($Evidence.safety) {
        if (-not $Evidence.safety.noCadWrite) {
            $failures += [ValidationResult]::new('cad_write_observed', 'safety.noCadWrite', 'CAD write detected')
        }

        if (-not $Evidence.safety.noSaveCall) {
            $failures += [ValidationResult]::new('plugin_save_observed', 'safety.noSaveCall', 'Save call detected')
        }

        if (-not $Evidence.safety.noSavetimeModification) {
            $failures += [ValidationResult]::new('cad_write_observed', 'safety.noSavetimeModification', 'SAVETIME modification detected')
        }

        if (-not $Evidence.safety.noAutoLisp) {
            $failures += [ValidationResult]::new('prohibited_tool_behavior', 'safety.noAutoLisp', 'AutoLISP detected')
        }

        if (-not $Evidence.safety.noComAutomation) {
            $failures += [ValidationResult]::new('prohibited_tool_behavior', 'safety.noComAutomation', 'COM automation detected')
        }

        if (-not $Evidence.safety.noSendKeys) {
            $failures += [ValidationResult]::new('prohibited_tool_behavior', 'safety.noSendKeys', 'SendKeys detected')
        }

        if (-not $Evidence.safety.noScriptInjection) {
            $failures += [ValidationResult]::new('prohibited_tool_behavior', 'safety.noScriptInjection', 'Script injection detected')
        }

        if (-not $Evidence.safety.noNetload) {
            $failures += [ValidationResult]::new('prohibited_tool_behavior', 'safety.noNetload', 'NETLOAD detected')
        }

        if (-not $Evidence.safety.noRegistryModification) {
            $failures += [ValidationResult]::new('prohibited_tool_behavior', 'safety.noRegistryModification', 'Registry modification detected')
        }
    }

    return $failures
}

function Test-SensitiveFields {
    param([PSCustomObject] $Object, [string] $Prefix = '')

    $failures = @()

    foreach ($prop in $Object.PSObject.Properties) {
        $fieldName = if ($Prefix) { "$Prefix.$($prop.Name)" } else { $prop.Name }

        # Check for sensitive field names - only flag fields that should never appear in evidence
        $sensitiveFieldNames = @(
            'dwgPath', 'drawingPath', 'filePath', 'directoryPath',
            'userName', 'userAccount', 'windowsUser',
            'trustedPaths', 'secureLoad', 'trustedPathsConfig',
            'apiKey', 'api_key', 'apiSecret', 'secretKey',
            'accessToken', 'refreshToken', 'bearerToken',
            'password', 'credential', 'privateKey',
            'stackTrace', 'stack_trace', 'exceptionDetail',
            'errorMessage', 'errorDetail'
        )

        if ($prop.Name -in $sensitiveFieldNames) {
            $failures += [ValidationResult]::new('sensitive_field_rejected', $fieldName, "Sensitive field not allowed: $($prop.Name)")
        }

        # Check for sensitive values
        if ($prop.Value -is [string]) {
            if (Test-SensitiveValue -Value $prop.Value) {
                $failures += [ValidationResult]::new('sensitive_value_rejected', $fieldName, "Sensitive value detected in field: $($prop.Name)")
            }
        }

        # Recurse into nested objects
        if ($prop.Value -is [PSCustomObject]) {
            $failures += Test-SensitiveFields -Object $prop.Value -Prefix $fieldName
        }
    }

    return $failures
}

function Test-UnknownFields {
    param([PSCustomObject] $Object, [string[]] $KnownFields, [string] $Prefix = '')

    $failures = @()

    foreach ($prop in $Object.PSObject.Properties) {
        $fieldName = if ($Prefix) { "$Prefix.$($prop.Name)" } else { $prop.Name }

        if ($fieldName -notin $KnownFields) {
            $failures += [ValidationResult]::new('unknown_field', $fieldName, "Unknown field: $fieldName")
        }

        if ($prop.Value -is [PSCustomObject]) {
            $failures += Test-UnknownFields -Object $prop.Value -KnownFields $KnownFields -Prefix $fieldName
        }
    }

    return $failures
}

function Test-DuplicateFields {
    param([string] $Path)

    $content = Get-Content -LiteralPath $Path -Raw -Encoding UTF8
    $lines = $content -split "`n"
    $fieldCounts = @{}

    foreach ($line in $lines) {
        if ($line -match '"([^"]+)"\s*:') {
            $fieldName = $Matches[1]
            if ($fieldCounts.ContainsKey($fieldName)) {
                $fieldCounts[$fieldName]++
            }
            else {
                $fieldCounts[$fieldName] = 1
            }
        }
    }

    $failures = @()
    foreach ($entry in $fieldCounts.GetEnumerator()) {
        if ($entry.Value -gt 1) {
            $failures += [ValidationResult]::new('duplicate_field', $entry.Key, "Duplicate field: $($entry.Key)")
        }
    }

    return $failures
}

# Main validation
try {
    $failures = @()

    # Validate manifest file exists
    if (-not (Test-Path -LiteralPath $ManifestPath -PathType Leaf)) {
        $failures += [ValidationResult]::new('manifest_invalid', 'manifest', "Manifest file not found: $ManifestPath")
    }

    # Validate evidence file exists
    if (-not (Test-Path -LiteralPath $EvidencePath -PathType Leaf)) {
        $failures += [ValidationResult]::new('evidence_invalid', 'evidence', "Evidence file not found: $EvidencePath")
    }

    if ($failures.Count -gt 0) {
        $failures | ForEach-Object { Write-Error "$($_.Code): $($_.Path) - $($_.Message)" }
        exit 1
    }

    # Validate UTF-8 no BOM
    if (-not (Test-StrictUtf8NoBom -Path $ManifestPath)) {
        $failures += [ValidationResult]::new('manifest_invalid', 'manifest', 'Manifest must be UTF-8 without BOM')
    }

    if (-not (Test-StrictUtf8NoBom -Path $EvidencePath)) {
        $failures += [ValidationResult]::new('evidence_invalid', 'evidence', 'Evidence must be UTF-8 without BOM')
    }

    # Validate JSON
    if (-not (Test-ValidJson -Path $ManifestPath)) {
        $failures += [ValidationResult]::new('manifest_invalid', 'manifest', 'Manifest contains invalid JSON')
    }

    if (-not (Test-ValidJson -Path $EvidencePath)) {
        $failures += [ValidationResult]::new('evidence_invalid', 'evidence', 'Evidence contains invalid JSON')
    }

    if ($failures.Count -gt 0) {
        $failures | ForEach-Object { Write-Error "$($_.Code): $($_.Path) - $($_.Message)" }
        exit 1
    }

    # Load JSON
    $manifest = Get-Content -LiteralPath $ManifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $evidence = Get-Content -LiteralPath $EvidencePath -Raw -Encoding UTF8 | ConvertFrom-Json

    # Validate manifest
    $failures += Validate-Manifest -Manifest $manifest

    # Validate evidence
    $failures += Validate-Evidence -Evidence $evidence -Manifest $manifest

    # Check for unknown fields
    $failures += Test-UnknownFields -Object $manifest -KnownFields $ManifestFields
    $failures += Test-UnknownFields -Object $evidence -KnownFields $EvidenceFields

    # Check for duplicate fields
    $failures += Test-DuplicateFields -Path $ManifestPath
    $failures += Test-DuplicateFields -Path $EvidencePath

    # Check for sensitive fields
    $failures += Test-SensitiveFields -Object $manifest
    $failures += Test-SensitiveFields -Object $evidence

    # Output results
    if ($failures.Count -gt 0) {
        foreach ($failure in $failures) {
            Write-Error "$($failure.Code): $($failure.Path) - $($failure.Message)"
        }
        exit 1
    }
    else {
        Write-Host "Validation passed." -ForegroundColor Green
        exit 0
    }
}
catch {
    Write-Error "Validation failed with exception: $_"
    exit 1
}
