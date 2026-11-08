param(
    [switch]$SkipTests,
    [switch]$SkipInstaller,
    [string]$CertificatePath = $env:SUNTRACE_CERT_PATH,
    [string]$CertificatePassword = $env:SUNTRACE_CERT_PASSWORD,
    [string]$TimestampUrl = "http://timestamp.digicert.com"
)

$ErrorActionPreference = "Stop"
$root = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$release = Join-Path $root "release"
$artifacts = Join-Path $root "artifacts"
$solution = Join-Path $root "SunTrace.sln"
$modernInstaller = Join-Path $root "installer\SunTrace.iss"
$legacyInstaller = Join-Path $root "installer\SunTrace.Legacy.iss"
$modernTfm = "net10.0-windows10.0.17763.0"
$legacyTfm = "net6.0-windows"
$modernPublish = Join-Path $artifacts "publish-modern"
$legacyPublish = Join-Path $artifacts "publish-legacy"
$modernRelease = Join-Path $release "Windows10-11"
$legacyRelease = Join-Path $release "Windows7-8.1"
$modernSetupName = "SunTrace-Setup-Windows10-11-x64.exe"
$legacySetupName = "SunTrace-Setup-Windows7-8.1-x64.exe"
$modernSetup = Join-Path $modernRelease $modernSetupName
$legacySetup = Join-Path $legacyRelease $legacySetupName
$version = "1.0.0"

Set-Location $root

if (Test-Path $release) {
    Remove-Item -Path $release -Recurse -Force
}

if (Test-Path $artifacts) {
    Remove-Item -Path $artifacts -Recurse -Force
}

New-Item -ItemType Directory -Path $modernRelease -Force | Out-Null
New-Item -ItemType Directory -Path $legacyRelease -Force | Out-Null

Write-Host "1/8 Очистка и сборка обеих версий..."
& dotnet clean $solution --configuration Release --nologo
if ($LASTEXITCODE -ne 0) {
    throw "Очистка завершилась с ошибкой."
}

& dotnet build $solution --configuration Release --nologo
if ($LASTEXITCODE -ne 0) {
    throw "Сборка завершилась с ошибкой."
}

if (-not $SkipTests) {
    Write-Host "2/8 Тестирование net10 и net6..."
    & dotnet test $solution --configuration Release --no-build --nologo
    if ($LASTEXITCODE -ne 0) {
        throw "Тесты не пройдены."
    }
}
else {
    Write-Host "2/8 Тестирование пропущено по параметру -SkipTests."
}

Write-Host "3/8 Публикация Windows 10/11..."
& (Join-Path $PSScriptRoot "publish.ps1") -Configuration Release -TargetFramework $modernTfm -Runtime win-x64 -OutputDirectory $modernPublish
if ($LASTEXITCODE -ne 0) {
    throw "Публикация Windows 10/11 завершилась с ошибкой."
}

Write-Host "4/8 Публикация Windows 7/8.1..."
& (Join-Path $PSScriptRoot "publish.ps1") -Configuration Release -TargetFramework $legacyTfm -Runtime win-x64 -OutputDirectory $legacyPublish
if ($LASTEXITCODE -ne 0) {
    throw "Публикация Windows 7/8.1 завершилась с ошибкой."
}

Copy-Item -Path $modernPublish -Destination (Join-Path $modernRelease "SunTrace") -Recurse -Force
Copy-Item -Path $legacyPublish -Destination (Join-Path $legacyRelease "SunTrace") -Recurse -Force

