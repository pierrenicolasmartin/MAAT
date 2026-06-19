// MAAT — Monitoring et Audit des Accès pour la Traçabilité
// Copyright (C) 2026  Pierre-Nicolas MARTIN
//
// This program is free software: you can redistribute it and/or modify it
// under the terms of the GNU General Public License as published by the Free
// Software Foundation, either version 3 of the License, or (at your option)
// any later version. This program is distributed WITHOUT ANY WARRANTY; see
// the GNU General Public License <https://www.gnu.org/licenses/> for details.

using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using MAAT.App.Localization;
using MAAT.App.Views;
using MAAT.Core;
using MAAT.Core.ActiveDirectory;
using MAAT.Core.Localization;
using MAAT.Core.Diagnostics;
using MAAT.Core.Models;
using MAAT.Core.Progress;
using MAAT.Export;
using MAAT.Storage;
using Microsoft.Win32;

namespace MAAT.App.ViewModels;

/// <summary>
/// Pilote un audit : exécution sur thread de fond avec écriture SQLite au fil de
/// l'eau, deux barres de progression (globale par phase + étape en cours),
/// annulation, puis chargement paresseux de l'arbre de résultats depuis la base.
/// La base et le résolveur AD restent disponibles après le scan pour la
/// sauvegarde et les exports (jalon 9c).
/// </summary>
public sealed class ScanViewModel : ObservableObject, IDisposable
{
    private readonly AuditParameters _parameters;
    private readonly BufferingScanLog _log = new();
    private readonly HashSet<ScanPhase> _phasesSeen = new();
    private readonly double _totalWeight;
    private double _smoothedEta;
    // Historique (temps, % global) pour estimer le débit récent (ETA fenêtre glissante).
    private readonly Queue<(double T, double G)> _etaSamples = new();
    private readonly CancellationTokenSource _cts = new();

    private AuditDatabase? _db;
    private AdGroupResolver? _ad;
    private AuditReadRepository? _repo;
    private long _runId;
    private TreeContext? _treeContext;
    private TreeNodeViewModel? _rootNode;
    private string _searchText = string.Empty;
    private string _searchInfo = string.Empty;
    private AccessIdentityViewModel _selectedAccess = AccessIdentityViewModel.Sentinel;
    // Message d'action stocké sous forme clé + argument pour se retraduire
    // dynamiquement quand la langue de l'interface change.
    private string? _actionKey;
    private object? _actionArg;
    private bool _isSaved;

    private readonly Stopwatch _elapsed = new();
    // Bandes [début, fin] de % global attribuées à chaque phase (selon les poids).
    private readonly Dictionary<ScanPhase, (double Start, double End)> _bands = new();
    private ScanPhase? _currentPhase;
    private double _phaseStartElapsed;
    private double _globalPercent;
    private double _stepPercent;
    private bool _stepIndeterminate = true;
    private string _phaseLabel = LocalizationManager.T("Prog_Init");
    private string _statusText = string.Empty;
    private bool _isRunning = true;
    private bool _isCompleted;
    private bool _isCancelled;
    private string _summaryText = string.Empty;
    private string _etaText = string.Empty;
    private string _elapsedText = string.Empty;
    private string _auditDateText = string.Empty;
    private DateTime _auditStamp = DateTime.Now;

    public ScanViewModel(AuditParameters parameters)
    {
        _parameters = parameters;
        _totalWeight = TotalWeight(parameters);
        BuildBands(parameters);
        CancelCommand = new RelayCommand(Cancel, () => _isRunning);
        SaveProjectCommand = new RelayCommand(SaveProject, () => IsCompleted);
        ExportCsvCommand = new RelayCommand(ExportCsv, () => IsCompleted);
        ExportHtmlCommand = new RelayCommand(ExportHtml, () => IsCompleted);
        // Le pied de page (message d'export) et le résumé des paramètres suivent
        // dynamiquement la langue de l'interface.
        LocalizationManager.Instance.PropertyChanged += OnLanguageChanged;
    }

