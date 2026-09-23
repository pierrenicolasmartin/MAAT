// MAAT — Monitoring et Audit des Accès pour la Traçabilité
// Copyright (C) 2026  Pierre-Nicolas MARTIN
//
// This program is free software: you can redistribute it and/or modify it
// under the terms of the GNU General Public License as published by the Free
// Software Foundation, either version 3 of the License, or (at your option)
// any later version. This program is distributed WITHOUT ANY WARRANTY; see
// the GNU General Public License <https://www.gnu.org/licenses/> for details.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
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
/// Moteur d'audit en <b>une seule descente en profondeur, en streaming</b>, conçu pour
/// une empreinte mémoire quasi plate et un CPU dominé par l'I/O disque :
///   • aucune carte d'ACL en mémoire (lecture du DACL brut élément par élément) ;
///   • résolution SID → nom mise en cache (une fois par identité unique) ;
///   • source d'héritage résolue via la <b>pile d'ancêtres</b> (amorcée par les
///     dossiers parents de la racine, pour nommer la vraie origine des droits hérités
///     d'au-dessus du périmètre) ;
///   • tailles repliées dans une passe préalable légère (carte dossier → taille).
///
/// Fiabilité de la traversée : les éléments dont l'ACL est illisible sont conservés
/// (marqués) plutôt que retirés — sinon leur sous-arbre lisible deviendrait orphelin ;
/// les échecs d'énumération sont journalisés et marqués ; les boucles (liens DFS,
/// liens suivis côté serveur) sont détectées par identité de répertoire ; une
/// profondeur de sécurité protège la pile.
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
    // Nombre maximal de descripteurs distincts gardés en cache (borne mémoire : quelques
    // Mo au pire ; en pratique quelques centaines à quelques milliers par audit).
    private const int AclCacheLimit = 20_000;

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
    private readonly TraversalGuard _guard = new();
    private readonly ConcurrentDictionary<SdKey, ParsedAcl> _aclCache = new();
    private int _aclCacheCount;
    private readonly HashSet<string> _adSams = new(StringComparer.OrdinalIgnoreCase);
    private string _unknownSource = "Source inconnue";
    private int _maxDepth;
    private int _totalItems;
    private int _itemCount;
    private int _aceTotal;
    private int _reparseCount;
    private int _dfsLinkCount;
    private int _cycleCount;
    private int _folderCount;
    private int _fileCount;
    private bool _abe;
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
        // Racine canonique : indispensable avant le préfixe \\?\ (qui désactive toute
        // normalisation Win32). Idempotent si l'appelant l'a déjà normalisée.
        string root = PathNormalizer.NormalizeRoot(parameters.RootPath);

        _p = parameters;
        _sink = sink;
        _progress = progress;
        _ct = cancellationToken;
        _maxDepth = parameters.Depth;
        _itemCount = 0;
        _aceTotal = 0;
        _reparseCount = 0;
        _dfsLinkCount = 0;
        _cycleCount = 0;
        _folderCount = 0;
        _fileCount = 0;
        _abe = false;
        _resolver = new SidNameResolver();
        _unknownSource = CoreStrings.T(parameters.Lang, "Src_Unknown");
        _ancestors.Clear();
        _guard.Reset();
        _aclCache.Clear();
        _aclCacheCount = 0;
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

        // Partage réseau : l'énumération basée sur l'accès (ABE) masque au compte d'audit
        // ce qu'il ne peut pas lire — à signaler, car l'audit est alors incomplet sans erreur.
        if (root.StartsWith(@"\\", StringComparison.Ordinal))
        {
            _abe = DetectAccessBasedEnumeration(root);
        }

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

        // --- Passe 2 : descente en profondeur, lecture ACL + émission au fil de l'eau ---
        _sw = Stopwatch.StartNew();
        _nextProgress = 0;
        if (parameters.AuditRights)
        {
            SeedAncestorsAboveRoot(root);
        }
        string rootName = root.TrimEnd('\\').Split('\\')[^1]; // « C:\ » → « C: »
        var rootBuilt = BuildItem(root, rootName, depth: 0, isFile: false, isReparse: false, fileSize: null);
        VisitDirectory(rootBuilt, root, depth: 0, parentId: default, entry: null);

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
            DfsLinkCount = _dfsLinkCount,
            CycleCount = _cycleCount,
            AccessBasedEnumeration = _abe,
            AclErrorCount = _reader?.AclErrorCount ?? 0,
            AdErrorCount = (_ad as AdGroupResolver)?.AdErrorCount ?? 0,
            AdResolved = _adSams.Count,
            AdAvailable = _ad.IsAvailable,
        };

        sink.Complete(summary);
        return summary;
    }

    /// <summary>Variante de confort accumulant en mémoire (tests / banc de diagnostic).</summary>
    public AuditResult RunToList(
        AuditParameters parameters,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var sink = new CollectingAuditSink();
        var summary = Run(parameters, sink, progress, cancellationToken);
        return new AuditResult { Summary = summary, Items = sink.Items };
    }

    /// <summary>
    /// Comptage rapide des éléments à émettre (mode sans volumétrie) : même logique
    /// d'arbre, de profondeur et de garde que la passe d'émission, mais sans lecture
    /// ACL ni taille — juste pour connaître le total (dénominateur de progression / ETA).
    /// </summary>
    private int CountEmitItems(string root, IProgress<ScanProgress>? progress, CancellationToken ct)
    {
        int count = 0;
        var sw = Stopwatch.StartNew();
        long next = 0;
        var guard = new TraversalGuard();

        void Recurse(string dir, int depth, DirIdentity parentId, FsEntry? entry)
        {
            ct.ThrowIfCancellationRequested();
            count++; // ce dossier
            if (progress is not null && sw.ElapsedMilliseconds >= next)
            {
                progress.Report(ScanProgress.Indeterminate(
                    ScanPhase.EnumeratingFolders, CoreStrings.T(_p.Lang, "Prog_FoldersDiscovered", count)));
                next = sw.ElapsedMilliseconds + ProgressThrottleMs;
            }
            if (depth >= TraversalGuard.MaxDepth) { return; }

            bool readId = entry is null || entry.Value.IsReparse;
            var children = FastDirectoryEnumerator.List(dir, readId, out _, out var opened);
            var id = readId ? opened : parentId.Child(entry!.Value.FileId);
            if (!guard.TryEnter(id, dir, children, out bool tracked)) { return; } // boucle : émis comme feuille

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
                    else { Recurse(e.FullPath, depth + 1, id, e); }
                }
            }
            guard.Leave(id, tracked);
        }

        Recurse(root, 0, default, null);
        return count;
    }

    /// <summary>
    /// Visite un répertoire <b>normal</b> (non jonction) : le liste, vérifie l'absence de
    /// boucle, émet son item (droits + taille + états), empile son cadre d'héritage, puis
    /// traite ses fichiers et descend dans ses sous-dossiers dans la limite de profondeur.
    /// </summary>
    /// <param name="parentId">Identité du répertoire parent (volume + identifiant).</param>
    /// <param name="entry">Entrée issue du listage du parent (null pour la racine).</param>
    private void VisitDirectory(Built dirBuilt, string path, int depth, DirIdentity parentId, FsEntry? entry)
    {
        _ct.ThrowIfCancellationRequested();
        var item = dirBuilt.Item;

        // Profondeur de sécurité : protège la pile même si l'identité est inconnue.
        if (depth >= TraversalGuard.MaxDepth)
        {
            item.Flags |= ItemFlags.DepthLimit;
            _log.Write("ENUM_PROFONDEUR_MAX", path,
                $"Profondeur de sécurité ({TraversalGuard.MaxDepth} niveaux) atteinte : contenu non parcouru");
            EmitBuilt(dirBuilt);
            _folderCount++;
            return;
        }

        // Listage. L'identité est lue sur le handle pour la racine et pour les points de
        // reparse parcourus (liens DFS, espaces cloud), qui peuvent changer de volume ;
        // sinon elle découle du listage du parent (même volume, aucun appel système).
        bool readIdentity = entry is null || entry.Value.IsReparse;
        var children = FastDirectoryEnumerator.List(path, readIdentity, out int error, out DirIdentity opened);
        DirIdentity id = readIdentity ? opened : parentId.Child(entry!.Value.FileId);

        if (!_guard.TryEnter(id, path, children, out bool tracked))
        {
            item.Flags |= ItemFlags.Cycle;
            _cycleCount++;
            _log.Write("ENUM_BOUCLE", path,
                "Boucle détectée : ce dossier réapparaît dans sa propre ascendance (contenu non parcouru)");
            EmitBuilt(dirBuilt);
            _folderCount++;
            return;
        }
        if (error != NativeMethods.ERROR_SUCCESS)
        {
            item.Flags |= ItemFlags.ContentUnreadable;
            LogEnumError(path, error, partial: children.Count > 0);
        }
        if (entry is { IsDfsLink: true })
        {
            _dfsLinkCount++;
        }

        // Émission séquentielle du dossier lui-même (déjà construit par l'appelant).
        EmitBuilt(dirBuilt);
        _folderCount++;

        // Cadre d'héritage : empilé même si l'ACL est illisible (clés vides, jamais source).
        _ancestors.Add(new DirFrame(path, dirBuilt.Keys));

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
                        VisitDirectory(built[i], e.FullPath, depth + 1, id, e); // émet en séquence à l'intérieur
                    }
                }
            }
        }

        _ancestors.RemoveAt(_ancestors.Count - 1);
        _guard.Leave(id, tracked);
        ReportProgress(final: false);
    }

    private void LogEnumError(string path, int error, bool partial)
    {
        string type = error switch
        {
            NativeMethods.ERROR_ACCESS_DENIED => "ENUM_ACCES_REFUSE",
            NativeMethods.ERROR_FILE_NOT_FOUND or NativeMethods.ERROR_PATH_NOT_FOUND => "ENUM_CHEMIN_INTROUVABLE",
            _ => "ENUM_ERREUR",
        };
        string what = partial ? "Contenu du dossier listé partiellement" : "Contenu du dossier non listable";
        _log.Write(type, path, $"{what} : {Win32Text.Describe(error)}");
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
        var built = BuildItem(e.FullPath, e.Name, depth, isFile, isReparse, size);
        if (!isFile && e.IsDfsLink)
        {
            built.Item.Flags |= ItemFlags.DfsLink;
        }
        return built;
    }

    /// <summary>
    /// Construit un item complet (enveloppe + taille + ACL traduites) <b>sans
    /// l'émettre</b> ni résoudre l'AD (différés à <see cref="EmitBuilt"/>, séquentiel).
    /// Thread-safe. Une ACL illisible donne un item <b>conservé</b>, sans ACE et marqué
    /// <see cref="ItemFlags.AclUnreadable"/> : le retirer rendrait orphelin tout son
    /// sous-arbre lisible (invisible dans l'arbre), voire ferait perdre la racine.
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

        // Droits : descripteur brut → ACL analysée (en cache) → ACE de cet élément.
        if (_p.AuditRights)
        {
            var parsed = GetParsedAcl(path, isDirectory: !isFile, out bool nullDacl);
            if (parsed is null)
            {
                item.Flags |= ItemFlags.AclUnreadable;
                return new Built(item, EmptyKeys);
            }
            if (parsed.ParseError is not null)
            {
                _log.Write("ACL_ACCESS_ERREUR", path, $"Impossible de lire les règles d'accès : {parsed.ParseError}");
                item.Flags |= ItemFlags.AclUnreadable;
                return new Built(item, EmptyKeys);
            }
            if (nullDacl)
            {
                item.Flags |= ItemFlags.NullDacl;
            }
            ApplyAcl(item, path, parsed);
            return new Built(item, parsed.ExplicitKeys);
        }

        return new Built(item, EmptyKeys);
    }

    /// <summary>
    /// Lit le descripteur de sécurité d'un chemin et renvoie son ACL analysée. L'analyse
    /// (canonicalisation .NET, résolution des noms, filtrage, traduction) est faite UNE fois
    /// par descripteur distinct : la plupart des éléments partagent un descripteur identique
    /// octet pour octet (droits hérités) — c'est l'essentiel du coût CPU et des allocations.
    /// Null si le descripteur est illisible.
    /// </summary>
    private ParsedAcl? GetParsedAcl(string path, bool isDirectory, out bool nullDacl, bool quiet = false)
    {
        byte[]? sd = _reader!.TryReadDescriptor(path, out nullDacl, quiet);
        if (sd is null)
        {
            return null;
        }
        var key = new SdKey(sd, isDirectory);
        if (_aclCache.TryGetValue(key, out var cached))
        {
            return cached;
        }
        var parsed = ParseAcl(sd, isDirectory);
        // Borne mémoire : au-delà, analyse sans mise en cache (arbres à ACL toutes distinctes).
        if (Volatile.Read(ref _aclCacheCount) < AclCacheLimit && _aclCache.TryAdd(key, parsed))
        {
            Interlocked.Increment(ref _aclCacheCount);
        }
        return parsed;
    }

    /// <summary>
    /// Analyse un descripteur : ACE retenues (filtrage par SID), traduites, avec leur clé de
    /// rapprochement ; clés des ACE explicites ; erreurs d'ACE à rejouer pour chaque élément.
    /// Indépendant de l'élément (la source d'héritage, elle, est résolue par élément).
    /// </summary>
    private ParsedAcl ParseAcl(byte[] sd, bool isDirectory)
    {
        AuthorizationRuleCollection rules;
        try
        {
            rules = StreamingAclReader.ParseRules(sd, isDirectory);
        }
        catch (Exception ex)
        {
            return new ParsedAcl(Array.Empty<ParsedAce>(), EmptyKeys, null, ex.Message);
        }

        var aces = new List<ParsedAce>(rules.Count);
        HashSet<string>? explicitKeys = null;
        List<string>? errors = null;
        foreach (var rule in rules)
        {
            if (rule is not FileSystemAccessRule fsRule)
            {
                continue;
            }
            // Une ACE malformée ne doit jamais interrompre le traitement des autres : elle est
            // écartée et signalée (le message est rejoué pour chaque élément concerné).
            try
            {
                if (!TryMatchKey(fsRule, out string idValue, out string mapKey))
                {
                    continue;
                }
                bool inherited = fsRule.IsInherited;
                // « Source = soi-même » si l'élément porte AUSSI cette ACE en explicite, parmi
                // celles déjà rencontrées (ordre du DACL, même sémantique que l'ordre canonique).
                bool selfSource = false;
                if (!inherited)
                {
                    (explicitKeys ??= new HashSet<string>(StringComparer.Ordinal)).Add(mapKey);
                }
                else
                {
                    selfSource = explicitKeys?.Contains(mapKey) == true;
                }
                aces.Add(new ParsedAce(
                    idValue,
                    fsRule.AccessControlType == AccessControlType.Deny,
                    NtfsRightsTranslator.Translate(fsRule.FileSystemRights, _p.Lang),
                    InheritanceScopeTranslator.Translate(fsRule.InheritanceFlags, fsRule.PropagationFlags, _p.Lang),
                    inherited,
                    mapKey,
                    selfSource));
            }
            catch (Exception ex)
            {
                (errors ??= new List<string>()).Add($"ACE illisible ignorée : {ex.Message}");
            }
        }
        return new ParsedAcl(aces, explicitKeys ?? EmptyKeys, errors, null);
    }

    /// <summary>Émission séquentielle : résolution AD, écriture au puits, compteurs.</summary>
    private void EmitBuilt(Built b)
    {
        if (_ad.IsAvailable)
        {
            ApplyAd(b.Item);
        }
        _sink.Emit(b.Item);
        _itemCount++;
        _aceTotal += b.Item.Acl.Count;
    }

    /// <summary>
    /// Matérialise l'ACL analysée pour CET élément : une <see cref="AceEntry"/> par ACE
    /// (objet propre à l'élément : l'AD y ajoute les membres), la source d'héritage étant
    /// résolue via la pile d'ancêtres de l'élément. Les erreurs d'ACE sont rejouées ici.
    /// </summary>
    private void ApplyAcl(AuditItem item, string path, ParsedAcl parsed)
    {
        foreach (var a in parsed.Aces)
        {
            item.Acl.Add(new AceEntry
            {
                Identity = a.Identity,
                Type = a.IsDeny ? AceType.Deny : AceType.Allow,
                RightsFr = a.Rights,
                ScopeFr = a.Scope,
                IsInherited = a.IsInherited,
                // Explicite : source = l'élément lui-même, non stockée (vide, redondante).
                SourcePath = !a.IsInherited ? string.Empty
                    : a.SelfSource ? path
                    : ResolveInheritanceSource(a.MapKey),
            });
            if (a.IsDeny)
            {
                item.HasDeny = true;
            }
        }
        if (parsed.AceErrors is not null)
        {
            foreach (string message in parsed.AceErrors)
            {
                _log.Write("ERREUR_ACE", path, message);
            }
        }
    }

    /// <summary>
    /// Nom de l'identité et clé de rapprochement d'une règle, après filtrage (identité
    /// irrésoluble, filtre « domaine uniquement »). Faux si la règle est écartée.
    /// </summary>
    private bool TryMatchKey(FileSystemAccessRule rule, out string idValue, out string mapKey)
    {
        mapKey = string.Empty;
        var sid = (SecurityIdentifier)rule.IdentityReference;
        idValue = _resolver.Resolve(sid);
        if (string.IsNullOrEmpty(idValue))
        {
            return false;
        }
        // Filtrage « domaine uniquement » par SID (indépendant de la langue).
        if (!_p.IncludeLocalAccounts && !IdentityUtils.IsDomainSid(sid, _machineSid))
        {
            return false;
        }
        int mask = (int)rule.FileSystemRights;
        int aceTypeInt = rule.AccessControlType == AccessControlType.Deny ? 1 : 0;
        // Clé normalisée (générique → spécifique) pour que les ACE héritées sous forme
        // spécifique retrouvent leur ancêtre stocké sous forme générique.
        mapKey = $"{idValue}|{AccessMask.NormalizeForMatch(mask)}|{aceTypeInt}";
        return true;
    }

    /// <summary>
    /// Amorce la pile d'ancêtres avec les dossiers <b>au-dessus</b> de la racine (jusqu'à la
    /// racine du lecteur ou du partage) : les droits hérités d'au-delà du périmètre audité
    /// retrouvent alors leur vraie origine (ex. « C:\Users\PC ») au lieu de « Source
    /// inconnue ». Lectures silencieuses : un parent illisible est simplement ignoré.
    /// </summary>
    private void SeedAncestorsAboveRoot(string root)
    {
        if (root.StartsWith(@"\\?\", StringComparison.Ordinal) || root.StartsWith(@"\\.\", StringComparison.Ordinal))
        {
            return;
        }
        var parents = new List<string>();
        for (string? cur = Path.GetDirectoryName(root); !string.IsNullOrEmpty(cur); cur = Path.GetDirectoryName(cur))
        {
            parents.Add(cur);
        }
        for (int i = parents.Count - 1; i >= 0; i--) // du plus haut au plus proche
        {
            var parsed = GetParsedAcl(parents[i], isDirectory: true, out _, quiet: true);
            if (parsed is { ParseError: null, ExplicitKeys.Count: > 0 })
            {
                _ancestors.Add(new DirFrame(parents[i], parsed.ExplicitKeys));
            }
        }
    }

    /// <summary>
    /// Source d'héritage d'une ACE héritée : l'ancêtre explicite le plus proche (pile, du
    /// plus profond au plus haut — y compris les parents de la racine), sinon « Source
    /// inconnue ». (Le cas « l'élément la porte aussi en explicite » est traité à l'analyse.)
    /// </summary>
    private string ResolveInheritanceSource(string mapKey)
    {
        for (int i = _ancestors.Count - 1; i >= 0; i--)
        {
            if (_ancestors[i].ExplicitKeys.Contains(mapKey))
            {
                return _ancestors[i].Path;
            }
        }
        return _unknownSource;
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
    /// Vrai si le partage de la racine UNC applique l'énumération basée sur l'accès (ABE),
    /// qui masque au compte d'audit les éléments qu'il ne peut pas lire. Journalisé en
    /// avertissement. Meilleur effort : tout échec de la requête est ignoré.
    /// </summary>
    private bool DetectAccessBasedEnumeration(string root)
    {
        string[] parts = root.TrimStart('\\').Split('\\');
        if (parts.Length < 2 || parts[0] is "?" or ".")
        {
            return false;
        }
        try
        {
            if (NativeMethods.NetShareGetInfo(parts[0], parts[1], 1005, out IntPtr buffer) != 0 || buffer == IntPtr.Zero)
            {
                return false;
            }
            try
            {
                uint flags = unchecked((uint)Marshal.ReadInt32(buffer));
                if ((flags & NativeMethods.SHI1005_FLAGS_ACCESS_BASED_DIRECTORY_ENUM) == 0)
                {
                    return false;
                }
                _log.Write("PARTAGE_ABE", $@"\\{parts[0]}\{parts[1]}",
                    "Énumération basée sur l'accès (ABE) active sur le partage : les éléments que le compte " +
                    "d'audit ne peut pas lire lui sont invisibles, donc absents de l'audit");
                return true;
            }
            finally
            {
                NativeMethods.NetApiBufferFree(buffer);
            }
        }
        catch
        {
            return false;
        }
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

    /// <summary>Item construit prêt à émettre + ses clés d'ACE explicites.</summary>
    private readonly struct Built
    {
        public Built(AuditItem item, HashSet<string> keys)
        {
            Item = item;
            Keys = keys;
        }

        public AuditItem Item { get; }
        public HashSet<string> Keys { get; }
    }

    /// <summary>ACE analysée d'un descripteur, indépendante de l'élément qui le porte.</summary>
    private readonly record struct ParsedAce(
        string Identity, bool IsDeny, string Rights, string Scope, bool IsInherited, string MapKey, bool SelfSource);

    /// <summary>ACL analysée d'un descripteur (partagée, en lecture seule, entre éléments).</summary>
    private sealed record ParsedAcl(
        IReadOnlyList<ParsedAce> Aces, HashSet<string> ExplicitKeys, IReadOnlyList<string>? AceErrors, string? ParseError);

    /// <summary>Clé de cache : octets du descripteur + nature (dossier / fichier : l'analyse .NET diffère).</summary>
    private readonly struct SdKey : IEquatable<SdKey>
    {
        private readonly byte[] _sd;
        private readonly bool _isDirectory;
        private readonly int _hash;

        public SdKey(byte[] sd, bool isDirectory)
        {
            _sd = sd;
            _isDirectory = isDirectory;
            var h = new HashCode();
            h.AddBytes(sd);
            h.Add(isDirectory);
            _hash = h.ToHashCode();
        }

        public bool Equals(SdKey other)
            => _hash == other._hash && _isDirectory == other._isDirectory && _sd.AsSpan().SequenceEqual(other._sd);

        public override bool Equals(object? obj) => obj is SdKey k && Equals(k);
        public override int GetHashCode() => _hash;
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
