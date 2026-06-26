param(
    [Parameter(Mandatory = $false)]
    $From = $PWD,
    [Parameter(Mandatory = $true)]
    $AssemblyName
)

Write-Output "`$From: '$From'"
Write-Output "`$AssemblyName: '$AssemblyName'"

try {
    $___prev = $ErrorActionPreference

    $ErrorActionPreference = 'Stop'

    if (-not (Test-Path $From)) {
        throw "Output directory '$From' does not exist."
    }
    $___modulePath = (Join-Path $From ($AssemblyName + '.dll'))
    if (-not (Test-Path $___modulePath)) {
        throw "Assembly '$___modulePath' does not exist."
    }

    if ((Get-ExecutionPolicy) -ne 'Bypass' -and (Get-ExecutionPolicy) -ne 'Unrestricted') {
        throw "ExecutionPolicy must be set to 'Bypass' or 'Unrestricted'."
    }

    Write-Output "Importing $___modulePath..."
    Import-Module $___modulePath -ErrorAction Stop # Requires ExecutionPolicy = Bypass or Unrestricted
    Write-Output "Adding 'VSDev' drive..."
    New-PSDrive -Name Dev -PSProvider 'VSDev' -Root '\'
}
finally {
    $ErrorActionPreference = $___prev

    Get-Variable -Name '___*' | Remove-Variable
}