param(
    [Parameter(Mandatory = $true)]
    [string[]]$Path,
    [string]$CertificatePath = $env:SUNTRACE_CERT_PATH,
    [string]$CertificatePassword = $env:SUNTRACE_CERT_PASSWORD,
    [string]$TimestampUrl = "http://timestamp.digicert.com",
    [switch]$VerifyOnly
)

$ErrorActionPreference = "Stop"

function Find-SignTool {
    $command = Get-Command "signtool.exe" -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    $kits = Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\bin"
    if (Test-Path $kits) {
        $candidate = Get-ChildItem -Path $kits -Filter "signtool.exe" -Recurse -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -match "\\x64\\signtool\.exe$" } |
            Sort-Object FullName -Descending |
            Select-Object -First 1
        if ($candidate) {
            return $candidate.FullName
        }
    }

    return $null
}

$signTool = Find-SignTool
if (-not $signTool) {
    throw "Не найден signtool.exe. Установите Windows SDK или добавьте signtool.exe в PATH."
}

if ($VerifyOnly) {
    foreach ($file in $Path) {
        if (-not (Test-Path $file)) {
            throw "Файл для проверки не найден: $file"
        }

        & $signTool verify /pa /v $file
        if ($LASTEXITCODE -ne 0) {
            throw "Подпись не прошла проверку: $file"
        }
    }

    Write-Host "Подпись проверена."
    exit 0
}

if ([string]::IsNullOrWhiteSpace($CertificatePath)) {
    Write-Host "Сертификат не задан. Файлы остаются неподписанными."
    Write-Host "Задайте SUNTRACE_CERT_PATH и SUNTRACE_CERT_PASSWORD или параметры -CertificatePath/-CertificatePassword."
    exit 0
}

if (-not (Test-Path $CertificatePath)) {
    throw "Файл сертификата не найден: $CertificatePath"
}

if ([string]::IsNullOrWhiteSpace($CertificatePassword)) {
    throw "Не задан пароль сертификата. Используйте SUNTRACE_CERT_PASSWORD или -CertificatePassword."
}

foreach ($file in $Path) {
    if (-not (Test-Path $file)) {
        throw "Файл для подписи не найден: $file"
    }

    & $signTool sign /fd SHA256 /td $TimestampUrl /tr $TimestampUrl /f $CertificatePath /p $CertificatePassword $file
    if ($LASTEXITCODE -ne 0) {
        throw "Не удалось подписать файл: $file"
    }
}

& $signTool verify /pa /v $Path
if ($LASTEXITCODE -ne 0) {
    throw "Подписанные файлы не прошли проверку."
}

Write-Host "Подпись выполнена и проверена."
