# MAAT — Monitoring et Audit des Accès pour la Traçabilité

[![en](https://img.shields.io/badge/lang-en-lightgrey.svg)](README.md)
[![fr](https://img.shields.io/badge/lang-fr-2b6cb0.svg)](README.fr.md)

**MAAT** est un logiciel Windows d'audit de systèmes de fichiers. Il inventorie une
arborescence, lit les **droits NTFS (ACL)** de chaque élément, calcule les **tailles**
et résout les **groupes Active Directory** (appartenances imbriquées), puis restitue
le tout dans une interface interactive et des rapports exportables.

> Statut : version 1.1.0 — fonctionnellement complète.
> Licence : **GNU GPL v3**.

---

## Aperçu

| <img src="docs/screenshots/fr_main_window.png" width="430"><br>**Fenêtre principale** (thèmes clair et sombre) | <img src="docs/screenshots/fr_audit_config.png" width="430"><br>**Configuration** d'un nouvel audit |
|:--:|:--:|
| <img src="docs/screenshots/fr_audit_in_progress.png" width="430"><br>**Progression** en temps réel, avec estimation du temps restant | <img src="docs/screenshots/fr_audit_results_folders.png" width="430"><br>**Résultats — vue Dossiers** : arbre + détail des droits |
| <img src="docs/screenshots/fr_audit_results_identities.png" width="430"><br>**Résultats — vue Identités** : emplacements et membres | <img src="docs/screenshots/fr_audit_summary.png" width="430"><br>**Rapport d'analyse** de l'audit |
| <img src="docs/screenshots/fr_html_export.png" width="430"><br>**Rapport HTML interactif** (autonome) | <img src="docs/screenshots/fr_user_guide.png" width="430"><br>**Guide d'utilisation** intégré |

### Icônes

<p>
  <img src="docs/icons/icon_app.png" width="96" alt="Icône de l'application MAAT" align="top">&nbsp;&nbsp;&nbsp;&nbsp;
  <img src="docs/icons/icon_file.png" width="96" alt="Icône des fichiers projet .maat" align="top">
</p>

Icône de l'application · icône des fichiers projet `.maat` (toutes deux à la plume de Maât).

---

## Téléchargement

La dernière version est publiée dans les
[**Releases**](https://github.com/pierrenicolasmartin/MAAT/releases/latest). Deux formats sont
proposés — un installeur MSI et un package portable sans installation :

| Artefact | Description | VirusTotal |
|---|---|:--:|
| **`MAAT-1.1.0-x64.msi`** | Installeur (Windows x64, runtime .NET 8 inclus) | [![VirusTotal](https://img.shields.io/badge/VirusTotal-rapport-394eff?logo=virustotal&logoColor=white)](https://www.virustotal.com/gui/file/01a2196ce660e7a2db20905d73f913bc426b474e2e6ebe457b8aa1572e330c73) |
| **`MAAT-1.1.0-portable-x64.zip`** | Package portable (sans installation) | [![VirusTotal](https://img.shields.io/badge/VirusTotal-rapport-394eff?logo=virustotal&logoColor=white)](https://www.virustotal.com/gui/file/0e94dd116ba9f165100e381b68f2ebd98f2684ac41b45e1938c73b3f128a7755) |
| &nbsp;&nbsp;└ **`MAAT.exe`** | Exécutable portable (dans le ZIP) | [![VirusTotal](https://img.shields.io/badge/VirusTotal-rapport-394eff?logo=virustotal&logoColor=white)](https://www.virustotal.com/gui/file/f6a0297a770c7206b270bc0bfcce22edf36a5c93d4563e26920c77b8030e34a7) |

Intégrité — SHA-256 :

```
01a2196ce660e7a2db20905d73f913bc426b474e2e6ebe457b8aa1572e330c73  MAAT-1.1.0-x64.msi
0e94dd116ba9f165100e381b68f2ebd98f2684ac41b45e1938c73b3f128a7755  MAAT-1.1.0-portable-x64.zip
f6a0297a770c7206b270bc0bfcce22edf36a5c93d4563e26920c77b8030e34a7  MAAT.exe
```

---

## Fonctionnalités

- **Audit des droits NTFS** : ACE explicites et héritées, traduction française des
  droits et portées, distinction Autoriser / Refuser, source de l'héritage.
- **Audit des tailles** : calcul bottom-up, marquage des tailles partielles (`≈`).
- **Résolution Active Directory** (optionnelle) : développement récursif des groupes
  avec détection de cycles ; annotation « (via *groupe*) » de l'appartenance indirecte.
  Dégradation propre hors domaine, sans dépendance au module RSAT.
- **Interface moderne** (WPF, MVVM) : arbre virtualisé, recherche, filtres par type
  de droit et par identité, thèmes clair / sombre, deux langues (FR / EN).
- **Exports** : CSV et **rapport HTML interactif autonome** (un seul fichier, ouvrable
  dans n'importe quel navigateur, sans dépendance externe).
- **Moteur en streaming** : énumération native, lecture ACL parallèle par lots,
  RAM bornée même sur des volumes très profonds (audit complet d'un disque système).
- **Format projet `.maat`** : base SQLite compressée (gzip), rouvrable, contenant
  résultats, paramètres et métadonnées.

## Confidentialité

Les rapports d'audit contiennent des données sensibles (droits, identités). Par
conception :

- la base de travail est créée dans `%TEMP%` puis **détruite à la fermeture** si le
  projet n'a pas été explicitement enregistré ;
- un `.maat` enregistré est une **copie distincte**, à la charge de l'utilisateur ;
- les fichiers `.maat` / `.maatdb` sont exclus du dépôt (`.gitignore`) : ne jamais
  committer de données d'audit réelles.

## Prérequis

- Windows 10 / 11 (les ACL NTFS et l'API Active Directory sont propres à Windows).
- [.NET 8 SDK](https://dotnet.microsoft.com/) pour compiler depuis les sources.
- La résolution AD nécessite une machine jointe à un domaine ; sinon elle est
  simplement désactivée.

## Compilation

```sh
dotnet build MAAT.sln -c Release
```

Exécutable autonome (single-file, self-contained) :

```sh
dotnet publish src/MAAT.App -p:PublishProfile=win-x64-selfcontained -c Release
```

Installeur MSI (nécessite [WiX Toolset v5](https://wixtoolset.org/)) :

```sh
cd installer
wix build Package.wxs -ext WixToolset.UI.wixext -o MAAT-1.1.0-x64.msi
```

Package portable (sans installation, préférences stockées à côté de l'exécutable) :

```sh
pwsh portable/build.ps1
```

## Structure du dépôt

| Chemin | Rôle |
|---|---|
| `src/MAAT.Core` | Moteur d'audit (énumération, ACL, AD, tailles) — sans dépendance UI. |
| `src/MAAT.Storage` | Persistance SQLite, format projet `.maat`. |
| `src/MAAT.Export` | Exports CSV et HTML. |
| `src/MAAT.App` | Application WPF (MVVM, thèmes, vues). |
| `installer/` | Projet MSI WiX v5. |
| `portable/` | Script de build du package portable. |
| `ressources/` | Icônes, logos, polices (sous licence OFL). |

## Architecture

L'application est découpée selon une direction de dépendances stricte :

```
MAAT.App ──► MAAT.Export ──► MAAT.Core
        └──► MAAT.Storage ─►
```

Le moteur (`StreamingAuditEngine`) émet chaque élément au fil de l'eau vers un *sink*
SQLite ; l'interface lit la base en pagination pour un arbre virtualisé, et les exports
consomment la base en streaming, sans jamais matérialiser l'ensemble en mémoire.

## Journal des versions

### 1.1.0
- Correction de divers bugs et optimisations.
- Nouvelle interface pour les résultats de l'audit.
- Raffinements de l'interface.

### 1.0.0
- Version initiale.

## Licence

Ce programme est un logiciel libre, distribué sous les termes de la
**GNU General Public License v3** — voir [LICENSE](LICENSE).

Les composants tiers (runtime .NET, SQLite, polices Hanken Grotesk / Newsreader / Geist Mono)
et leurs licences sont décrits dans [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt).

Copyright (C) 2026 Pierre-Nicolas MARTIN.

---

<sub>🤖 Ce projet a été conçu et développé avec l'assistance de Claude (Anthropic).</sub>
