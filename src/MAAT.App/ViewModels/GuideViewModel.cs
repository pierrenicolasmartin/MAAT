// MAAT — Monitoring et Audit des Accès pour la Traçabilité
// Copyright (C) 2026  Pierre-Nicolas MARTIN
//
// This program is free software: you can redistribute it and/or modify it
// under the terms of the GNU General Public License as published by the Free
// Software Foundation, either version 3 of the License, or (at your option)
// any later version. This program is distributed WITHOUT ANY WARRANTY; see
// the GNU General Public License <https://www.gnu.org/licenses/> for details.

using System.Collections.ObjectModel;
using MAAT.App.Localization;

namespace MAAT.App.ViewModels;

/// <summary>Une sous-section d'un sujet du guide : un intertitre, un corps, et une note optionnelle.</summary>
public sealed class GuideSection
{
    public string Heading { get; init; } = string.Empty;
    public string Body { get; init; } = string.Empty;
    public string? Note { get; init; }
    public bool HasNote => !string.IsNullOrEmpty(Note);
}

/// <summary>Un sujet du guide : numéro, kicker, titre, intro (lede) et sous-sections.</summary>
public sealed class GuideTopic
{
    public string Number { get; init; } = string.Empty;   // « 01 »
    public string Kicker { get; init; } = string.Empty;   // « DÉMARRER »
    public string Title { get; init; } = string.Empty;
    public string Intro { get; init; } = string.Empty;
    public IReadOnlyList<GuideSection> Sections { get; init; } = Array.Empty<GuideSection>();

    /// <summary>Kicker du panneau de détail, ex. « 05 · DROITS NTFS ».</summary>
    public string DetailKicker => $"{Number} · {Kicker}";

    /// <summary>Texte aplati en minuscules, pour le filtre de recherche.</summary>
    public string SearchBlob { get; init; } = string.Empty;
}

/// <summary>
/// ViewModel de la fenêtre Guide : sommaire filtrable, sujet sélectionné, navigation
/// précédent/suivant et pagination. Le contenu est <b>orienté utilisateur</b> (usage du
/// logiciel, bons et mauvais usages, lecture des données) et fourni en données structurées.
/// </summary>
public sealed class GuideViewModel : ObservableObject
{
    private readonly List<GuideTopic> _all;
    private GuideTopic? _selected;
    private string _searchText = string.Empty;

    public GuideViewModel()
    {
        _all = Build(LocalizationManager.Instance.ActiveCode);
        Topics = new ObservableCollection<GuideTopic>(_all);
        _selected = _all.Count > 0 ? _all[0] : null;
        PrevCommand = new RelayCommand(() => Move(-1), () => CanPrev);
        NextCommand = new RelayCommand(() => Move(1), () => CanNext);
    }

    public ObservableCollection<GuideTopic> Topics { get; }
    public RelayCommand PrevCommand { get; }
    public RelayCommand NextCommand { get; }

    /// <summary>Émis quand le sujet change : la vue remet le défilement du détail en haut.</summary>
    public event Action? SelectionChanged;

    public GuideTopic? SelectedTopic
    {
        get => _selected;
        set { if (SetProperty(ref _selected, value)) { RaiseNavigation(); SelectionChanged?.Invoke(); } }
    }

    public string SearchText
    {
        get => _searchText;
        set { if (SetProperty(ref _searchText, value)) { ApplyFilter(); } }
    }

    private void ApplyFilter()
    {
        string q = _searchText.Trim().ToLowerInvariant();
        var match = string.IsNullOrEmpty(q) ? _all : _all.Where(t => t.SearchBlob.Contains(q)).ToList();
        Topics.Clear();
        foreach (var t in match) { Topics.Add(t); }
        if (_selected is null || !Topics.Contains(_selected))
        {
            SelectedTopic = Topics.Count > 0 ? Topics[0] : null;
        }
        RaiseNavigation();
    }

