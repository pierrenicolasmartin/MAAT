// MAAT — Monitoring et Audit des Accès pour la Traçabilité
// Copyright (C) 2026  Pierre-Nicolas MARTIN
//
// This program is free software: you can redistribute it and/or modify it
// under the terms of the GNU General Public License as published by the Free
// Software Foundation, either version 3 of the License, or (at your option)
// any later version. This program is distributed WITHOUT ANY WARRANTY; see
// the GNU General Public License <https://www.gnu.org/licenses/> for details.

using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
using MAAT.Core.Acl;
using MAAT.Core.ActiveDirectory;
using MAAT.Core.Common;
using MAAT.Core.Diagnostics;
using MAAT.Core.Localization;
using MAAT.Core.Models;
using MAAT.Core.Progress;
using MAAT.Core.Scanning;
using MAAT.Core.Sizing;
using AceType = MAAT.Core.Models.AceType;

namespace MAAT.Core;

/// <summary>
/// Moteur d'audit en <b>une seule descente DFS en streaming</b>, conçu pour une
/// empreinte mémoire quasi plate et un CPU dominé par l'I/O disque :
///   • aucune carte d'ACL en mémoire (lecture du DACL brut élément par élément) ;
///   • résolution SID → nom mise en cache (une fois par identité unique) ;
///   • source d'héritage résolue via la <b>pile d'ancêtres</b> du DFS (pas de carte
///     globale ni de tri préalable) ;
///   • tailles repliées dans une passe préalable légère (carte dossier → taille).
///
/// Émet chaque <see cref="AuditItem"/> au fil de l'eau via le contrat
/// <see cref="IAuditSink"/> (parents avant enfants).
/// </summary>
public sealed class StreamingAuditEngine
{
    private const long ProgressThrottleMs = 150;
    // En-deçà de ce nombre d'enfants, la construction reste séquentielle (le coût
    // d'orchestration parallèle ne serait pas amorti).
    private const int ParallelThreshold = 8;
    // Taille d'un lot de construction parallèle : borne la mémoire bufferisée sur les
    // répertoires à très large éventail, tout en gardant un parallélisme efficace.
    private const int ChunkSize = 512;

    private readonly IScanLog _log;
    private readonly IAdGroupResolver _ad;
    private ParallelOptions _parallelOptions = new();

    // État d'un run (réinitialisé à chaque Run).
    private AuditParameters _p = null!;
    private IAuditSink _sink = null!;
    private IProgress<ScanProgress>? _progress;
    private CancellationToken _ct;
    private SizeResult? _sizes;
    private StreamingAclReader? _reader;
    private SidNameResolver _resolver = new();
    private SecurityIdentifier? _machineSid;
    private readonly List<DirFrame> _ancestors = new(64);
    private readonly HashSet<string> _adSams = new(StringComparer.OrdinalIgnoreCase);
    private int _maxDepth;
    private int _totalItems;
    private int _itemCount;
    private int _aceTotal;
    private int _reparseCount;
    private Stopwatch _sw = new();
    private long _nextProgress;

    public StreamingAuditEngine(IScanLog? log = null, IAdGroupResolver? adResolver = null)
    {
        _log = log ?? NullScanLog.Instance;
        _ad = adResolver ?? NullAdGroupResolver.Instance;
    }

    public AuditSummary Run(
        AuditParameters parameters,
        IAuditSink sink,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        string root = parameters.RootPath;

        _p = parameters;
        _sink = sink;
        _progress = progress;
        _ct = cancellationToken;
        _maxDepth = parameters.Depth;
        _itemCount = 0;
        _aceTotal = 0;
        _reparseCount = 0;
        _folderCount = 0;
        _fileCount = 0;
        _resolver = new SidNameResolver();
        _ancestors.Clear();
        _adSams.Clear();
        _machineSid = parameters is { AuditRights: true, IncludeLocalAccounts: false }
            ? IdentityUtils.GetLocalMachineSid()
            : null;
        _reader = parameters.AuditRights ? new StreamingAclReader(_log) : null;
        _parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Min(16, Math.Max(2, Environment.ProcessorCount * 2)),
            CancellationToken = cancellationToken,
        };

        sink.Begin(parameters, root);