if (-not $SkipInstaller) {
    Write-Host "5/8 Сборка двух установщиков Inno Setup..."
    $isccCandidates = @(
        (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"),
        (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"),
        (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe")
    )
    $iscc = $isccCandidates | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
    if (-not $iscc) {
        throw "Не найден ISCC.exe (Inno Setup 6). Установите Inno Setup 6 или используйте -SkipInstaller."
    }

    & $iscc $modernInstaller
    if ($LASTEXITCODE -ne 0) {
        throw "Inno Setup завершился с ошибкой для Windows 10/11."
    }

    & $iscc $legacyInstaller
    if ($LASTEXITCODE -ne 0) {
        throw "Inno Setup завершился с ошибкой для Windows 7/8.1."
    }
}
else {
    Write-Host "5/8 Сборка установщиков пропущена по параметру -SkipInstaller."
}

$modernSignatureStatus = "не подписан: сертификат издателя не предоставлен"
$legacySignatureStatus = "не подписан: сертификат издателя не предоставлен"

if (-not $SkipInstaller) {
    if (-not (Test-Path $modernSetup)) {
        throw "Установщик не найден: $modernSetup"
    }

    if (-not (Test-Path $legacySetup)) {
        throw "Установщик не найден: $legacySetup"
    }

    if (-not [string]::IsNullOrWhiteSpace($CertificatePath)) {
        Write-Host "6/8 Подписание обеих версий..."
        & (Join-Path $PSScriptRoot "sign.ps1") `
            -Path @((Join-Path $modernPublish "SunTrace.exe"), $modernSetup) `
            -CertificatePath $CertificatePath `
            -CertificatePassword $CertificatePassword `
            -TimestampUrl $TimestampUrl
        if ($LASTEXITCODE -ne 0) {
            throw "Подписание версии Windows 10/11 завершилось с ошибкой."
        }

        & (Join-Path $PSScriptRoot "sign.ps1") `
            -Path @((Join-Path $legacyPublish "SunTrace.exe"), $legacySetup) `
            -CertificatePath $CertificatePath `
            -CertificatePassword $CertificatePassword `
            -TimestampUrl $TimestampUrl
        if ($LASTEXITCODE -ne 0) {
            throw "Подписание версии Windows 7/8.1 завершилось с ошибкой."
        }

        & (Join-Path $PSScriptRoot "sign.ps1") -Path @((Join-Path $modernPublish "SunTrace.exe"), $modernSetup) -VerifyOnly
        if ($LASTEXITCODE -ne 0) {
            throw "Проверка подписи версии Windows 10/11 завершилась с ошибкой."
        }

        & (Join-Path $PSScriptRoot "sign.ps1") -Path @((Join-Path $legacyPublish "SunTrace.exe"), $legacySetup) -VerifyOnly
        if ($LASTEXITCODE -ne 0) {
            throw "Проверка подписи версии Windows 7/8.1 завершилась с ошибкой."
        }

        $modernSignatureStatus = "подписана сертификатом издателя и проверена"
        $legacySignatureStatus = "подписана сертификатом издателя и проверена"
    }
    else {
        Write-Host "6/8 Подписание пропущено: сертификат не задан."
    }
}
else {
    Write-Host "6/8 Подписание пропущено вместе с установщиками."
}

function Write-ReleaseReadme {
    param(
        [string]$TemplatePath,
        [string]$TargetPath,
        [string]$SignatureStatus,
        [string]$SetupName
    )

    $template = Get-Content -Path $TemplatePath -Raw -Encoding UTF8
    $content = $template.Replace("{{VERSION}}", $version)
    $content = $content.Replace("{{SIGNATURE_STATUS}}", $SignatureStatus)
    $content = $content.Replace("{{BUILT_AT}}", (Get-Date).ToString("yyyy-MM-dd HH:mm:ss"))
    $content = $content.Replace("{{SETUP_NAME}}", $SetupName)
    [System.IO.File]::WriteAllText($TargetPath, $content, [System.Text.UTF8Encoding]::new($true))
}

Write-Host "7/8 Формирование README..."
if (-not $SkipInstaller) {
    Write-ReleaseReadme -TemplatePath (Join-Path $root "installer\README.template.txt") -TargetPath (Join-Path $modernRelease "README.txt") -SignatureStatus $modernSignatureStatus -SetupName $modernSetupName
    Write-ReleaseReadme -TemplatePath (Join-Path $root "installer\README.legacy.template.txt") -TargetPath (Join-Path $legacyRelease "README.txt") -SignatureStatus $legacySignatureStatus -SetupName $legacySetupName
}

Write-Host "8/8 Вычисление SHA-256..."
if (-not $SkipInstaller) {
    $modernHash = (Get-FileHash -Path $modernSetup -Algorithm SHA256).Hash.ToLowerInvariant()
    $legacyHash = (Get-FileHash -Path $legacySetup -Algorithm SHA256).Hash.ToLowerInvariant()
    $modernHashLine = "$modernHash  $modernSetupName"
    $legacyHashLine = "$legacyHash  $legacySetupName"
    [System.IO.File]::WriteAllText((Join-Path $modernRelease "$modernSetupName.sha256"), $modernHashLine + "`n", [System.Text.UTF8Encoding]::new($false))
    [System.IO.File]::WriteAllText((Join-Path $legacyRelease "$legacySetupName.sha256"), $legacyHashLine + "`n", [System.Text.UTF8Encoding]::new($false))
    Write-Host ""
    Write-Host "Готово: $release"
    Write-Host "  $modernSetupName"
    Write-Host "  $legacySetupName"
    Write-Host "Подпись Windows 10/11: $modernSignatureStatus"
    Write-Host "Подпись Windows 7/8.1: $legacySignatureStatus"
    Write-Host "SHA-256 Windows 10/11: $modernHash"
    Write-Host "SHA-256 Windows 7/8.1: $legacyHash"
}
else {
    Write-Host ""
    Write-Host "Готово без установщиков: $release"
}
