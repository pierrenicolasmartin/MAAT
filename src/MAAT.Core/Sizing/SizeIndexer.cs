// MAAT — Monitoring et Audit des Accès pour la Traçabilité
// Copyright (C) 2026  Pierre-Nicolas MARTIN
//
// This program is free software: you can redistribute it and/or modify it
// under the terms of the GNU General Public License as published by the Free
// Software Foundation, either version 3 of the License, or (at your option)
// any later version. This program is distributed WITHOUT ANY WARRANTY; see
// the GNU General Public License <https://www.gnu.org/licenses/> for details.

using System.Diagnostics;
using MAAT.Core.Common;
using MAAT.Core.Diagnostics;
using MAAT.Core.Localization;
using MAAT.Core.Progress;
using MAAT.Core.Scanning;

namespace MAAT.Core.Sizing;

/// <summary>
/// Calcul des tailles dossier en <b>une seule passe DFS</b> (énumération native),
/// agrégation bottom-up sur la pile de récursion (jonctions exclues comme
/// l'Explorateur, propagation du caractère « partiel »), sans matérialiser la liste
/// des dossiers ni une entrée par fichier : seule une carte dossier → taille est conservée.
///
/// Indépendant de la profondeur d'audit : une taille reflète toujours tout le
/// sous-arbre.
/// </summary>
public sealed class SizeIndexer
{
    private const long ProgressThrottleMs = 150;

    private readonly IScanLog _log;
    private readonly string _lang;

    private Dictionary<string, long> _sizes = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _incomplete = new(StringComparer.OrdinalIgnoreCase);
    private IProgress<ScanProgress>? _progress;
    private CancellationToken _ct;
    private Stopwatch _sw = new();
    private long _nextProgress;
    private int _dirCount;
    private int _auditMaxDepth;
    private bool _auditFiles;

    public SizeIndexer(IScanLog? log = null, string lang = "fr")
    {
        _log = log ?? NullScanLog.Instance;
        _lang = lang;
    }

    /// <summary>
    /// Nombre d'éléments qui seront <b>émis</b> par la passe d'audit (dossiers et,
    /// si demandé, fichiers, dans la limite de profondeur). Calculé gratuitement
    /// pendant le parcours des tailles ; sert de dénominateur à la barre de
    /// progression et à l'estimation du temps restant.
    /// </summary>
    public int EmitItemCount { get; private set; }

    /// <param name="auditMaxDepth">Profondeur d'audit (0 = illimitée) pour compter
    /// les éléments émis. Le calcul des tailles, lui, reste toujours en profondeur complète.</param>
    /// <param name="auditFiles">Vrai si les fichiers seront audités (donc comptés).</param>
    public SizeResult Compute(
        string rootPath,
        int auditMaxDepth,
        bool auditFiles,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        _sizes = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        _incomplete = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        _progress = progress;
        _ct = cancellationToken;
        _auditMaxDepth = auditMaxDepth;
        _auditFiles = auditFiles;
        EmitItemCount = 0;
        _sw = Stopwatch.StartNew();
        _nextProgress = 0;
        _dirCount = 0;

        ComputeDir(rootPath, 0);

        progress?.Report(ScanProgress.Indeterminate(
            ScanPhase.ComputingSizes, CoreStrings.T(_lang, "Prog_SizeEnum", _dirCount)));
        return new SizeResult(_sizes, _incomplete);
    }

    /// <summary>Vrai si un dossier à cette profondeur est dans le périmètre d'audit (donc émis).</summary>
    private bool InAuditDepth(int depth) => _auditMaxDepth == 0 || depth <= _auditMaxDepth - 1;

    /// <summary>
    /// Renvoie (taille du sous-arbre, partiel) et enregistre la taille du dossier.
    /// Compte au passage les éléments émis dans le périmètre d'audit. Séquentiel à
    /// dessein : l'énumération des métadonnées est I/O-bound et souvent dominée par
    /// un sous-arbre unique (WinSxS…), où la parallélisation déséquilibre la charge.
    /// </summary>
    private (long Size, bool Partial) ComputeDir(string dir, int depth)
    {
        _ct.ThrowIfCancellationRequested();

        _dirCount++;
        if (InAuditDepth(depth)) { EmitItemCount++; } // ce dossier sera émis
        if (_sw.ElapsedMilliseconds >= _nextProgress)
        {
            _progress?.Report(ScanProgress.Indeterminate(
                ScanPhase.ComputingSizes, CoreStrings.T(_lang, "Prog_SizeEnum", _dirCount)));
            _nextProgress = _sw.ElapsedMilliseconds + ProgressThrottleMs;
        }

        var entries = FastDirectoryEnumerator.List(dir, out int error);
        bool partial = error != NativeMethods.ERROR_SUCCESS;
        if (partial)
        {
            _log.Write("TAILLE_ACCES_REFUSE", dir,
                $"Énumération impossible lors du calcul des tailles (code {error})");
        }

        long total = 0;
        foreach (var e in entries)
        {
            if (e.IsDirectory)
            {
                if (e.IsNameSurrogate)
                {
                    // Jonction : émise comme feuille (comptée) mais ni parcourue ni dimensionnée.
                    if (InAuditDepth(depth + 1)) { EmitItemCount++; }
                    continue;
                }
                var (childSize, childPartial) = ComputeDir(e.FullPath, depth + 1);
                total += childSize;
                partial |= childPartial;
            }
            else
            {
                // Fichier : émis (compté) si l'audit porte sur les fichiers et que le
                // dossier est dans la profondeur ; sa taille n'est comptée que s'il
                // n'est pas un lien (parité avec l'Explorateur).
                if (_auditFiles && InAuditDepth(depth)) { EmitItemCount++; }
                if (!e.IsNameSurrogate) { total += e.Size; }
            }
        }

        _sizes[dir] = total;
        if (partial)
        {
            _incomplete.Add(dir);
        }
        return (total, partial);
    }
}