    private int Index => _selected is null ? -1 : Topics.IndexOf(_selected);
    public bool CanPrev => Index > 0;
    public bool CanNext => Index >= 0 && Index < Topics.Count - 1;
    public string Pagination => Index < 0 ? string.Empty : $"{Index + 1:00} / {Topics.Count:00}";

    private void Move(int delta)
    {
        int i = Index + delta;
        if (i >= 0 && i < Topics.Count) { SelectedTopic = Topics[i]; }
    }

    private void RaiseNavigation()
    {
        OnPropertyChanged(nameof(Pagination));
        OnPropertyChanged(nameof(CanPrev));
        OnPropertyChanged(nameof(CanNext));
        PrevCommand.RaiseCanExecuteChanged();
        NextCommand.RaiseCanExecuteChanged();
    }

    // ───────── Contenu (orienté utilisateur), FR / EN ─────────

    private static GuideTopic Topic(int n, string kicker, string title, string intro, params GuideSection[] sections)
    {
        var blob = new System.Text.StringBuilder();
        blob.Append(kicker).Append(' ').Append(title).Append(' ').Append(intro);
        foreach (var s in sections) { blob.Append(' ').Append(s.Heading).Append(' ').Append(s.Body).Append(' ').Append(s.Note); }
        return new GuideTopic
        {
            Number = n.ToString("00"),
            Kicker = kicker,
            Title = title,
            Intro = intro,
            Sections = sections,
            SearchBlob = blob.ToString().ToLowerInvariant(),
        };
    }

    private static GuideSection Sec(string heading, string body, string? note = null)
        => new() { Heading = heading, Body = body, Note = note };

    private static List<GuideTopic> Build(string lang)
        => lang == "en" ? BuildEn() : BuildFr();