    private void OnLanguageChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(ActionMessage));
        OnPropertyChanged(nameof(ParametersText));
    }

    public string RootPath => _parameters.RootPath;
    public AuditParameters Parameters => _parameters;

    /// <summary>Résumé lisible des paramètres (affiché pendant et après le traitement).</summary>
    public string ParametersText
    {
        get
        {
            // Le moteur compte la racine (1) ; l'interface compte les sous-niveaux
            // (0 = racine seule). On retraduit pour rester cohérent avec la liste.
            string depth = _parameters.Depth switch
            {
                0 => LocalizationManager.T("Param_DepthUnlimited"),
                1 => LocalizationManager.T("Param_DepthRoot"),
                int d => LocalizationManager.T("Param_Depth", d - 1),
            };
            string scope = LocalizationManager.T(_parameters.AuditFiles ? "Param_FoldersFiles" : "Param_Folders");
            string content = LocalizationManager.T(_parameters switch
            {
                { AuditRights: true, AuditSize: true } => "Param_RightsSize",
                { AuditRights: true } => "Param_Rights",
                _ => "Param_Size",
            });
            return $"{scope} · {content} · {depth}";
        }
    }

    /// <summary>
    /// Charge un audit existant depuis un fichier projet <c>.maat</c> (sans rescanner).
    /// </summary>
    public static ScanViewModel LoadProject(AuditDatabase db)
    {
        var repo = new AuditReadRepository(db);
        var run = repo.GetLatestRun()
                  ?? throw new InvalidOperationException("Le projet ne contient aucun audit.");
        var content = run.AuditRights && run.AuditSize ? AuditContent.RightsAndSize
            : run.AuditRights ? AuditContent.RightsOnly : AuditContent.SizeOnly;
        var p = new AuditParameters
        {
            RootPath = run.RootPath,
            Depth = run.Depth,
            Scope = run.AuditFiles ? AuditScope.FoldersAndFiles : AuditScope.FoldersOnly,
            Content = content,
        };
        var vm = new ScanViewModel(p);
        vm.InitializeFromExisting(db, run);
        return vm;
    }

    /// <summary>Base de l'audit (disponible après le scan, pour sauvegarde/export).</summary>
    public AuditDatabase? Database => _db;

    /// <summary>Synthèse de l'audit terminé.</summary>
    public AuditSummary? Summary { get; private set; }

    /// <summary>Journal accumulé pendant le scan.</summary>
    public BufferingScanLog Log => _log;

    /// <summary>Nœuds affichés : la racine en mode normal, des résultats à plat en recherche.</summary>
    public ObservableCollection<TreeNodeViewModel> Nodes { get; } = new();

    /// <summary>Terme de recherche : filtre l'affichage en liste à plat depuis SQLite.</summary>
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                OnPropertyChanged(nameof(IsSearching));
                // Une recherche désactive le filtre d'accès (mutuellement exclusifs).
                if (!string.IsNullOrWhiteSpace(value) && !_selectedAccess.IsSentinel)
                {
                    _selectedAccess = AccessIdentityViewModel.Sentinel;
                    OnPropertyChanged(nameof(SelectedAccessIdentity));
                }
                ApplyDisplay();
            }
        }
    }

    /// <summary>Identités sélectionnables pour le filtre « contrôle d'accès » (groupes puis utilisateurs).</summary>
    public ObservableCollection<AccessIdentityViewModel> AccessIdentities { get; } = new();

    /// <summary>
    /// Identité dont on veut voir les accès effectifs (direct ou via groupe AD).
    /// La sentinelle affiche l'arbre complet.
    /// </summary>
    public AccessIdentityViewModel SelectedAccessIdentity
    {
        get => _selectedAccess;
        set
        {
            var v = value ?? AccessIdentityViewModel.Sentinel;
            if (SetProperty(ref _selectedAccess, v))
            {
                if (!v.IsSentinel && !string.IsNullOrWhiteSpace(_searchText))
                {
                    _searchText = string.Empty;
                    OnPropertyChanged(nameof(SearchText));
                    OnPropertyChanged(nameof(IsSearching));
                }
                ApplyDisplay();
            }
        }
    }

    /// <summary>Info de recherche / filtre (nombre de résultats), vide en vue arbre.</summary>
    public string SearchInfo { get => _searchInfo; private set => SetProperty(ref _searchInfo, value); }

    /// <summary>Vrai si une recherche est active (bascule l'UI en liste à plat).</summary>
    public bool IsSearching => !string.IsNullOrWhiteSpace(_searchText);

    /// <summary>État de filtre ACL partagé (Tous / Explicites / Hérités).</summary>
    public AclFilterState AclFilter { get; } = new();

    /// <summary>
    /// Sélection du filtre ACL : filtre les lignes de droits affichées ET bascule
    /// l'arbre en liste à plat des seuls éléments concernés (façon rapport HTML).
    /// </summary>
    public AclFilter SelectedAclFilter
    {
        get => AclFilter.Filter;
        set
        {
            if (AclFilter.Filter != value)
            {
                AclFilter.Filter = value;
                OnPropertyChanged();
                ApplyDisplay();
            }
        }
    }

    public RelayCommand CancelCommand { get; }
    public RelayCommand SaveProjectCommand { get; }
    public RelayCommand ExportCsvCommand { get; }
    public RelayCommand ExportHtmlCommand { get; }

    /// <summary>Message de pied de page, retraduit à la volée selon la langue courante.</summary>
    public string ActionMessage => _actionKey is null
        ? string.Empty
        : (_actionArg is null ? LocalizationManager.T(_actionKey) : LocalizationManager.T(_actionKey, _actionArg));

    private void SetAction(string key, object? arg = null)
    {
        _actionKey = key;
        _actionArg = arg;
        OnPropertyChanged(nameof(ActionMessage));
    }
    public bool IsSaved { get => _isSaved; private set => SetProperty(ref _isSaved, value); }

    public double GlobalPercent
    {
        get => _globalPercent;
        private set { if (SetProperty(ref _globalPercent, value)) { OnPropertyChanged(nameof(GlobalPercentText)); } }
    }

    /// <summary>Progression globale en texte, ex. « 42 % ».</summary>
    public string GlobalPercentText => $"{(int)Math.Round(_globalPercent)} %";

    public double StepPercent { get => _stepPercent; private set => SetProperty(ref _stepPercent, value); }
    public bool StepIndeterminate { get => _stepIndeterminate; private set => SetProperty(ref _stepIndeterminate, value); }
    public string PhaseLabel { get => _phaseLabel; private set => SetProperty(ref _phaseLabel, value); }
    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }
    public string SummaryText { get => _summaryText; private set => SetProperty(ref _summaryText, value); }

    // --- Badges de synthèse (façon rapport HTML) ---
    private int _statFolders, _statFiles, _statAces, _statIdentities;
    private string _statSizeText = string.Empty;

    public int StatFolders { get => _statFolders; private set => SetProperty(ref _statFolders, value); }
    public int StatFiles { get => _statFiles; private set => SetProperty(ref _statFiles, value); }
    public int StatAces { get => _statAces; private set => SetProperty(ref _statAces, value); }
    public int StatIdentities { get => _statIdentities; private set => SetProperty(ref _statIdentities, value); }
    public string StatSizeText { get => _statSizeText; private set => SetProperty(ref _statSizeText, value); }

    public bool ShowFilesStat => _parameters.AuditFiles;
    public bool ShowSizeStat => _parameters.AuditSize;
    public bool ShowRightsStat => _parameters.AuditRights;

    private void PopulateStats(int folders, int files, int aces)
    {
        StatFolders = folders;
        StatFiles = files;
        StatAces = aces;
        if (_repo is null) { return; }
        StatIdentities = _parameters.AuditRights ? _repo.CountIdentities(_runId) : 0;
        if (_parameters.AuditSize && _repo.GetRoot(_runId)?.SizeBytes is long bytes)
        {
            StatSizeText = SizeFormatter.Format(bytes, false, LocalizationManager.Instance.ActiveCode);
        }
    }

    /// <summary>Identités de l'audit (pour la fenêtre Identités). Vide si non disponible.</summary>
    public IReadOnlyList<IdentityRow> LoadIdentities()
        => _repo?.GetIdentities(_runId) ?? Array.Empty<IdentityRow>();

    /// <summary>
    /// Construit le rapport d'analyse de fin d'audit (statistiques + éléments
    /// non audités). À n'appeler qu'après un scan terminé (<see cref="Summary"/> non nul).
    /// </summary>
    public AuditReportViewModel BuildReport()
        => new(Summary!, _log.Snapshot(), SuggestName(),
               _parameters.AuditSize ? StatSizeText : null);

    /// <summary>Temps restant estimé (vide tant qu'indéterminable).</summary>
    public string EtaText { get => _etaText; private set => SetProperty(ref _etaText, value); }

    /// <summary>Temps écoulé depuis le début de l'audit (toujours exact).</summary>
    public string ElapsedText { get => _elapsedText; private set => SetProperty(ref _elapsedText, value); }

    /// <summary>Date et heure de l'audit (affichées dans l'interface).</summary>
    public string AuditDateText { get => _auditDateText; private set => SetProperty(ref _auditDateText, value); }

    public bool IsRunning
    {
        get => _isRunning;
        private set { if (SetProperty(ref _isRunning, value)) { CancelCommand.RaiseCanExecuteChanged(); } }
    }

    public bool IsCompleted
    {
        get => _isCompleted;
        private set
        {
            if (SetProperty(ref _isCompleted, value))
            {
                SaveProjectCommand.RaiseCanExecuteChanged();
                ExportCsvCommand.RaiseCanExecuteChanged();
                ExportHtmlCommand.RaiseCanExecuteChanged();
            }
        }
    }
    public bool IsCancelled { get => _isCancelled; private set => SetProperty(ref _isCancelled, value); }

    /// <summary>Lance le scan. À appeler depuis le thread UI (pour le marshaling de progression).</summary>
    public async Task StartAsync()
    {
        _auditStamp = DateTime.Now;
        AuditDateText = DateTimeOffset.Now.ToString("dd/MM/yyyy HH:mm");
        _elapsed.Restart();
        var progress = new Progress<ScanProgress>(OnProgress);
        try
        {
            var (summary, db) = await Task.Run(() => RunScan(progress, _cts.Token));
            Summary = summary;
            _db = db;
            BuildSummaryText(summary);
            LoadTree();
            PopulateStats(summary.FolderCount, summary.FileCount, summary.AceTotal);
            GlobalPercent = 100;
            StepPercent = 100;
            StepIndeterminate = false;
            EtaText = string.Empty;
            PhaseLabel = LocalizationManager.T("Prog_Done");
            IsCompleted = true;
        }
        catch (OperationCanceledException)
        {
            IsCancelled = true;
            PhaseLabel = LocalizationManager.T("Prog_Cancelled");
        }
        catch (Exception ex)
        {
            PhaseLabel = LocalizationManager.T("Prog_Error");
            StatusText = ex.Message;
        }
        finally
        {
            IsRunning = false;
        }
    }

    private (AuditSummary, AuditDatabase) RunScan(IProgress<ScanProgress> progress, CancellationToken token)
    {
        var db = AuditDatabase.CreateTemporary();
        _ad = new AdGroupResolver(_log);
        var engine = new StreamingAuditEngine(_log, _ad);
        using var sink = new SqliteAuditSink(db);
        var summary = engine.Run(_parameters, sink, progress, token);
        ScanLogWriter.Write(db, _log.Snapshot());
        return (summary, db);
    }

    /// <summary>
    /// Attribue à chaque phase qui aura lieu une bande [début, fin] du % global,
    /// proportionnelle à son poids et dans l'ordre d'exécution du moteur.
    /// </summary>
    /// <summary>
    /// Phases réellement émises par le moteur streaming, dans l'ordre : une phase de
    /// <b>préparation</b> (calcul des tailles si volumétrie, sinon comptage rapide),
    /// puis la phase <b>déterminée</b> d'émission (lecture des droits). En volumétrie
    /// seule, l'émission réutilise la phase « tailles ».
    /// </summary>
    private static List<ScanPhase> PhaseOrder(AuditParameters p)
    {
        var order = new List<ScanPhase>
        {
            p.AuditSize ? ScanPhase.ComputingSizes : ScanPhase.EnumeratingFolders,
        };
        if (p.AuditRights) { order.Add(ScanPhase.ReadingAcl); }
        return order;
    }

    private void BuildBands(AuditParameters p)
    {
        double acc = 0;
        foreach (var ph in PhaseOrder(p))
        {
            double w = PhaseWeight(ph);
            _bands[ph] = (acc / _totalWeight * 100.0, (acc + w) / _totalWeight * 100.0);
            acc += w;
        }
    }

    private void OnProgress(ScanProgress p)
    {
        _phasesSeen.Add(p.Phase);
        PhaseLabel = PhaseToLabel(p.Phase);
        StatusText = p.Status;

        double nowElapsed = _elapsed.Elapsed.TotalSeconds;
        if (_currentPhase != p.Phase)
        {
            _currentPhase = p.Phase;
            _phaseStartElapsed = nowElapsed;
        }

        var (bStart, bEnd) = _bands.TryGetValue(p.Phase, out var band)
            ? band
            : (_globalPercent, 100.0);

        double target;
        if (p.Percent >= 0)
        {
            // Phase déterminée (ACL, tailles, fichiers) : le global suit exactement
            // la sous-progression à l'intérieur de la bande.
            StepIndeterminate = false;
            StepPercent = p.Percent;
            target = bStart + (bEnd - bStart) * (p.Percent / 100.0);
        }
        else
        {
            // Phase indéterminée (énumération, total inconnu) : la barre AVANCE
            // quand même, par approche asymptotique vers ~70 % de la bande, pour
            // ne jamais paraître figée.
            StepIndeterminate = true;
            double tPhase = nowElapsed - _phaseStartElapsed;
            target = bStart + (bEnd - bStart) * 0.70 * (1.0 - Math.Exp(-tPhase / 3.0));
        }

        if (target > _globalPercent) { GlobalPercent = target; } // monotone

        UpdateEta();
    }

    /// <summary>Poids relatif d'une phase dans la progression globale.</summary>
    private static double PhaseWeight(ScanPhase phase) => phase switch
    {
        ScanPhase.EnumeratingFolders => 8,
        ScanPhase.EnumeratingFiles => 7,
        ScanPhase.ComputingSizes => 20,
        ScanPhase.ReadingAcl => 50,
        ScanPhase.ResolvingAd => 15,
        _ => 5,
    };

    private void UpdateEta()
    {
        double g = _globalPercent;
        double now = _elapsed.Elapsed.TotalSeconds;

        // Temps écoulé : toujours exact, sert d'ancrage même si l'ETA reste estimée.
        var elapsedSpan = TimeSpan.FromSeconds(now);
        ElapsedText = LocalizationManager.T("Prog_Elapsed",
            elapsedSpan.TotalHours >= 1 ? elapsedSpan.ToString(@"hh\:mm\:ss") : elapsedSpan.ToString(@"mm\:ss"));

        if (g >= 100)
        {
            EtaText = string.Empty;
            return;
        }

        _etaSamples.Enqueue((now, g));
        // Fenêtre glissante (~6 s) : on mesure le débit RÉCENT, pas la moyenne
        // depuis t=0 — sinon le démarrage lent (énumération) fausse tout.
        while (_etaSamples.Count > 2 && now - _etaSamples.Peek().T > 6.0)
        {
            _etaSamples.Dequeue();
        }

        var (t0, g0) = _etaSamples.Peek();
        double dt = now - t0;
        double dg = g - g0;

        // Tant qu'aucune progression récente mesurable (phase indéterminée), on
        // n'affiche pas d'estimation trompeuse.
        if (now < 3 || dt < 0.75 || dg < 0.1)
        {
            EtaText = LocalizationManager.T("Prog_Estimating");
            return;
        }

        double ratePerSec = dg / dt;                  // points de % par seconde
        double rawSeconds = (100.0 - g) / ratePerSec;
        // Lissage léger : stabilise l'affichage sans le rendre inerte.
        _smoothedEta = _smoothedEta <= 0 ? rawSeconds : 0.6 * _smoothedEta + 0.4 * rawSeconds;

        var remaining = TimeSpan.FromSeconds(Math.Clamp(_smoothedEta, 1, 86_400));
        EtaText = LocalizationManager.T("Prog_EtaLeft",
            remaining.TotalHours >= 1 ? remaining.ToString(@"hh\:mm\:ss") : remaining.ToString(@"mm\:ss"));
    }

    private void LoadTree()
    {
        if (_db is null)
        {
            return;
        }
        _repo = new AuditReadRepository(_db);
        var run = _repo.GetLatestRun();
        if (run is null)
        {
            return;
        }
        _runId = run.Id;
        _treeContext = new TreeContext
        {
            Repo = _repo,
            Filter = AclFilter,
            RightsAudited = run.AuditRights,
        };
        var root = _repo.GetRoot(run.Id);
        if (root is not null)
        {
            _rootNode = new TreeNodeViewModel(root, _treeContext) { IsExpanded = true };
            Nodes.Clear();
            Nodes.Add(_rootNode);
        }
        BuildAccessIdentities(run.AuditRights);
    }

    private void BuildAccessIdentities(bool rightsAudited)
    {
        AccessIdentities.Clear();
        AccessIdentities.Add(AccessIdentityViewModel.Sentinel);
        if (!rightsAudited || _repo is null)
        {
            return;
        }
        var rows = _repo.GetIdentities(_runId);
        foreach (var r in rows.Where(r => !string.IsNullOrWhiteSpace(r.Members)))
        {
            AccessIdentities.Add(AccessIdentityViewModel.From(r)); // groupes d'abord
        }
        foreach (var r in rows.Where(r => string.IsNullOrWhiteSpace(r.Members)))
        {
            AccessIdentities.Add(AccessIdentityViewModel.From(r)); // puis utilisateurs
        }
    }

    /// <summary>Affiche l'arbre, ou une liste à plat (recherche ou filtre d'accès).</summary>
    private void ApplyDisplay()
    {
        if (_repo is null || _rootNode is null || _treeContext is null)
        {
            return;
        }
        // Libère les nœuds transitoires (résultats à plat) avant de vider la
        // collection ; la racine persistante (_rootNode) n'est jamais libérée ici.
        foreach (var node in Nodes)
        {
            if (!ReferenceEquals(node, _rootNode)) { node.Dispose(); }
        }
        Nodes.Clear();

        // 1) Filtre « contrôle d'accès » (prioritaire)
        if (!_selectedAccess.IsSentinel)
        {
            var items = _repo.GetItemsForAccess(_runId, _selectedAccess.Identity, _selectedAccess.Sam);
            foreach (var m in items)
            {
                Nodes.Add(new TreeNodeViewModel(m, _treeContext, leaf: true));
            }
            SearchInfo = (items.Count >= 1000 ? "1000+ " : items.Count + " ")
                         + $"élément(s) — accès de {_selectedAccess.Display}";
            return;
        }

        // 2) Recherche texte
        if (!string.IsNullOrWhiteSpace(_searchText))
        {
            string term = _searchText.Trim();
            var matches = _repo.Search(_runId, term);
            foreach (var m in matches)
            {
                Nodes.Add(new TreeNodeViewModel(m, _treeContext, leaf: true, highlight: term));
            }
            SearchInfo = matches.Count >= 500
                ? "500+ résultats — affinez la recherche"
                : $"{matches.Count} résultat(s)";
            return;
        }

        // 3) Filtre de type de droits : liste à plat des éléments concernés
        if (AclFilter.Filter != ViewModels.AclFilter.All)
        {
            bool inherited = AclFilter.Filter == ViewModels.AclFilter.Inherited;
            var items = _repo.GetItemsByAclKind(_runId, inherited);
            foreach (var m in items)
            {
                Nodes.Add(new TreeNodeViewModel(m, _treeContext, leaf: true));
            }
            string kind = inherited ? "droits hérités" : "droits explicites";
            SearchInfo = (items.Count >= 1000 ? "1000+ " : items.Count + " ") + $"élément(s) — {kind}";
            return;
        }

        // 4) Arbre complet
        Nodes.Add(_rootNode);
        SearchInfo = string.Empty;
    }

    private void InitializeFromExisting(AuditDatabase db, AuditRunRow run)
    {
        _db = db;
        IsSaved = true; // déjà un fichier projet
        LoadTree();     // renseigne _repo, _runId et l'arbre
        PopulateStats(run.FolderCount, run.FileCount, run.AceTotal);
        SummaryText = BuildSummaryFromRun(run);
        _auditStamp = run.StartedUtc.ToLocalTime().DateTime;
        AuditDateText = run.StartedUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
        StatusText = run.RootPath;
        PhaseLabel = LocalizationManager.T("Prog_ProjectLoaded");
        GlobalPercent = 100;
        StepPercent = 100;
        StepIndeterminate = false;
        IsRunning = false;
        IsCompleted = true;
    }

    private static string BuildSummaryFromRun(AuditRunRow s)
    {
        var parts = new List<string> { $"{s.FolderCount} dossier(s)" };
        if (s.AuditFiles) { parts.Add($"{s.FileCount} fichier(s)"); }
        if (s.AuditRights) { parts.Add($"{s.AceTotal} ACE"); }
        if (s.ReparseCount > 0) { parts.Add($"{s.ReparseCount} jonction(s)"); }
        if (s.AclErrors > 0) { parts.Add($"{s.AclErrors} accès refusé(s)"); }
        parts.Add($"{s.ElapsedMs / 1000.0:N1} s");
        return string.Join("  ·  ", parts);
    }

    private void SaveProject()
    {
        if (_db is null) { return; }
        var dialog = new SaveFileDialog
        {
            Title = LocalizationManager.T("Dlg_SaveProject"),
            Filter = LocalizationManager.T("Filter_Maat"),
            FileName = SuggestName() + ProjectService.ProjectExtension,
            DefaultExt = ProjectService.ProjectExtension,
        };
        if (dialog.ShowDialog() == true)
        {
            string path = dialog.FileName;
            var db = _db;
            RunWithBusy("Busy_Save", path, () =>
            {
                ProjectService.SaveAs(db, path);
                ReleaseMemory();
            });
            IsSaved = true;
            SetAction("Msg_Saved", path);
        }
    }

    private void ExportCsv()
    {
        if (_db is null || _repo is null) { return; }
        var dialog = new SaveFileDialog
        {
            Title = LocalizationManager.T("Dlg_ExportCsv"),
            Filter = LocalizationManager.T("Filter_Csv"),
            FileName = SuggestName() + ".csv",
            DefaultExt = ".csv",
        };
        if (dialog.ShowDialog() == true)
        {
            string path = dialog.FileName;
            string lang = LocalizationManager.Instance.ActiveCode;
            RunWithBusy("Busy_ExportCsv", path, () =>
            {
                int maxLevels = _repo.GetMaxDepth(_runId) + 1;
                new CsvExporter(lang).ExportToFile(
                    path, _repo.EnumerateAuditItems(_runId), _parameters, _parameters.RootPath, maxLevels);
                ReleaseMemory();
            });
            SetAction("Msg_ExportCsv", path);
        }
    }

    private void ExportHtml()
    {
        if (_db is null || _repo is null) { return; }
        var dialog = new SaveFileDialog
        {
            Title = LocalizationManager.T("Dlg_ExportHtml"),
            Filter = LocalizationManager.T("Filter_Html"),
            FileName = SuggestName() + ".html",
            DefaultExt = ".html",
        };
        if (dialog.ShowDialog() == true)
        {
            string path = dialog.FileName;
            var now = DateTimeOffset.Now;
            string lang = LocalizationManager.Instance.ActiveCode;
            RunWithBusy("Busy_ExportHtml", path, () =>
            {
                int count = _repo.GetLatestRun()?.ItemCount ?? 0;
                new HtmlReportExporter(lang).ExportToFile(
                    path, _repo.EnumerateAuditItems(_runId), _parameters, _parameters.RootPath, now, count);
                ReleaseMemory();
            });
            SetAction("Msg_ExportHtml", path);
        }
    }

    /// <summary>
    /// Libère la mémoire après une opération lourde (export / sauvegarde) : relâche
    /// le cache SQLite et force un compactage du Large Object Heap pour rendre la
    /// mémoire à l'OS (sinon les gros tampons restaient réservés après l'export).
    /// </summary>
    private void ReleaseMemory()
    {
        _db?.ReleaseMemory();
        System.Runtime.GCSettings.LargeObjectHeapCompactionMode =
            System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    /// <summary>Exécute une opération longue derrière un dialogue d'attente (gros volumes).</summary>
    private static void RunWithBusy(string titleKey, string path, Action work)
        => BusyWindow.Run(Application.Current?.MainWindow,
            LocalizationManager.T(titleKey),
            LocalizationManager.T("Busy_Writing", Path.GetFileName(path)),
            work);

    private string SuggestName()
    {
        string mode = _parameters switch
        {
            { AuditRights: true, AuditSize: true } => LocalizationManager.T("Name_RightsSize"),
            { AuditRights: true } => LocalizationManager.T("Name_Rights"),
            _ => LocalizationManager.T("Name_Size"),
        };
        string leaf = _parameters.RootPath.TrimEnd('\\').Split('\\', '/').LastOrDefault() ?? "racine";
        foreach (char c in Path.GetInvalidFileNameChars()) { leaf = leaf.Replace(c, '_'); }
        if (string.IsNullOrWhiteSpace(leaf)) { leaf = "racine"; }
        // Horodatage de l'audit dans le nom (tri chronologique des exports).
        return $"{mode}_{leaf}_{_auditStamp:yyyy-MM-dd_HHmm}";
    }

    private void BuildSummaryText(AuditSummary s)
    {
        var parts = new List<string> { $"{s.FolderCount} dossier(s)" };
        if (s.Parameters.AuditFiles) { parts.Add($"{s.FileCount} fichier(s)"); }
        if (s.Parameters.AuditRights) { parts.Add($"{s.AceTotal} ACE"); }
        if (s.ReparseCount > 0) { parts.Add($"{s.ReparseCount} jonction(s)"); }
        if (s.AclErrorCount > 0) { parts.Add($"{s.AclErrorCount} accès refusé(s)"); }
        parts.Add($"{s.Elapsed.TotalSeconds:N1} s");
        if (s.Parameters.AuditRights)
        {
            parts.Add(s.AdAvailable ? $"AD : {s.AdResolved} identité(s) résolue(s)" : "AD indisponible");
        }
        SummaryText = string.Join("  ·  ", parts);
    }

    private void Cancel()
    {
        if (_isRunning)
        {
            _cts.Cancel();
            PhaseLabel = LocalizationManager.T("Prog_Cancelling");
        }
    }

    /// <summary>Somme des poids des phases attendues pour ces paramètres.</summary>
    private static double TotalWeight(AuditParameters p)
    {
        double w = 0;
        foreach (var ph in PhaseOrder(p)) { w += PhaseWeight(ph); }
        return w;
    }

    private static string PhaseToLabel(ScanPhase phase) => LocalizationManager.T(phase switch
    {
        ScanPhase.EnumeratingFolders => "Phase_EnumFolders",
        ScanPhase.EnumeratingFiles => "Phase_EnumFiles",
        ScanPhase.ReadingAcl => "Phase_ReadAcl",
        ScanPhase.ResolvingAd => "Phase_ResolveAd",
        ScanPhase.ComputingSizes => "Phase_Sizes",
        ScanPhase.Exporting => "Phase_Export",
        _ => "Phase_Default",
    });

    public void Dispose()
    {
        LocalizationManager.Instance.PropertyChanged -= OnLanguageChanged;
        foreach (var node in Nodes)
        {
            if (!ReferenceEquals(node, _rootNode)) { node.Dispose(); }
        }
        _rootNode?.Dispose();
        _cts.Dispose();
        _ad?.Dispose();
        // La base est fermée/supprimée par le propriétaire (MainViewModel) selon
        // qu'elle a été sauvegardée ou non.
    }
}