@{
    Severity = @(
        'Error'
        'Warning'
    )

    IncludeDefaultRules = $false

    IncludeRules = @(
        'PSAvoidAssignmentToAutomaticVariable'
        'PSAvoidDefaultValueForMandatoryParameter'
        'PSAvoidDefaultValueSwitchParameter'
        'PSAvoidExclaimOperator'
        'PSAvoidGlobalAliases'
        'PSAvoidUsingCmdletAliases'
        'PSAvoidUsingConvertToSecureStringWithPlainText'
        'PSAvoidUsingEmptyCatchBlock'
        'PSAvoidUsingInvokeExpression'
        'PSAvoidUsingPlainTextForPassword'
        'PSReviewUnusedParameter'
        'PSUseCompatibleSyntax'
        'PSUseDeclaredVarsMoreThanAssignments'
    )

    # Okno PowerShell files are repo-owned scripts and internal harness helpers, not
    # exported PowerShell modules. Cmdlet naming rules are intentionally disabled
    # because they produce misleading guidance for private orchestration helpers
    # such as Try-Resolve*, Validate-* and plural inventory-returning functions.
    # Write-Host is allowed for human-facing CLI/installer output, while MCP
    # transport scripts keep their own stdout/stderr contract in code review.
    # The repository stores scripts as UTF-8 without BOM. Formatting-only PSSA
    # rules, including casing-only Information diagnostics and trailing
    # semicolon formatting, are disabled because the project has no canonical
    # PowerShell formatter and many smoke/contract assertions intentionally
    # keep long literals together for reviewability. Layout and command casing
    # remain code-review owned.
    ExcludeRules = @()

    Rules = @{
        PSUseCompatibleSyntax = @{
            Enable = $true
            TargetVersions = @(
                '5.1'
                '7.0'
            )
        }

        PSAvoidAssignmentToAutomaticVariable = @{
            Enable = $true
        }

        PSAvoidDefaultValueForMandatoryParameter = @{
            Enable = $true
        }

        PSAvoidDefaultValueSwitchParameter = @{
            Enable = $true
        }

        PSAvoidExclaimOperator = @{
            Enable = $true
        }

        PSAvoidGlobalAliases = @{
            Enable = $true
        }

        PSAvoidUsingCmdletAliases = @{
            AllowList = @()
        }

        PSAvoidUsingConvertToSecureStringWithPlainText = @{
            Enable = $true
        }

        PSAvoidUsingEmptyCatchBlock = @{
            Enable = $true
        }

        PSAvoidUsingInvokeExpression = @{
            Enable = $true
        }

        PSAvoidUsingPlainTextForPassword = @{
            Enable = $true
        }

        PSReviewUnusedParameter = @{
            Enable = $true
        }

        PSUseDeclaredVarsMoreThanAssignments = @{
            Enable = $true
        }
    }
}