    private static List<GuideTopic> BuildFr() => new()
    {
        Topic(1, "DÉMARRER", "Lancer un premier audit",
            "MAAT inventorie un dossier et en extrait les droits NTFS et/ou la volumétrie. Tout part du choix d'un dossier à analyser.",
            Sec("Ouvrir l'assistant",
                "Menu Fichier → Nouvel audit, ou le bouton de l'écran d'accueil. Une fenêtre de configuration s'ouvre pour régler le périmètre."),
            Sec("Choisir le dossier",
                "Saisissez un chemin local (ex. C:\\Partages) ou un partage réseau UNC (\\\\serveur\\partage), ou utilisez Parcourir. MAAT n'analyse que ce à quoi votre compte Windows a accès.",
                "Vous pouvez auditer une racine entière (C:\\), mais c'est long : pour débuter, ciblez un sous-dossier précis.")),

        Topic(2, "PARAMÈTRES", "Régler la portée de l'audit",
            "Quatre réglages déterminent ce qui sera analysé — et la durée de l'audit. Bien les choisir, c'est gagner du temps et de la lisibilité.",
            Sec("Profondeur d'analyse",
                "Jusqu'où descendre dans l'arborescence. La racine est le niveau 0, ses sous-dossiers le niveau 1, etc. « Illimité » parcourt tout ; limiter la profondeur accélère l'audit et allège l'affichage."),
            Sec("Type et contenu",
                "Type : dossiers seuls (rapide) ou dossiers + fichiers (beaucoup plus long et volumineux). Contenu : droits NTFS, volumétrie, ou les deux. Pour un audit de droits, les dossiers suffisent le plus souvent."),
            Sec("Étendue des identités",
                "Cochée, l'audit inclut les comptes locaux et intégrés (BUILTIN, AUTORITÉ NT, Tout le monde…). Décochée, il ne garde que les identités du domaine Active Directory — pratique pour se concentrer sur les accès « métier ».",
                "La volumétrie d'un dossier reflète toujours l'intégralité de son sous-arbre, même au-delà de la profondeur d'affichage choisie.")),

        Topic(3, "RÉSULTATS", "Explorer les résultats",
            "La fenêtre de résultats est un explorateur maître-détail : l'arbre (ou la liste des identités) à gauche, le détail à droite. Deux vues, Dossiers et Identités, se commutent en haut.",
            Sec("Repères en haut",
                "Une barre de contexte rappelle le chemin audité et la date ; un bandeau de synthèse résume le nombre de dossiers, la volumétrie, les entrées de droits et les identités."),
            Sec("Vue Dossiers",
                "Dépliez l'arbre avec le chevron — la volumétrie s'affiche à droite de chaque dossier, le symbole ≈ signalant une taille partielle. Sélectionnez un dossier : ses droits NTFS s'affichent dans la table de détail à droite."),
            Sec("Lire la table des droits",
                "Chaque ligne donne le type (Autoriser en vert, Refuser en rouge), l'identité, les autorisations, la portée et l'héritage. Cliquez le nombre de la colonne Membres pour dévoiler les membres d'un groupe, ou « Hérité » pour voir le dossier source.",
                "La séparation centrale est ajustable : faites glisser la poignée pour élargir l'arbre ou le détail. La largeur est mémorisée d'une session à l'autre.")),

        Topic(4, "RECHERCHE & FILTRES", "Retrouver et cibler",
            "Sur de gros volumes, la recherche et les filtres sont indispensables pour aller droit à l'essentiel.",
            Sec("Recherche",
                "Tapez un nom de dossier, un nom de fichier ou un chemin : l'arbre bascule en liste à plat des correspondances."),
            Sec("Filtre des droits",
                "Tous, Explicites ou Hérités. Les droits explicites — posés manuellement sur un élément — sont souvent les vrais points d'attention d'un audit."),
            Sec("Partir d'une identité",
                "Pour répondre à « à quoi cette personne a-t-elle accès ? », basculez sur la vue Identités (voir le chapitre Identités) : elle liste tous les emplacements où une identité possède un droit.")),

        Topic(5, "DROITS NTFS", "Comprendre une ligne d'autorisation",
            "Chaque ligne décrit qui peut faire quoi, et d'où vient ce droit. Savoir la lire est le cœur de l'analyse.",
            Sec("Identité et type",
                "L'identité est le compte ou le groupe concerné (DOMAINE\\groupe, BUILTIN\\…). Le type est Autoriser ou Refuser — et un Refuser l'emporte toujours sur un Autoriser."),
            Sec("Autorisations et portée",
                "Les autorisations sont les droits accordés (Contrôle total, Modifier, Lecture…). La portée précise à quoi ils s'appliquent : ce dossier, ses sous-dossiers, ses fichiers."),
            Sec("Hérité, explicite et source",
                "Un droit hérité provient d'un dossier parent — la source indique lequel. Un droit explicite est défini sur l'élément lui-même.",
                "Points d'attention fréquents : les droits explicites, et les « Contrôle total » accordés à des groupes très larges (Tout le monde, Utilisateurs authentifiés).")),

        Topic(6, "IDENTITÉS", "Savoir qui sont les comptes",
            "La vue Identités de l'explorateur consolide toutes les identités rencontrées, pour passer de « tel groupe » à « telles personnes ».",
            Sec("Liste et détail",
                "Basculez sur la vue Identités : la liste affiche les groupes Active Directory puis les comptes, chacun avec le nombre de dossiers concernés. Sélectionnez une identité pour ouvrir son détail à droite."),
            Sec("Emplacements et membres",
                "Le détail indique le type (groupe ou compte), les membres d'un groupe, et la table paginée de tous les emplacements où l'identité possède un droit — avec le type, les autorisations, la portée et l'héritage. Réglez le nombre de lignes par page et naviguez de page en page."),
            Sec("Résolution Active Directory",
                "Les groupes sont développés récursivement pour révéler les utilisateurs réels qui héritent des droits. Hors domaine, cette résolution est simplement ignorée, sans bloquer l'audit.")),

        Topic(7, "RAPPORT D'ANALYSE", "Vérifier ce que l'audit a couvert",
            "À la fin d'un audit, un rapport récapitule ce qui a été fait — et surtout ce qui n'a pas pu l'être.",
            Sec("Statistiques",
                "Nombre de dossiers, de fichiers, d'entrées de droits, d'identités, et volumétrie totale : une vue d'ensemble du périmètre réellement traité."),
            Sec("Éléments non audités",
                "La liste des éléments ignorés (accès refusé, jonctions…) avec leur motif. Un audit n'est fiable que si vous savez ce qu'il a manqué.",
                "Exportez le journal pour conserver une trace des accès refusés et des éléments ignorés.")),

        Topic(8, "EXPORTS & PROJETS", "Conserver et partager les résultats",
            "Les résultats se réenregistrent pour réouverture, ou s'exportent pour être partagés et retravaillés.",
            Sec("Rapport HTML",
                "Un fichier autonome et interactif (recherche, filtres, thème clair/sombre), ouvrable dans n'importe quel navigateur — le format idéal à transmettre."),
            Sec("Export CSV",
                "Une ligne par droit, pour le traitement dans un tableur (tri, filtres, tableaux croisés)."),
            Sec("Projet .maat",
                "Enregistre l'audit complet dans un fichier rouvrable plus tard sans re-scanner (Fichier → Ouvrir un projet, ou double-clic dans l'Explorateur).")),

        Topic(9, "APPARENCE", "Adapter l'affichage",
            "L'interface s'ajuste à vos préférences de lecture.",
            Sec("Choisir le thème",
                "Menu Préférences → Affichage bascule entre Auto (suit le système), clair et sombre. Le rapport HTML exporté possède aussi sa propre bascule clair/sombre.")),

        Topic(10, "CONFIDENTIALITÉ", "Manipuler des données sensibles",
            "Un audit de droits révèle qui accède à quoi : ces informations sont sensibles et méritent des précautions.",
            Sec("Données en mémoire",
                "La base de travail d'un audit non enregistré est créée dans un emplacement temporaire et détruite à la fermeture de l'application."),
            Sec("Bonnes pratiques",
                "Un rapport (HTML, CSV ou .maat) peut exposer toute la structure d'accès d'un partage. Ne le transmettez qu'à des destinataires habilités et conservez-le dans un emplacement protégé.")),

        Topic(11, "CAS PARTICULIERS", "OneDrive, jonctions et accès refusés",
            "Certains éléments du système de fichiers demandent un traitement particulier — voici comment MAAT s'y prend.",
            Sec("OneDrive et fichiers cloud",
                "Les dossiers synchronisés sont parcourus normalement ; leur volumétrie logique est comptée sans déclencher de téléchargement."),
            Sec("Jonctions et liens symboliques",
                "Ils sont audités pour leurs droits, mais non parcourus, afin d'éviter les boucles et les doublons."),
            Sec("Accès refusés",
                "Un dossier inaccessible est journalisé, l'audit continue, et sa volumétrie est alors marquée ≈ (partielle). Le rapport de fin d'audit les récapitule tous.")),

        Topic(12, "BON USAGE & LIMITES", "Tirer le meilleur de MAAT, sans se tromper",
            "Quelques principes pour des audits fiables et une interprétation correcte des résultats.",
            Sec("MAAT lit, ne modifie pas",
                "MAAT est un outil de lecture seule : il n'édite jamais les droits ni les fichiers. Utilisez les outils Windows pour corriger ce que l'audit met en lumière."),
            Sec("Vous voyez ce que vous pouvez voir",
                "L'audit s'exécute sous votre identité Windows : vous n'observez que ce à quoi vous avez accès. Pour un inventaire exhaustif, lancez MAAT avec un compte ayant les droits de lecture sur tout le périmètre ; les accès refusés signalent les angles morts."),
            Sec("Performances",
                "Pour de très gros volumes, limitez la profondeur ou n'analysez que les dossiers. La volumétrie, elle, reste toujours calculée intégralement.",
                "Un audit n'est qu'une photographie à un instant T : les droits évoluent. Datez et archivez vos rapports pour comparer dans le temps.")),
    };