        // --- Passe 1 : préparation — détermine le TOTAL d'éléments à émettre (dénominateur
        //     de la barre de progression et de l'ETA), avant la passe lourde de lecture ACL.
        //     • avec volumétrie : le parcours des tailles (pleine profondeur) compte aussi,
        //       gratuitement, les éléments du périmètre d'audit ;
        //     • sans volumétrie : un comptage rapide, limité à la profondeur d'audit.
        if (parameters.AuditSize)
        {
            var sizeIndexer = new SizeIndexer(_log, parameters.Lang);
            _sizes = sizeIndexer.Compute(root, _maxDepth, parameters.AuditFiles, progress, cancellationToken);
            _totalItems = sizeIndexer.EmitItemCount;
        }
        else
        {
            _sizes = null;
            _totalItems = CountEmitItems(root, progress, cancellationToken);
        }

        // --- Passe 2 : descente DFS profondeur-limitée, lecture ACL + émission au fil de l'eau ---
        _sw = Stopwatch.StartNew();
        _nextProgress = 0;
        string rootName = root.TrimEnd('\\').Split('\\')[^1]; // « C:\ » → « C: »
        var rootBuilt = BuildItem(root, rootName, depth: 0, isFile: false, isReparse: false, fileSize: null);
        VisitDirectory(rootBuilt, root, depth: 0);

        ReportProgress(final: true);
        sw.Stop();

        var summary = new AuditSummary
        {
            Parameters = parameters,
            RootPath = root,
            Elapsed = sw.Elapsed,
            FolderCount = _folderCount,
            FileCount = _fileCount,
            ItemCount = _itemCount,
            AceTotal = _aceTotal,
            ReparseCount = _reparseCount,
            AclErrorCount = _reader?.AclErrorCount ?? 0,
            AdErrorCount = (_ad as AdGroupResolver)?.AdErrorCount ?? 0,
            AdResolved = _adSams.Count,
            AdAvailable = _ad.IsAvailable,
        };

