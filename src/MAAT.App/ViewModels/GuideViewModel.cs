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
                "Bouton « Nouvel audit » de l'écran d'accueil, menu Fichier → Nouvel audit, ou Ctrl+N. Une fenêtre de configuration s'ouvre pour régler le périmètre."),
            Sec("Projets récents",
                "L'écran d'accueil liste les derniers projets ouverts ou enregistrés : un clic les rouvre. « Effacer » vide la liste (seuls les chemins sont mémorisés, jamais les résultats)."),
            Sec("Choisir le dossier",
                "Saisissez un chemin local (ex. C:\\Partages) ou un partage réseau UNC (\\\\serveur\\partage), ou utilisez Parcourir. MAAT n'analyse que ce à quoi votre compte Windows a accès.",
                "Vous pouvez auditer une racine entière (C:\\), mais c'est long : pour débuter, ciblez un sous-dossier précis.")),

        Topic(2, "PARAMÈTRES", "Régler la portée de l'audit",
            "Quatre réglages déterminent ce qui sera analysé — et la durée de l'audit. Bien les choisir, c'est gagner du temps et de la lisibilité.",
            Sec("Profondeur d'analyse",
                "Jusqu'où descendre dans l'arborescence. La racine est le niveau 0, ses sous-dossiers le niveau 1, etc. « Illimité » parcourt tout ; limiter la profondeur accélère l'audit et allège l'affichage."),
            Sec("Contenu et éléments analysés",
                "Contenu : droits NTFS, volumétrie, ou les deux en un seul passage. Éléments : dossiers uniquement (rapide) ou dossiers et fichiers (beaucoup plus long et volumineux). Pour un audit de droits, les dossiers suffisent le plus souvent."),
            Sec("Étendue des identités",
                "Cochée, l'audit inclut les comptes locaux et intégrés (BUILTIN, AUTORITÉ NT, Tout le monde…). Décochée, il ne garde que les identités du domaine Active Directory — pratique pour se concentrer sur les accès « métier ».",
                "La volumétrie d'un dossier reflète toujours l'intégralité de son sous-arbre, même au-delà de la profondeur d'affichage choisie.")),

        Topic(3, "RÉSULTATS", "Explorer les résultats",
            "La fenêtre de résultats est un explorateur maître-détail : l'arbre (ou la liste des identités) à gauche, le détail à droite. Deux vues, Dossiers et Identités, se commutent en haut.",
            Sec("En-tête de l'audit",
                "L'en-tête rappelle le dossier audité, la date et les paramètres, puis résume les dossiers, fichiers, volumétrie, entrées de droits et identités. L'indicateur « Couverture » dit si l'audit est complet ou combien d'éléments n'ont pas pu être audités ; un clic ouvre le rapport d'analyse. Les boutons Enregistrer et Exporter sont à droite."),
            Sec("Vue Dossiers",
                "Dépliez l'arbre avec le chevron — la volumétrie s'affiche à droite de chaque dossier, le symbole ≈ signalant une taille partielle. Sélectionnez un dossier : ses droits NTFS s'affichent dans la table de détail à droite."),
            Sec("États particuliers",
                "Une icône d'avertissement ambre signale un élément audité de façon incomplète (droits illisibles, contenu non listable, boucle évitée, profondeur maximale atteinte). Les étiquettes DFS et LIEN repèrent les liens DFS et les jonctions / liens symboliques. Sélectionnez l'élément : un encart explique son état au-dessus de la table des droits."),
            Sec("Lire la table des droits",
                "Chaque ligne donne le type (pastille Autoriser ou Refuser), l'identité, les autorisations, la portée et l'héritage. Sous un groupe résolu, le lien « N membres » dévoile ses membres ; « Hérité » affiche le dossier source ; la pastille « Explicite » signale un droit posé sur l'élément lui-même.",
                "La séparation centrale est ajustable : faites glisser la poignée pour élargir l'arbre ou le détail. La largeur est mémorisée d'une session à l'autre.")),

        Topic(4, "RECHERCHE & FILTRES", "Retrouver et cibler",
            "Sur de gros volumes, la recherche et les filtres sont indispensables pour aller droit à l'essentiel.",
            Sec("Recherche",
                "Ctrl+F place le curseur dans la recherche. Tapez un nom de dossier, un nom de fichier ou un chemin : l'arbre bascule en liste à plat des correspondances."),
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
                "Basculez sur la vue Identités : chaque identité est listée avec le nombre de dossiers où elle apparaît. Sélectionnez-en une pour ouvrir son détail à droite. Le type « Groupe » n'est affiché que lorsqu'il est certain (groupe Active Directory résolu, groupe local intégré, Tout le monde)."),
            Sec("Emplacements et membres",
                "Le détail indique le type (groupe ou compte), les membres d'un groupe, et la table paginée de tous les emplacements où l'identité possède un droit — avec le type, les autorisations, la portée et l'héritage. Réglez le nombre de lignes par page et naviguez de page en page."),
            Sec("Résolution Active Directory",
                "Les groupes sont développés récursivement pour révéler les utilisateurs réels qui héritent des droits. Hors domaine, cette résolution est simplement ignorée, sans bloquer l'audit.")),

        Topic(7, "RAPPORT D'ANALYSE", "Vérifier ce que l'audit a couvert",
            "À la fin d'un audit, un rapport récapitule ce qui a été fait — et surtout ce qui n'a pas pu l'être. Il reste accessible à tout moment, y compris pour un projet rouvert, via le bouton « Rapport d'analyse » ou l'indicateur « Couverture ».",
            Sec("Périmètre couvert",
                "Nombre de dossiers, de fichiers, d'entrées de droits, volumétrie et durée : une vue d'ensemble du périmètre réellement traité."),
            Sec("Points d'attention",
                "Des tuiles mettent en avant ce qui mérite attention : accès refusés, erreurs de lecture, jonctions ignorées, liens DFS parcourus, boucles évitées, énumération basée sur l'accès (ABE) détectée sur le partage, et disponibilité d'Active Directory."),
            Sec("Éléments non audités",
                "La liste des éléments ignorés (accès refusé, jonctions…) avec leur motif. Un audit n'est fiable que si vous savez ce qu'il a manqué.",
                "Exportez le journal pour conserver une trace des accès refusés et des éléments ignorés.")),

        Topic(8, "EXPORTS & PROJETS", "Conserver et partager les résultats",
            "Les résultats se réenregistrent pour réouverture, ou s'exportent pour être partagés et retravaillés.",
            Sec("Rapport HTML",
                "Bouton « Exporter » de l'en-tête → Rapport HTML. Un fichier autonome et interactif (recherche, filtres, thème clair/sombre), ouvrable dans n'importe quel navigateur — le format idéal à transmettre."),
            Sec("Export CSV",
                "Une ligne par droit, pour le traitement dans un tableur (tri, filtres, tableaux croisés)."),
            Sec("Projet .maat",
                "« Enregistrer » (Ctrl+S) conserve l'audit complet dans un fichier rouvrable plus tard sans re-scanner : Ctrl+O, écran d'accueil ou double-clic dans l'Explorateur.")),

        Topic(9, "APPARENCE", "Adapter l'affichage",
            "L'interface s'ajuste à vos préférences de lecture.",
            Sec("Choisir le thème",
                "Menu Préférences → Affichage bascule entre Auto (suit le système), clair et sombre ; la barre de titre de Windows suit le thème. Le rapport HTML exporté possède aussi sa propre bascule clair/sombre."),
            Sec("Langue",
                "Menu Préférences → Langue : français, anglais ou automatique (langue de Windows). Le rapport HTML et l'export CSV sont produits dans la langue de l'interface.")),

        Topic(10, "CONFIDENTIALITÉ", "Manipuler des données sensibles",
            "Un audit de droits révèle qui accède à quoi : ces informations sont sensibles et méritent des précautions.",
            Sec("Données en mémoire",
                "La base de travail d'un audit non enregistré est créée dans un emplacement temporaire et détruite à la fermeture de l'application."),
            Sec("Bonnes pratiques",
                "Un rapport (HTML, CSV ou .maat) peut exposer toute la structure d'accès d'un partage. Ne le transmettez qu'à des destinataires habilités et conservez-le dans un emplacement protégé.")),

        Topic(11, "CAS PARTICULIERS", "DFS, jonctions, accès refusés…",
            "Certains éléments du système de fichiers demandent un traitement particulier — voici comment MAAT s'y prend.",
            Sec("OneDrive et fichiers cloud",
                "Les dossiers synchronisés sont parcourus normalement ; leur volumétrie logique est comptée sans déclencher de téléchargement."),
            Sec("Espaces de noms DFS",
                "Les liens DFS (\\\\domaine\\dfs\\…) sont suivis de façon transparente vers leur cible : le contenu et les droits affichés sont ceux du partage cible. Une coupure réseau passagère est automatiquement retentée."),
            Sec("Jonctions et liens symboliques",
                "Ils sont audités pour leurs droits (étiquette LIEN), mais non parcourus, afin d'éviter les boucles et les doublons."),
            Sec("Accès refusés",
                "Un élément dont les droits sont illisibles reste dans l'arbre, marqué d'une icône d'avertissement ambre. Un dossier dont le contenu ne peut être listé est journalisé, l'audit continue, et sa volumétrie est marquée ≈ (partielle). Le rapport de fin d'audit les récapitule tous."),
            Sec("Énumération basée sur l'accès (ABE)",
                "Sur un partage qui applique l'ABE, les éléments que votre compte ne peut pas lire lui sont tout simplement invisibles : ils n'apparaissent ni dans l'arbre, ni dans le journal. Le rapport d'analyse vous en avertit.",
                "Pour un audit exhaustif d'un partage ABE, utilisez un compte disposant d'un droit de lecture sur tout le périmètre."),
            Sec("Boucles et arborescences très profondes",
                "Un dossier qui réapparaît dans sa propre ascendance (boucle) n'est pas reparcouru ; au-delà de 512 niveaux, la descente s'arrête par sécurité. Les chemins de plus de 260 caractères sont pris en charge.")),

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
                "The “New audit” button on the start screen, menu File → New audit, or Ctrl+N. A configuration window opens to set the scope."),
            Sec("Recent projects",
                "The start screen lists the projects you recently opened or saved: one click reopens them. “Clear” empties the list (only paths are remembered, never results)."),
            Sec("Choose the folder",
                "Enter a local path (e.g. C:\\Shares) or a UNC network share (\\\\server\\share), or use Browse. MAAT only analyses what your Windows account can access.",
                "You can audit a whole drive root (C:\\), but it is slow: to begin, target a specific subfolder.")),

        Topic(2, "SETTINGS", "Set the audit scope",
            "Four settings decide what gets analysed — and how long it takes. Choosing them well saves time and improves readability.",
            Sec("Analysis depth",
                "How deep to descend. The root is level 0, its subfolders level 1, etc. “Unlimited” walks everything; limiting depth speeds up the audit and lightens the view."),
            Sec("Content and items analysed",
                "Content: NTFS permissions, sizes, or both in a single pass. Items: folders only (fast) or folders and files (much longer and larger). For a permissions audit, folders are usually enough."),
            Sec("Identity scope",
                "Checked, the audit includes local and built-in accounts (BUILTIN, NT AUTHORITY, Everyone…). Unchecked, it keeps only Active Directory domain identities — handy to focus on “business” access.",
                "A folder's volumetry always reflects its entire subtree, even beyond the displayed depth.")),

        Topic(3, "RESULTS", "Explore the results",
            "The results window is a master-detail explorer: the tree (or the identities list) on the left, the detail on the right. Two views, Folders and Identities, switch at the top.",
            Sec("Audit header",
                "The header recalls the audited folder, the date and the settings, then sums up folders, files, size, permission entries and identities. The “Coverage” indicator tells whether the audit is complete or how many items could not be audited; clicking it opens the analysis report. Save and Export are on the right."),
            Sec("Folders view",
                "Expand the tree with the chevron — volumetry shows to the right of each folder, the ≈ symbol marking a partial size. Select a folder: its NTFS permissions appear in the detail table on the right."),
            Sec("Special states",
                "An amber warning icon flags an item audited incompletely (unreadable permissions, content that could not be listed, loop avoided, maximum depth reached). The DFS and LINK tags mark DFS links and junctions / symbolic links. Select the item: a panel explains its state above the permissions table."),
            Sec("Read the permissions table",
                "Each row gives the type (Allow or Deny badge), the identity, the permissions, the scope and the inheritance. Under a resolved group, the “N members” link reveals its members; “Inherited” shows the source folder; the “Explicit” badge marks a right set on the item itself.",
                "The central divider is adjustable: drag the handle to widen the tree or the detail. The width is remembered across sessions.")),

        Topic(4, "SEARCH & FILTERS", "Find and focus",
            "On large volumes, search and filters are essential to get straight to the point.",
            Sec("Search",
                "Ctrl+F puts the cursor in the search box. Type a folder name, a file name or a path: the tree switches to a flat list of matches."),
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
                "Switch to the Identities view: each identity is listed with the number of folders where it appears. Select one to open its detail on the right. The “Group” type is shown only when it is certain (resolved Active Directory group, built-in local group, Everyone)."),
            Sec("Locations and members",
                "The detail shows the type (group or account), a group's members, and the paginated table of every location where the identity holds a permission — with type, permissions, scope and inheritance. Set the number of rows per page and move from page to page."),
            Sec("Active Directory resolution",
                "Groups are expanded recursively to reveal the actual users who inherit the rights. Off-domain, this resolution is simply skipped, without blocking the audit.")),

        Topic(7, "ANALYSIS REPORT", "Check what the audit covered",
            "At the end of an audit, a report summarises what was done — and above all what could not be. It remains available at any time, including for a reopened project, through the “Analysis report” button or the “Coverage” indicator.",
            Sec("Scope covered",
                "Number of folders, files, permission entries, size and duration: an overview of the scope actually processed."),
            Sec("Points of attention",
                "Tiles highlight what deserves attention: access denied, read errors, skipped junctions, DFS links followed, loops avoided, access-based enumeration (ABE) detected on the share, and Active Directory availability."),
            Sec("Non-audited items",
                "The list of skipped items (access denied, junctions…) with their reason. An audit is only trustworthy if you know what it missed.",
                "Export the log to keep a record of access-denied and skipped items.")),

        Topic(8, "EXPORTS & PROJECTS", "Keep and share the results",
            "Results can be saved for reopening, or exported to be shared and reworked.",
            Sec("HTML report",
                "Header “Export” button → HTML report. A self-contained, interactive file (search, filters, light/dark theme), openable in any browser — the ideal format to hand over."),
            Sec("CSV export",
                "One row per right, for processing in a spreadsheet (sorting, filters, pivot tables)."),
            Sec(".maat project",
                "“Save” (Ctrl+S) keeps the full audit in a file you can reopen later without re-scanning: Ctrl+O, the start screen, or a double-click in Explorer.")),

        Topic(9, "APPEARANCE", "Adjust the display",
            "The interface adapts to your reading preferences.",
            Sec("Choose the theme",
                "Menu Preferences → Display switches between Auto (follows the system), light and dark; the Windows title bar follows the theme. The exported HTML report also has its own light/dark toggle."),
            Sec("Language",
                "Menu Preferences → Language: French, English or automatic (Windows language). The HTML report and CSV export are produced in the interface language.")),

        Topic(10, "CONFIDENTIALITY", "Handling sensitive data",
            "A permissions audit reveals who accesses what: this information is sensitive and warrants care.",
            Sec("Data in memory",
                "The working database of an unsaved audit is created in a temporary location and destroyed when the application closes."),
            Sec("Good practices",
                "A report (HTML, CSV or .maat) can expose a share's entire access structure. Only hand it to authorised recipients and keep it in a protected location.")),

        Topic(11, "SPECIAL CASES", "DFS, junctions, access denied…",
            "Some file-system items need special handling — here is how MAAT deals with them.",
            Sec("OneDrive and cloud files",
                "Synced folders are walked normally; their logical volumetry is counted without triggering a download."),
            Sec("DFS namespaces",
                "DFS links (\\\\domain\\dfs\\…) are followed transparently to their target: the content and permissions shown are those of the target share. A transient network drop is retried automatically."),
            Sec("Junctions and symbolic links",
                "They are audited for their permissions (LINK tag), but not walked, to avoid loops and duplicates."),
            Sec("Access denied",
                "An item whose permissions cannot be read stays in the tree, marked with an amber warning icon. A folder whose content cannot be listed is logged, the audit continues, and its volumetry is marked ≈ (partial). The end-of-audit report lists them all."),
            Sec("Access-based enumeration (ABE)",
                "On a share that applies ABE, items your account cannot read are simply invisible to it: they appear neither in the tree nor in the log. The analysis report warns you about it.",
                "For an exhaustive audit of an ABE share, use an account with read access over the whole scope."),
            Sec("Loops and very deep trees",
                "A folder that reappears in its own ancestry (loop) is not walked again; beyond 512 levels, descent stops as a safeguard. Paths longer than 260 characters are supported.")),

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
