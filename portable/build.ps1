# Construit le PACKAGE PORTABLE de MAAT : exécutable autonome + marqueur de mode
# portable (préférences stockées À CÔTÉ de l'exe, rien dans %APPDATA%), licence et
# notice, le tout dans une archive ZIP. Aucune installation requise côté utilisateur.
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$version = '1.1.0'
$name    = "MAAT-$version-portable-x64"
$stage   = Join-Path 'build' $name
$zip     = "$name.zip"
$repo    = (Resolve-Path '..').Path
$exe     = Join-Path $repo 'publish\win-x64\MAAT.App.exe'

Write-Host '1/3  Publication de l''exécutable autonome (self-contained, single-file)…'
dotnet publish (Join-Path $repo 'src\MAAT.App\MAAT.App.csproj') -p:PublishProfile=win-x64-selfcontained | Out-Null
if (-not (Test-Path $exe)) { throw "Publication introuvable : $exe" }

Write-Host '2/3  Mise en scène des fichiers…'
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force $stage | Out-Null
Copy-Item $exe (Join-Path $stage 'MAAT.exe') -Force
Copy-Item (Join-Path $repo 'LICENSE') (Join-Path $stage 'LICENSE.txt') -Force

# Marqueur de mode portable (sa seule présence bascule le stockage en local).
'Présence de ce fichier = mode portable : préférences enregistrées dans ce dossier (settings.json).' |
    Set-Content -Path (Join-Path $stage 'MAAT.portable') -Encoding UTF8

@"
================================  ENGLISH  ================================

MAAT $version — Portable edition (Windows 10/11 x64)

No installation: double-click MAAT.exe.

• Self-contained application (.NET bundled): nothing else to install.
• Portable mode: thanks to the "MAAT.portable" file in this folder, your
  preferences (theme, language, panel widths) are saved HERE in
  "settings.json", not in your Windows profile. No trace is left on the host
  machine: delete the folder and nothing remains.
  (Remove "MAAT.portable" to fall back to standard per-user storage.)
• Unsaved audits use a temporary database wiped on exit (confidentiality).

License: GPL-3.0 (see LICENSE.txt).
Source code: https://github.com/pierrenicolasmartin/MAAT


===============================  FRANÇAIS  ===============================

MAAT $version — version portable (Windows 10/11 x64)

Aucune installation : double-cliquez MAAT.exe.

• Application autonome (.NET embarqué) : rien d'autre à installer.
• Mode portable : grâce au fichier « MAAT.portable » de ce dossier, vos
  préférences (thème, langue, largeurs) sont enregistrées ICI dans
  « settings.json », et non dans votre profil Windows. Aucune trace n'est
  laissée sur la machine hôte : supprimez le dossier et il ne reste rien.
  (Retirez « MAAT.portable » pour revenir au stockage utilisateur classique.)
• Les audits non enregistrés utilisent une base temporaire effacée à la
  fermeture (confidentialité).

Licence : GPL-3.0 (voir LICENSE.txt).
Code source : https://github.com/pierrenicolasmartin/MAAT
"@ | Set-Content -Path (Join-Path $stage 'README.txt') -Encoding UTF8

Write-Host '3/3  Création de l''archive ZIP…'
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path $stage -DestinationPath $zip

$mb = (Get-Item $zip).Length / 1MB
Write-Host ("OK  -> {0} ({1:N1} Mo)" -f $zip, $mb)