    private static List<GuideTopic> BuildEn() => new()
    {
        Topic(1, "GET STARTED", "Run your first audit",
            "MAAT inventories a folder and extracts its NTFS permissions and/or volumetry. It all starts with choosing a folder to analyse.",
            Sec("Open the wizard",
                "Menu File → New audit, or the home-screen button. A configuration window opens to set the scope."),
            Sec("Choose the folder",
                "Enter a local path (e.g. C:\\Shares) or a UNC network share (\\\\server\\share), or use Browse. MAAT only analyses what your Windows account can access.",
                "You can audit a whole drive root (C:\\), but it is slow: to begin, target a specific subfolder.")),

        Topic(2, "SETTINGS", "Set the audit scope",
            "Four settings decide what gets analysed — and how long it takes. Choosing them well saves time and improves readability.",
            Sec("Analysis depth",
                "How deep to descend. The root is level 0, its subfolders level 1, etc. “Unlimited” walks everything; limiting depth speeds up the audit and lightens the view."),
            Sec("Type and content",
                "Type: folders only (fast) or folders + files (much longer and larger). Content: NTFS permissions, volumetry, or both. For a permissions audit, folders are usually enough."),
            Sec("Identity scope",
                "Checked, the audit includes local and built-in accounts (BUILTIN, NT AUTHORITY, Everyone…). Unchecked, it keeps only Active Directory domain identities — handy to focus on “business” access.",
                "A folder's volumetry always reflects its entire subtree, even beyond the displayed depth.")),

        Topic(3, "RESULTS", "Explore the results",
            "The results window is a master-detail explorer: the tree (or the identities list) on the left, the detail on the right. Two views, Folders and Identities, switch at the top.",
            Sec("Cues at the top",
                "A context bar recalls the audited path and date; a summary band sums up the number of folders, the volumetry, the permission entries and the identities."),
            Sec("Folders view",
                "Expand the tree with the chevron — volumetry shows to the right of each folder, the ≈ symbol marking a partial size. Select a folder: its NTFS permissions appear in the detail table on the right."),
            Sec("Read the permissions table",
                "Each row gives the type (Allow in green, Deny in red), the identity, the permissions, the scope and the inheritance. Click the count in the Members column to reveal a group's members, or “Inherited” to see the source folder.",
                "The central divider is adjustable: drag the handle to widen the tree or the detail. The width is remembered across sessions.")),

        Topic(4, "SEARCH & FILTERS", "Find and focus",
            "On large volumes, search and filters are essential to get straight to the point.",
            Sec("Search",
                "Type a folder name, a file name or a path: the tree switches to a flat list of matches."),
            Sec("Permissions filter",
                "All, Explicit or Inherited. Explicit rights — set manually on an item — are often the real focus points of an audit."),
            Sec("Start from an identity",
                "To answer “what can this person access?”, switch to the Identities view (see the Identities chapter): it lists every location where an identity holds a permission.")),

        Topic(5, "NTFS PERMISSIONS", "Understand a permission row",
            "Each row describes who can do what, and where the right comes from. Reading it is the heart of the analysis.",
            Sec("Identity and type",
                "The identity is the account or group concerned (DOMAIN\\group, BUILTIN\\…). The type is Allow or Deny — and a Deny always overrides an Allow."),
            Sec("Permissions and scope",
                "Permissions are the granted rights (Full control, Modify, Read…). The scope states what they apply to: this folder, its subfolders, its files."),
            Sec("Inherited, explicit and source",
                "An inherited right comes from a parent folder — the source tells which one. An explicit right is set on the item itself.",
                "Frequent focus points: explicit rights, and “Full control” granted to very broad groups (Everyone, Authenticated Users).")),

        Topic(6, "IDENTITIES", "Know who the accounts are",
            "The explorer's Identities view consolidates every identity met, to move from “that group” to “those people”.",
            Sec("List and detail",
                "Switch to the Identities view: the list shows Active Directory groups then accounts, each with the number of folders involved. Select an identity to open its detail on the right."),
            Sec("Locations and members",
                "The detail shows the type (group or account), a group's members, and the paginated table of every location where the identity holds a permission — with type, permissions, scope and inheritance. Set the number of rows per page and move from page to page."),
            Sec("Active Directory resolution",
                "Groups are expanded recursively to reveal the actual users who inherit the rights. Off-domain, this resolution is simply skipped, without blocking the audit.")),

        Topic(7, "ANALYSIS REPORT", "Check what the audit covered",
            "At the end of an audit, a report summarises what was done — and above all what could not be.",
            Sec("Statistics",
                "Number of folders, files, permission entries, identities, and total volumetry: an overview of the scope actually processed."),
            Sec("Non-audited items",
                "The list of skipped items (access denied, junctions…) with their reason. An audit is only trustworthy if you know what it missed.",
                "Export the log to keep a record of access-denied and skipped items.")),

        Topic(8, "EXPORTS & PROJECTS", "Keep and share the results",
            "Results can be saved for reopening, or exported to be shared and reworked.",
            Sec("HTML report",
                "A self-contained, interactive file (search, filters, light/dark theme), openable in any browser — the ideal format to hand over."),
            Sec("CSV export",
                "One row per right, for processing in a spreadsheet (sorting, filters, pivot tables)."),
            Sec(".maat project",
                "Saves the full audit in a file you can reopen later without re-scanning (File → Open a project, or double-click in Explorer).")),

        Topic(9, "APPEARANCE", "Adjust the display",
            "The interface adapts to your reading preferences.",
            Sec("Choose the theme",
                "Menu Preferences → Display switches between Auto (follows the system), light and dark. The exported HTML report also has its own light/dark toggle.")),

        Topic(10, "CONFIDENTIALITY", "Handling sensitive data",
            "A permissions audit reveals who accesses what: this information is sensitive and warrants care.",
            Sec("Data in memory",
                "The working database of an unsaved audit is created in a temporary location and destroyed when the application closes."),
            Sec("Good practices",
                "A report (HTML, CSV or .maat) can expose a share's entire access structure. Only hand it to authorised recipients and keep it in a protected location.")),

        Topic(11, "SPECIAL CASES", "OneDrive, junctions and access denied",
            "Some file-system items need special handling — here is how MAAT deals with them.",
            Sec("OneDrive and cloud files",
                "Synced folders are walked normally; their logical volumetry is counted without triggering a download."),
            Sec("Junctions and symbolic links",
                "They are audited for their permissions, but not walked, to avoid loops and duplicates."),
            Sec("Access denied",
                "An inaccessible folder is logged, the audit continues, and its volumetry is then marked ≈ (partial). The end-of-audit report lists them all.")),

        Topic(12, "GOOD USE & LIMITS", "Get the most from MAAT, the right way",
            "A few principles for reliable audits and a correct reading of the results.",
            Sec("MAAT reads, it does not modify",
                "MAAT is a read-only tool: it never edits permissions or files. Use the Windows tools to fix what the audit brings to light."),
            Sec("You see what you can see",
                "The audit runs under your Windows identity: you only observe what you can access. For an exhaustive inventory, run MAAT with an account that has read rights over the whole scope; access-denied entries flag the blind spots."),
            Sec("Performance",
                "For very large volumes, limit the depth or analyse folders only. Volumetry, however, is always computed in full.",
                "An audit is only a snapshot at a point in time: rights change. Date and archive your reports to compare over time.")),
    };
}