        sink.Complete(summary);
        return summary;
    }

    /// <summary>Variante de confort accumulant en mémoire (tests / banc de diff).</summary>
    public AuditResult RunToList(
        AuditParameters parameters,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var sink = new CollectingAuditSink();
        var summary = Run(parameters, sink, progress, cancellationToken);
        return new AuditResult { Summary = summary, Items = sink.Items };
    }

    private int _folderCount;
    private int _fileCount;

    /// <summary>
    /// Comptage rapide des éléments à émettre (mode sans volumétrie) : même logique
    /// d'arbre et de profondeur que la passe d'émission, mais sans lecture ACL ni
    /// taille — juste pour connaître le total (dénominateur de progression / ETA).
    /// </summary>
    private int CountEmitItems(string root, IProgress<ScanProgress>? progress, CancellationToken ct)
    {
        int count = 0;
        var sw = Stopwatch.StartNew();
        long next = 0;

        void Recurse(string dir, int depth)
        {
            ct.ThrowIfCancellationRequested();
            count++; // ce dossier
            if (progress is not null && sw.ElapsedMilliseconds >= next)
            {
                progress.Report(ScanProgress.Indeterminate(
                    ScanPhase.EnumeratingFolders, CoreStrings.T(_p.Lang, "Prog_FoldersDiscovered", count)));
                next = sw.ElapsedMilliseconds + ProgressThrottleMs;
            }

            var children = FastDirectoryEnumerator.List(dir, out _);
            if (_p.AuditFiles)
            {
                foreach (var e in children)
                {
                    if (!e.IsDirectory) { count++; }
                }
            }
            bool expand = _maxDepth == 0 || depth <= _maxDepth - 2;
            if (expand)
            {
                foreach (var e in children)
                {
                    if (!e.IsDirectory) { continue; }
                    if (e.IsNameSurrogate) { count++; } // jonction émise comme feuille
                    else { Recurse(e.FullPath, depth + 1); }
                }
            }
        }

        Recurse(root, 0);
        return count;
    }

    /// <summary>
    /// Visite un répertoire <b>normal</b> (non jonction) : émet son item (droits +
    /// taille), empile son cadre d'héritage, puis traite ses fichiers et descend
    /// dans ses sous-dossiers dans la limite de profondeur.
    /// </summary>
    private void VisitDirectory(Built dirBuilt, string path, int depth)
    {
        _ct.ThrowIfCancellationRequested();

        // Émission séquentielle du dossier lui-même (déjà construit par l'appelant).
        EmitBuilt(dirBuilt);
        _folderCount++;

        // Le cadre est empilé même si l'item n'a pas pu être émis (ACL illisible) :
        // ses clés seront vides et ne serviront alors jamais de source (parité).
        _ancestors.Add(new DirFrame(path, dirBuilt.Keys));

        var children = FastDirectoryEnumerator.List(path, out _);

        // Fichiers du dossier : construits en parallèle PAR LOTS, émis en séquence.
        if (_p.AuditFiles)
        {
            var files = children.Where(e => !e.IsDirectory).ToList();
            for (int start = 0; start < files.Count; start += ChunkSize)
            {
                int count = Math.Min(ChunkSize, files.Count - start);
                var built = BuildChunk(files, start, count, depth + 1, isFile: true);
                for (int i = 0; i < count; i++)
                {
                    EmitBuilt(built[i]);
                    _fileCount++;
                }
                ReportProgress(final: false); // mises à jour fluides sur les très gros dossiers
            }
        }

        // Sous-dossiers : on n'expanse que si les enfants restent dans la profondeur.
        // (profondeur N couvre les niveaux 0..N-1 ; on expanse tant que depth <= N-2.)
        bool expand = _maxDepth == 0 || depth <= _maxDepth - 2;
        if (expand)
        {
            var subdirs = children.Where(e => e.IsDirectory).ToList();
            // Construction PAR LOTS pour borner la mémoire sur les répertoires à très
            // large éventail (ex. WinSxS, ~100k sous-dossiers) : on ne bufferise jamais
            // plus d'un lot d'items à la fois. La pile d'ancêtres est en lecture seule
            // pendant la construction parallèle d'un lot.
            for (int start = 0; start < subdirs.Count; start += ChunkSize)
            {
                int count = Math.Min(ChunkSize, subdirs.Count - start);
                var built = BuildChunk(subdirs, start, count, depth + 1, isFile: false);
                for (int i = 0; i < count; i++)
                {
                    var e = subdirs[start + i];
                    if (e.IsNameSurrogate)
                    {
                        // Jonction / lien : auditée pour ses droits, mais non parcourue.
                        EmitBuilt(built[i]);
                        _folderCount++;
                        _reparseCount++;
                        _log.Write("ENUM_REPARSE_IGNORE", e.FullPath,
                            "Jonction / lien symbolique : contenu non parcouru (anti-boucle, anti-doublon)");
                    }
                    else
                    {
                        VisitDirectory(built[i], e.FullPath, depth + 1); // émet en séquence à l'intérieur
                    }
                }
            }
        }

        _ancestors.RemoveAt(_ancestors.Count - 1);
        ReportProgress(final: false);
    }

    /// <summary>
    /// Construit, <b>en parallèle</b>, les items d'un lot d'entrées sœurs (lecture ACL
    /// + traduction). Sûr : chaque item est indépendant, le cache SID est concurrent et
    /// la pile d'ancêtres est en lecture seule ici. L'émission (puits, AD, compteurs)
    /// reste séquentielle, hors de cette méthode.
    /// </summary>
    private Built[] BuildChunk(IReadOnlyList<FsEntry> entries, int start, int count, int depth, bool isFile)
    {
        var result = new Built[count];

        // Petits lots ou mode sans droits : séquentiel (coût d'orchestration non amorti).
        if (!_p.AuditRights || count <= ParallelThreshold)
        {
            for (int i = 0; i < count; i++)
            {
                result[i] = BuildOne(entries[start + i], depth, isFile);
            }
            return result;
        }

        Parallel.For(0, count, _parallelOptions, i =>
        {
            result[i] = BuildOne(entries[start + i], depth, isFile);
        });
        return result;
    }

    private Built BuildOne(FsEntry e, int depth, bool isFile)
    {
        bool isReparse = !isFile && e.IsNameSurrogate;
        long? size = isFile ? (e.IsNameSurrogate ? null : e.Size) : null;
        return BuildItem(e.FullPath, e.Name, depth, isFile, isReparse, size);
    }

    /// <summary>
    /// Construit un item complet (enveloppe + taille + ACL traduites) <b>sans
    /// l'émettre</b> ni résoudre l'AD (différés à <see cref="EmitBuilt"/>, séquentiel).
    /// Thread-safe. En mode Droits, une ACL illisible donne un <see cref="Built"/> vide
    /// (item non émis ensuite).
    /// </summary>
    private Built BuildItem(
        string path, string name, int depth, bool isFile, bool isReparse, long? fileSize)
    {
        var item = new AuditItem
        {
            FullPath = path,
            Name = name,
            Depth = depth,
            IsFile = isFile,
            IsReparse = isReparse,
        };

        // Taille : dossier → carte ; fichier → longueur directe (déjà calculée).
        if (_sizes is not null)
        {
            if (isFile)
            {
                item.SizeBytes = fileSize;
                item.SizePartial = false;
            }
            else
            {
                long? sz = _sizes.TryGet(path);
                item.SizeBytes = sz;
                item.SizePartial = sz is not null && _sizes.IsPartial(path);
            }
        }

        // Droits : lecture des règles (en SID) + construction des ACE traduites.
        if (_p.AuditRights)
        {
            var rules = _reader!.TryReadRules(path, isDirectory: !isFile);
            if (rules is null)
            {
                return new Built(null, EmptyKeys); // ACL illisible → item non émis
            }
            var keys = BuildAces(item, path, rules);
            return new Built(item, keys);
        }

        return new Built(item, EmptyKeys);
    }

    /// <summary>Émission séquentielle : résolution AD, écriture au puits, compteurs.</summary>
    private void EmitBuilt(Built b)
    {
        if (b.Item is null)
        {
            return; // ACL illisible : élément non émis (cohérent avec la lecture des droits)
        }
        if (_ad.IsAvailable)
        {
            ApplyAd(b.Item);
        }
        _sink.Emit(b.Item);
        _itemCount++;
        _aceTotal += b.Item.Acl.Count;
    }

    /// <summary>
    /// Traduit les ACE brutes en <see cref="AceEntry"/> (filtrage par SID, droits /
    /// portée localisés, source d'héritage via la pile d'ancêtres) et les ajoute à
    /// l'item. Renvoie les clés des ACE explicites de cet élément.
    /// </summary>
    private HashSet<string> BuildAces(AuditItem item, string path, AuthorizationRuleCollection rules)
    {
        HashSet<string>? explicitKeys = null;

        foreach (var rule in rules)
        {
            if (rule is not FileSystemAccessRule fsRule)
            {
                continue;
            }

            // Une ACE malformée (SID/droits illisibles) ne doit jamais interrompre le
            // traitement des autres ACE de l'élément — d'autant qu'on construit en
            // parallèle (une exception remonterait en AggregateException et planterait
            // l'audit). On journalise et on poursuit avec les autres ACE.
            try
            {
                var sid = (SecurityIdentifier)fsRule.IdentityReference;
                string idValue = _resolver.Resolve(sid);
                if (string.IsNullOrEmpty(idValue))
                {
                    continue;
                }

                // Filtrage « domaine uniquement » par SID (indépendant de la langue).
                if (!_p.IncludeLocalAccounts && !IdentityUtils.IsDomainSid(sid, _machineSid))
                {
                    continue;
                }

                int mask = (int)fsRule.FileSystemRights;
                bool inherited = fsRule.IsInherited;
                bool isDeny = fsRule.AccessControlType == AccessControlType.Deny;
                int aceTypeInt = isDeny ? 1 : 0;
                // Clé normalisée (générique → spécifique) pour que les ACE héritées sous
                // forme spécifique retrouvent leur ancêtre stocké sous forme générique.
                string mapKey = $"{idValue}|{AccessMask.NormalizeForMatch(mask)}|{aceTypeInt}";

                // Droit EXPLICITE : la source est l'élément lui-même → non stockée (vide),
                // redondante et inutile à l'affichage. On enregistre tout de même la clé
                // pour que les ACE héritées des descendants la retrouvent.
                // Droit HÉRITÉ : on résout l'ancêtre source.
                string source;
                if (!inherited)
                {
                    (explicitKeys ??= new HashSet<string>(StringComparer.Ordinal)).Add(mapKey);
                    source = string.Empty;
                }
                else
                {
                    source = ResolveInheritanceSource(mapKey, path, explicitKeys);
                }

                item.Acl.Add(new AceEntry
                {
                    Identity = idValue,
                    Type = isDeny ? AceType.Deny : AceType.Allow,
                    RightsFr = NtfsRightsTranslator.Translate(fsRule.FileSystemRights, _p.Lang),
                    ScopeFr = InheritanceScopeTranslator.Translate(
                        fsRule.InheritanceFlags, fsRule.PropagationFlags, _p.Lang),
                    IsInherited = inherited,
                    SourcePath = source,
                });
                if (isDeny)
                {
                    item.HasDeny = true;
                }
            }
            catch (Exception ex)
            {
                _log.Write("ERREUR_ACE", path, $"ACE illisible ignorée : {ex.Message}");
            }
        }

        return explicitKeys ?? EmptyKeys;
    }

    /// <summary>
    /// Source d'héritage : une ACE explicite est sa propre source ; une ACE héritée
    /// retrouve l'ancêtre explicite le plus proche (pile, du plus profond au plus
    /// haut), ou cet élément lui-même s'il la définit aussi explicitement, sinon
    /// « Source inconnue » (libellé d'origine conservé tel quel).
    /// </summary>
    private string ResolveInheritanceSource(
        string mapKey, string path, HashSet<string>? selfExplicitKeys)
    {
        if (selfExplicitKeys is not null && selfExplicitKeys.Contains(mapKey))
        {
            return path;
        }
        for (int i = _ancestors.Count - 1; i >= 0; i--)
        {
            if (_ancestors[i].ExplicitKeys.Contains(mapKey))
            {
                return _ancestors[i].Path;
            }
        }
        return "Source inconnue";
    }

    private void ApplyAd(AuditItem item)
    {
        foreach (var ace in item.Acl)
        {
            if (!IdentityUtils.IsSystemPrefixed(ace.Identity))
            {
                _adSams.Add(IdentityUtils.ExtractSam(ace.Identity));
            }
        }
        _ad.ApplyMembers(item);
    }

    /// <summary>
    /// Progression DÉTERMINÉE de la passe d'émission : avancement = éléments traités
    /// / total connu (calculé en passe 1). La phase rapportée dépend du mode (lecture
    /// des droits, ou calcul des tailles si volumétrie seule), pour s'aligner sur les
    /// bandes de l'UI et alimenter une ETA réaliste.
    /// </summary>
    private void ReportProgress(bool final)
    {
        if (_progress is null)
        {
            return;
        }
        ScanPhase phase = _p.AuditRights ? ScanPhase.ReadingAcl : ScanPhase.ComputingSizes;
        int processed = _folderCount + _fileCount;
        int total = Math.Max(_totalItems, processed); // borne : jamais > 100 %

        if (final)
        {
            _progress.Report(ScanProgress.Of(phase, $"{total} / {total}", total, total));
            return;
        }
        if (_sw.ElapsedMilliseconds >= _nextProgress)
        {
            _progress.Report(ScanProgress.Of(
                phase, CoreStrings.T(_p.Lang, "Prog_ItemsProcessed", processed, total), processed, total));
            _nextProgress = _sw.ElapsedMilliseconds + ProgressThrottleMs;
        }
    }

    private static readonly HashSet<string> EmptyKeys = new(StringComparer.Ordinal);

    /// <summary>Item construit prêt à émettre : l'item (null si ACL illisible) + ses clés d'ACE explicites.</summary>
    private readonly struct Built
    {
        public Built(AuditItem? item, HashSet<string> keys)
        {
            Item = item;
            Keys = keys;
        }

        public AuditItem? Item { get; }
        public HashSet<string> Keys { get; }
    }

    /// <summary>Cadre d'un dossier sur la pile d'ancêtres : chemin + clés d'ACE explicites.</summary>
    private readonly struct DirFrame
    {
        public DirFrame(string path, HashSet<string> explicitKeys)
        {
            Path = path;
            ExplicitKeys = explicitKeys;
        }

        public string Path { get; }
        public HashSet<string> ExplicitKeys { get; }
    }
}
