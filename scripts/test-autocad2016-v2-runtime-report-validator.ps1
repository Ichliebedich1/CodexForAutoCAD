[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$validatorPath = Join-Path $repoRoot 'scripts\verify-autocad2016-v2-runtime-report.ps1'
$templateDir = Join-Path $repoRoot 'handoff\autocad2016\evidence'
$testDir = Join-Path $repoRoot 'artifacts\v2-runtime-report-test'

# Test counters
$testCount = 0
$passCount = 0
$failCount = 0

function Write-TestResult {
    param(
        [string] $TestName,
        [bool] $Passed,
        [string] $Detail = ''
    )

    $script:testCount++
    if ($Passed) {
        $script:passCount++
        Write-Host "PASS $TestName" -ForegroundColor Green
    }
    else {
        $script:failCount++
        Write-Host "FAIL $TestName" -ForegroundColor Red
        if ($Detail) {
            Write-Host "  $Detail" -ForegroundColor Yellow
        }
    }
}

function New-TestManifest {
    param([hashtable] $Override = @{})

    $manifest = @{
        '$schema' = 'cad-context-v2-runtime-candidate-manifest'
        schemaVersion = 1
        candidateId = 'test-candidate-001'
        commit = '0ceb1234567890abcdef1234567890abcdef1234'
        hostDllSha256 = '9c27f4a71e4dfaec393b53ab15a657fa37ca9f8a7b09e0522894ab3b354603bb'
        moduleVersion = '1.0.0'
        schema = 'codex.autocad.cad-context'
        schemaVersion_target = 2
        status = ''
        recordedAtUtc = '2026-07-21T00:00:00Z'
        testMatrix = @{
            sampleA = @{
                line = $true
                circle = $true
                polyline = $true
                dbText = $true
                mText = $true
                blockReference = $true
            }
            sampleB = @{
                arc = $true
                ellipse = $true
                spline = $true
                point = $true
                ray = $true
                xline = $true
            }
            sampleC = @{
                polyline2d = $true
                polyline3d = $true
                dimension = $true
            }
            sampleD = @{
                hatch = $true
                leader = $true
                mLeader = $true
                table = $true
            }
            sampleE = @{
                unknownEntityType = $true
                entityReadFailed = $true
                entityDataLimit = $true
            }
        }
        coverage = @{
            strongTypeCount = 19
            placeholderReasonCount = 3
            mixedSelectionTested = $true
            dbmodUnchanged = $true
            documentSwitchClearsContext = $true
            noCadWrite = $true
            noSaveCall = $true
            noSavetimeModification = $true
        }
    }

    foreach ($key in $Override.Keys) {
        $manifest[$key] = $Override[$key]
    }

    return $manifest
}

function New-TestEvidence {
    param([hashtable] $Override = @{})

    $evidence = @{
        '$schema' = 'cad-context-v2-runtime-evidence'
        schemaVersion = 1
        candidateId = 'test-candidate-001'
        commit = '0ceb1234567890abcdef1234567890abcdef1234'
        hostDllSha256 = '9c27f4a71e4dfaec393b53ab15a657fa37ca9f8a7b09e0522894ab3b354603bb'
        moduleVersion = '1.0.0'
        schema = 'codex.autocad.cad-context'
        schemaVersion_target = 2
        status = ''
        recordedAtUtc = '2026-07-21T00:00:00Z'
        testId = 'test-001'
        testType = 'manual'
        entityTypes = @('line', 'circle', 'polyline', 'dbText', 'mText', 'blockReference',
            'arc', 'ellipse', 'spline', 'point', 'ray', 'xline',
            'polyline2d', 'polyline3d', 'dimension', 'hatch', 'leader', 'mLeader', 'table')
        placeholderReasons = @('unknown-entity-type', 'entity-read-failed', 'entity-data-limit')
        selection = @{
            entityCount = 5
            parsedEntityCount = 4
            unsupportedEntityCount = 1
            complete = $false
        }
        dbmod = @{
            before = 0
            after = 0
            unchanged = $true
        }
        documentSwitch = @{
            tested = $true
            contextCleared = $true
        }
        safety = @{
            noCadWrite = $true
            noSaveCall = $true
            noSavetimeModification = $true
            noAutoLisp = $true
            noComAutomation = $true
            noSendKeys = $true
            noScriptInjection = $true
            noNetload = $true
            noRegistryModification = $true
        }
        notes = ''
    }

    foreach ($key in $Override.Keys) {
        $evidence[$key] = $Override[$key]
    }

    return $evidence
}

function Save-TestFile {
    param(
        [hashtable] $Data,
        [string] $FileName
    )

    $path = Join-Path $testDir $FileName
    $json = $Data | ConvertTo-Json -Depth 10
    [System.IO.File]::WriteAllText($path, $json, [System.Text.UTF8Encoding]::new($false))
    return $path
}

function Invoke-Validator {
    param(
        [string] $ManifestPath,
        [string] $EvidencePath
    )

    try {
        $output = & $validatorPath -ManifestPath $ManifestPath -EvidencePath $EvidencePath 2>&1
        $exitCode = $LASTEXITCODE
        return @{
            ExitCode = $exitCode
            Output = $output
        }
    }
    catch {
        return @{
            ExitCode = 1
            Output = @($_.Exception.Message)
        }
    }
}

# Create test directory
if (Test-Path -LiteralPath $testDir) {
    Remove-Item -LiteralPath $testDir -Recurse -Force
}
New-Item -ItemType Directory -Path $testDir -Force | Out-Null

# Test 1: Valid manifest and evidence
Write-Host "`n=== Test 1: Valid manifest and evidence ===" -ForegroundColor Cyan
$manifest = New-TestManifest
$evidence = New-TestEvidence
$manifestPath = Save-TestFile -Data $manifest -FileName 'valid-manifest.json'
$evidencePath = Save-TestFile -Data $evidence -FileName 'valid-evidence.json'
$result = Invoke-Validator -ManifestPath $manifestPath -EvidencePath $evidencePath
Write-TestResult -TestName 'Valid manifest and evidence' -Passed ($result.ExitCode -eq 0)

# Test 2: Invalid commit SHA (too short)
Write-Host "`n=== Test 2: Invalid commit SHA ===" -ForegroundColor Cyan
$manifest = New-TestManifest -Override @{ commit = '0ceb123' }
$evidence = New-TestEvidence -Override @{ commit = '0ceb123' }
$manifestPath = Save-TestFile -Data $manifest -FileName 'invalid-commit-manifest.json'
$evidencePath = Save-TestFile -Data $evidence -FileName 'invalid-commit-evidence.json'
$result = Invoke-Validator -ManifestPath $manifestPath -EvidencePath $evidencePath
Write-TestResult -TestName 'Invalid commit SHA (too short)' -Passed ($result.ExitCode -ne 0)

# Test 3: Invalid artifact SHA (too short)
Write-Host "`n=== Test 3: Invalid artifact SHA ===" -ForegroundColor Cyan
$manifest = New-TestManifest -Override @{ hostDllSha256 = '9c27f4a71e' }
$evidence = New-TestEvidence -Override @{ hostDllSha256 = '9c27f4a71e' }
$manifestPath = Save-TestFile -Data $manifest -FileName 'invalid-sha-manifest.json'
$evidencePath = Save-TestFile -Data $evidence -FileName 'invalid-sha-evidence.json'
$result = Invoke-Validator -ManifestPath $manifestPath -EvidencePath $evidencePath
Write-TestResult -TestName 'Invalid artifact SHA (too short)' -Passed ($result.ExitCode -ne 0)

# Test 4: Candidate identity mismatch
Write-Host "`n=== Test 4: Candidate identity mismatch ===" -ForegroundColor Cyan
$manifest = New-TestManifest
$evidence = New-TestEvidence -Override @{ candidateId = 'different-candidate' }
$manifestPath = Save-TestFile -Data $manifest -FileName 'mismatch-manifest.json'
$evidencePath = Save-TestFile -Data $evidence -FileName 'mismatch-evidence.json'
$result = Invoke-Validator -ManifestPath $manifestPath -EvidencePath $evidencePath
Write-TestResult -TestName 'Candidate identity mismatch' -Passed ($result.ExitCode -ne 0)

# Test 5: Schema mismatch
Write-Host "`n=== Test 5: Schema mismatch ===" -ForegroundColor Cyan
$manifest = New-TestManifest -Override @{ schema = 'wrong-schema' }
$evidence = New-TestEvidence -Override @{ schema = 'wrong-schema' }
$manifestPath = Save-TestFile -Data $manifest -FileName 'schema-mismatch-manifest.json'
$evidencePath = Save-TestFile -Data $evidence -FileName 'schema-mismatch-evidence.json'
$result = Invoke-Validator -ManifestPath $manifestPath -EvidencePath $evidencePath
Write-TestResult -TestName 'Schema mismatch' -Passed ($result.ExitCode -ne 0)

# Test 6: Coverage incomplete
Write-Host "`n=== Test 6: Coverage incomplete ===" -ForegroundColor Cyan
$manifest = New-TestManifest
$manifest.coverage.strongTypeCount = 10
$evidence = New-TestEvidence
$manifestPath = Save-TestFile -Data $manifest -FileName 'coverage-incomplete-manifest.json'
$evidencePath = Save-TestFile -Data $evidence -FileName 'coverage-incomplete-evidence.json'
$result = Invoke-Validator -ManifestPath $manifestPath -EvidencePath $evidencePath
Write-TestResult -TestName 'Coverage incomplete (strongTypeCount=10)' -Passed ($result.ExitCode -ne 0)

# Test 7: Count mismatch
Write-Host "`n=== Test 7: Count mismatch ===" -ForegroundColor Cyan
$manifest = New-TestManifest
$evidence = New-TestEvidence
$evidence.selection.entityCount = 10
$evidence.selection.parsedEntityCount = 5
$evidence.selection.unsupportedEntityCount = 3
$manifestPath = Save-TestFile -Data $manifest -FileName 'count-mismatch-manifest.json'
$evidencePath = Save-TestFile -Data $evidence -FileName 'count-mismatch-evidence.json'
$result = Invoke-Validator -ManifestPath $manifestPath -EvidencePath $evidencePath
Write-TestResult -TestName 'Count mismatch' -Passed ($result.ExitCode -ne 0)

# Test 8: Complete flag mismatch
Write-Host "`n=== Test 8: Complete flag mismatch ===" -ForegroundColor Cyan
$manifest = New-TestManifest
$evidence = New-TestEvidence
$evidence.selection.complete = $true
$evidence.selection.unsupportedEntityCount = 1
$manifestPath = Save-TestFile -Data $manifest -FileName 'complete-mismatch-manifest.json'
$evidencePath = Save-TestFile -Data $evidence -FileName 'complete-mismatch-evidence.json'
$result = Invoke-Validator -ManifestPath $manifestPath -EvidencePath $evidencePath
Write-TestResult -TestName 'Complete flag mismatch' -Passed ($result.ExitCode -ne 0)

# Test 9: DBMOD changed
Write-Host "`n=== Test 9: DBMOD changed ===" -ForegroundColor Cyan
$manifest = New-TestManifest
$evidence = New-TestEvidence
$evidence.dbmod.before = 0
$evidence.dbmod.after = 1
$evidence.dbmod.unchanged = $false
$manifestPath = Save-TestFile -Data $manifest -FileName 'dbmod-changed-manifest.json'
$evidencePath = Save-TestFile -Data $evidence -FileName 'dbmod-changed-evidence.json'
$result = Invoke-Validator -ManifestPath $manifestPath -EvidencePath $evidencePath
Write-TestResult -TestName 'DBMOD changed' -Passed ($result.ExitCode -ne 0)

# Test 10: Save observed
Write-Host "`n=== Test 10: Save observed ===" -ForegroundColor Cyan
$manifest = New-TestManifest
$evidence = New-TestEvidence
$evidence.safety.noSaveCall = $false
$manifestPath = Save-TestFile -Data $manifest -FileName 'save-observed-manifest.json'
$evidencePath = Save-TestFile -Data $evidence -FileName 'save-observed-evidence.json'
$result = Invoke-Validator -ManifestPath $manifestPath -EvidencePath $evidencePath
Write-TestResult -TestName 'Save observed' -Passed ($result.ExitCode -ne 0)

# Test 11: CAD write observed
Write-Host "`n=== Test 11: CAD write observed ===" -ForegroundColor Cyan
$manifest = New-TestManifest
$evidence = New-TestEvidence
$evidence.safety.noCadWrite = $false
$manifestPath = Save-TestFile -Data $manifest -FileName 'cad-write-manifest.json'
$evidencePath = Save-TestFile -Data $evidence -FileName 'cad-write-evidence.json'
$result = Invoke-Validator -ManifestPath $manifestPath -EvidencePath $evidencePath
Write-TestResult -TestName 'CAD write observed' -Passed ($result.ExitCode -ne 0)

# Test 12: Sensitive value - path
Write-Host "`n=== Test 12: Sensitive value - path ===" -ForegroundColor Cyan
$manifest = New-TestManifest
$evidence = New-TestEvidence -Override @{ notes = 'C:\Users\Administrator\test.dwg' }
$manifestPath = Save-TestFile -Data $manifest -FileName 'sensitive-path-manifest.json'
$evidencePath = Save-TestFile -Data $evidence -FileName 'sensitive-path-evidence.json'
$result = Invoke-Validator -ManifestPath $manifestPath -EvidencePath $evidencePath
Write-TestResult -TestName 'Sensitive value - path' -Passed ($result.ExitCode -ne 0)

# Test 13: Sensitive value - API Key
Write-Host "`n=== Test 13: Sensitive value - API Key ===" -ForegroundColor Cyan
$manifest = New-TestManifest
$evidence = New-TestEvidence -Override @{ notes = 'api_key=sk-1234567890abcdef' }
$manifestPath = Save-TestFile -Data $manifest -FileName 'sensitive-apikey-manifest.json'
$evidencePath = Save-TestFile -Data $evidence -FileName 'sensitive-apikey-evidence.json'
$result = Invoke-Validator -ManifestPath $manifestPath -EvidencePath $evidencePath
Write-TestResult -TestName 'Sensitive value - API Key' -Passed ($result.ExitCode -ne 0)

# Test 14: Sensitive value - stack trace
Write-Host "`n=== Test 14: Sensitive value - stack trace ===" -ForegroundColor Cyan
$manifest = New-TestManifest
$evidence = New-TestEvidence -Override @{ notes = 'at System.IO.File.Read(String path)' }
$manifestPath = Save-TestFile -Data $manifest -FileName 'sensitive-stacktrace-manifest.json'
$evidencePath = Save-TestFile -Data $evidence -FileName 'sensitive-stacktrace-evidence.json'
$result = Invoke-Validator -ManifestPath $manifestPath -EvidencePath $evidencePath
Write-TestResult -TestName 'Sensitive value - stack trace' -Passed ($result.ExitCode -ne 0)

# Test 15: Unknown field
Write-Host "`n=== Test 15: Unknown field ===" -ForegroundColor Cyan
$manifest = New-TestManifest
$manifest['unknownField'] = 'value'
$evidence = New-TestEvidence
$manifestPath = Save-TestFile -Data $manifest -FileName 'unknown-field-manifest.json'
$evidencePath = Save-TestFile -Data $evidence -FileName 'unknown-field-evidence.json'
$result = Invoke-Validator -ManifestPath $manifestPath -EvidencePath $evidencePath
Write-TestResult -TestName 'Unknown field' -Passed ($result.ExitCode -ne 0)

# Test 16: Missing required field
Write-Host "`n=== Test 16: Missing required field ===" -ForegroundColor Cyan
$manifest = New-TestManifest
$manifest.Remove('candidateId')
$evidence = New-TestEvidence
$manifestPath = Save-TestFile -Data $manifest -FileName 'missing-field-manifest.json'
$evidencePath = Save-TestFile -Data $evidence -FileName 'missing-field-evidence.json'
$result = Invoke-Validator -ManifestPath $manifestPath -EvidencePath $evidencePath
Write-TestResult -TestName 'Missing required field' -Passed ($result.ExitCode -ne 0)

# Test 17: Test matrix incomplete
Write-Host "`n=== Test 17: Test matrix incomplete ===" -ForegroundColor Cyan
$manifest = New-TestManifest
$manifest.testMatrix.sampleA.line = $false
$evidence = New-TestEvidence
$manifestPath = Save-TestFile -Data $manifest -FileName 'matrix-incomplete-manifest.json'
$evidencePath = Save-TestFile -Data $evidence -FileName 'matrix-incomplete-evidence.json'
$result = Invoke-Validator -ManifestPath $manifestPath -EvidencePath $evidencePath
Write-TestResult -TestName 'Test matrix incomplete' -Passed ($result.ExitCode -ne 0)

# Test 18: Document switch not tested
Write-Host "`n=== Test 18: Document switch not tested ===" -ForegroundColor Cyan
$manifest = New-TestManifest
$manifest.coverage.documentSwitchClearsContext = $false
$evidence = New-TestEvidence
$manifestPath = Save-TestFile -Data $manifest -FileName 'docswitch-manifest.json'
$evidencePath = Save-TestFile -Data $evidence -FileName 'docswitch-evidence.json'
$result = Invoke-Validator -ManifestPath $manifestPath -EvidencePath $evidencePath
Write-TestResult -TestName 'Document switch not tested' -Passed ($result.ExitCode -ne 0)

# Test 19: SAVETIME modification
Write-Host "`n=== Test 19: SAVETIME modification ===" -ForegroundColor Cyan
$manifest = New-TestManifest
$evidence = New-TestEvidence
$evidence.safety.noSavetimeModification = $false
$manifestPath = Save-TestFile -Data $manifest -FileName 'savetime-manifest.json'
$evidencePath = Save-TestFile -Data $evidence -FileName 'savetime-evidence.json'
$result = Invoke-Validator -ManifestPath $manifestPath -EvidencePath $evidencePath
Write-TestResult -TestName 'SAVETIME modification' -Passed ($result.ExitCode -ne 0)

# Test 20: AutoLISP detected
Write-Host "`n=== Test 20: AutoLISP detected ===" -ForegroundColor Cyan
$manifest = New-TestManifest
$evidence = New-TestEvidence
$evidence.safety.noAutoLisp = $false
$manifestPath = Save-TestFile -Data $manifest -FileName 'autolisp-manifest.json'
$evidencePath = Save-TestFile -Data $evidence -FileName 'autolisp-evidence.json'
$result = Invoke-Validator -ManifestPath $manifestPath -EvidencePath $evidencePath
Write-TestResult -TestName 'AutoLISP detected' -Passed ($result.ExitCode -ne 0)

# Test 21: COM automation detected
Write-Host "`n=== Test 21: COM automation detected ===" -ForegroundColor Cyan
$manifest = New-TestManifest
$evidence = New-TestEvidence
$evidence.safety.noComAutomation = $false
$manifestPath = Save-TestFile -Data $manifest -FileName 'com-manifest.json'
$evidencePath = Save-TestFile -Data $evidence -FileName 'com-evidence.json'
$result = Invoke-Validator -ManifestPath $manifestPath -EvidencePath $evidencePath
Write-TestResult -TestName 'COM automation detected' -Passed ($result.ExitCode -ne 0)

# Test 22: SendKeys detected
Write-Host "`n=== Test 22: SendKeys detected ===" -ForegroundColor Cyan
$manifest = New-TestManifest
$evidence = New-TestEvidence
$evidence.safety.noSendKeys = $false
$manifestPath = Save-TestFile -Data $manifest -FileName 'sendkeys-manifest.json'
$evidencePath = Save-TestFile -Data $evidence -FileName 'sendkeys-evidence.json'
$result = Invoke-Validator -ManifestPath $manifestPath -EvidencePath $evidencePath
Write-TestResult -TestName 'SendKeys detected' -Passed ($result.ExitCode -ne 0)

# Test 23: Script injection detected
Write-Host "`n=== Test 23: Script injection detected ===" -ForegroundColor Cyan
$manifest = New-TestManifest
$evidence = New-TestEvidence
$evidence.safety.noScriptInjection = $false
$manifestPath = Save-TestFile -Data $manifest -FileName 'script-manifest.json'
$evidencePath = Save-TestFile -Data $evidence -FileName 'script-evidence.json'
$result = Invoke-Validator -ManifestPath $manifestPath -EvidencePath $evidencePath
Write-TestResult -TestName 'Script injection detected' -Passed ($result.ExitCode -ne 0)

# Test 24: NETLOAD detected
Write-Host "`n=== Test 24: NETLOAD detected ===" -ForegroundColor Cyan
$manifest = New-TestManifest
$evidence = New-TestEvidence
$evidence.safety.noNetload = $false
$manifestPath = Save-TestFile -Data $manifest -FileName 'netload-manifest.json'
$evidencePath = Save-TestFile -Data $evidence -FileName 'netload-evidence.json'
$result = Invoke-Validator -ManifestPath $manifestPath -EvidencePath $evidencePath
Write-TestResult -TestName 'NETLOAD detected' -Passed ($result.ExitCode -ne 0)

# Test 25: Registry modification detected
Write-Host "`n=== Test 25: Registry modification detected ===" -ForegroundColor Cyan
$manifest = New-TestManifest
$evidence = New-TestEvidence
$evidence.safety.noRegistryModification = $false
$manifestPath = Save-TestFile -Data $manifest -FileName 'registry-manifest.json'
$evidencePath = Save-TestFile -Data $evidence -FileName 'registry-evidence.json'
$result = Invoke-Validator -ManifestPath $manifestPath -EvidencePath $evidencePath
Write-TestResult -TestName 'Registry modification detected' -Passed ($result.ExitCode -ne 0)

# Test 26: Invalid entity type
Write-Host "`n=== Test 26: Invalid entity type ===" -ForegroundColor Cyan
$manifest = New-TestManifest
$evidence = New-TestEvidence
$evidence.entityTypes = @('line', 'invalidType')
$manifestPath = Save-TestFile -Data $manifest -FileName 'invalid-entity-manifest.json'
$evidencePath = Save-TestFile -Data $evidence -FileName 'invalid-entity-evidence.json'
$result = Invoke-Validator -ManifestPath $manifestPath -EvidencePath $evidencePath
Write-TestResult -TestName 'Invalid entity type' -Passed ($result.ExitCode -ne 0)

# Test 27: Invalid placeholder reason
Write-Host "`n=== Test 27: Invalid placeholder reason ===" -ForegroundColor Cyan
$manifest = New-TestManifest
$evidence = New-TestEvidence
$evidence.placeholderReasons = @('unknown-entity-type', 'invalid-reason')
$manifestPath = Save-TestFile -Data $manifest -FileName 'invalid-reason-manifest.json'
$evidencePath = Save-TestFile -Data $evidence -FileName 'invalid-reason-evidence.json'
$result = Invoke-Validator -ManifestPath $manifestPath -EvidencePath $evidencePath
Write-TestResult -TestName 'Invalid placeholder reason' -Passed ($result.ExitCode -ne 0)

# Test 28: Schema version mismatch
Write-Host "`n=== Test 28: Schema version mismatch ===" -ForegroundColor Cyan
$manifest = New-TestManifest -Override @{ schemaVersion_target = 1 }
$evidence = New-TestEvidence -Override @{ schemaVersion_target = 1 }
$manifestPath = Save-TestFile -Data $manifest -FileName 'schema-version-manifest.json'
$evidencePath = Save-TestFile -Data $evidence -FileName 'schema-version-evidence.json'
$result = Invoke-Validator -ManifestPath $manifestPath -EvidencePath $evidencePath
Write-TestResult -TestName 'Schema version mismatch' -Passed ($result.ExitCode -ne 0)

# Test 29: Mixed selection with all strong types
Write-Host "`n=== Test 29: Mixed selection with all strong types ===" -ForegroundColor Cyan
$manifest = New-TestManifest
$evidence = New-TestEvidence
$evidence.selection.entityCount = 20
$evidence.selection.parsedEntityCount = 19
$evidence.selection.unsupportedEntityCount = 1
$evidence.selection.complete = $false
$manifestPath = Save-TestFile -Data $manifest -FileName 'mixed-selection-manifest.json'
$evidencePath = Save-TestFile -Data $evidence -FileName 'mixed-selection-evidence.json'
$result = Invoke-Validator -ManifestPath $manifestPath -EvidencePath $evidencePath
Write-TestResult -TestName 'Mixed selection with all strong types' -Passed ($result.ExitCode -eq 0)

# Test 30: Complete selection (no unsupported)
Write-Host "`n=== Test 30: Complete selection ===" -ForegroundColor Cyan
$manifest = New-TestManifest
$evidence = New-TestEvidence
$evidence.selection.entityCount = 19
$evidence.selection.parsedEntityCount = 19
$evidence.selection.unsupportedEntityCount = 0
$evidence.selection.complete = $true
$manifestPath = Save-TestFile -Data $manifest -FileName 'complete-selection-manifest.json'
$evidencePath = Save-TestFile -Data $evidence -FileName 'complete-selection-evidence.json'
$result = Invoke-Validator -ManifestPath $manifestPath -EvidencePath $evidencePath
Write-TestResult -TestName 'Complete selection (no unsupported)' -Passed ($result.ExitCode -eq 0)

# Summary
Write-Host "`n=== Summary ===" -ForegroundColor Cyan
Write-Host "Total tests: $testCount" -ForegroundColor White
Write-Host "Passed: $passCount" -ForegroundColor Green
Write-Host "Failed: $failCount" -ForegroundColor $(if ($failCount -eq 0) { 'Green' } else { 'Red' })

# Cleanup
Remove-Item -LiteralPath $testDir -Recurse -Force -ErrorAction SilentlyContinue

if ($failCount -eq 0) {
    Write-Host "`nAll tests passed." -ForegroundColor Green
    exit 0
}
else {
    Write-Host "`nSome tests failed." -ForegroundColor Red
    exit 1
}
