# Construit l'installeur MSI MULTILINGUE de MAAT.
# Base = en-US (repli) ; transform fr-FR embarqué (LCID 1036). Windows Installer
# sélectionne automatiquement la langue selon le système, repli en anglais.
# Prérequis : publication self-contained faite (..\publish\win-x64\MAAT.App.exe)
#             et l'extension WiX UI (wix extension add -g WixToolset.UI.wixext).
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$enMsi  = "build\en\MAAT.msi"
$frMsi  = "build\fr\MAAT.msi"
$mst    = "build\fr-FR.mst"
$out    = "MAAT-1.1.0-x64.msi"

New-Item -ItemType Directory -Force build\en, build\fr | Out-Null

Write-Host "1/4  Build en-US (base)…"
wix build Package.wxs -arch x64 -culture en-US -loc Strings.en-US.wxl `
    -d ProductLang=1033 -d LicenseRtf=License.en-US.rtf `
    -ext WixToolset.UI.wixext -o $enMsi

Write-Host "2/4  Build fr-FR…"
wix build Package.wxs -arch x64 -culture fr-FR -loc Strings.fr-FR.wxl `
    -d ProductLang=1036 -d LicenseRtf=License.fr-FR.rtf `
    -ext WixToolset.UI.wixext -o $frMsi

Write-Host "3/4  Transform fr-FR…"
# -serr f : ignore le changement de page de code ; pas de validation de langue.
wix msi transform $enMsi $frMsi -out $mst -serr f | Out-Null

Write-Host "4/4  Embarque le transform + déclare les langues…"
Copy-Item $enMsi $out -Force
cscript //nologo embed-transform.vbs (Resolve-Path $out).Path (Resolve-Path $mst).Path 1036
if ($LASTEXITCODE -ne 0) { throw "Embedding du transform échoué." }

Write-Host "OK  -> $out (multilingue : en-US base + fr-FR)"
