// MAAT — Monitoring et Audit des Accès pour la Traçabilité
// Copyright (C) 2026  Pierre-Nicolas MARTIN
//
// This program is free software: you can redistribute it and/or modify it
// under the terms of the GNU General Public License as published by the Free
// Software Foundation, either version 3 of the License, or (at your option)
// any later version. This program is distributed WITHOUT ANY WARRANTY; see
// the GNU General Public License <https://www.gnu.org/licenses/> for details.

namespace MAAT.Core.Diagnostics;

/// <summary>Gravité d'une entrée de journal (alignée sur le log CSV du script).</summary>
public enum LogSeverity
{
    Warn,
    Error,
}

/// <summary>
/// Une entrée de journal d'audit. Reprend les colonnes du fichier
/// <c>.log.csv</c> produit par <c>Write-Log</c> : horodatage, gravité,
/// type, chemin, message. Sera persistée dans la table <c>scan_log</c>.
/// </summary>
public sealed record ScanLogEntry(
    DateTimeOffset Timestamp,
    LogSeverity Severity,
    string Type,
    string Path,
    string Message)
{
    /// <summary>
    /// Construit une entrée en dérivant la gravité du type, comme le script :
    /// un type contenant « ERREUR » ou « ERROR » est une erreur, sinon un avertissement.
    /// </summary>
    public static ScanLogEntry Create(string type, string path, string message = "")
    {
        var severity = type.Contains("ERREUR", StringComparison.OrdinalIgnoreCase)
                       || type.Contains("ERROR", StringComparison.OrdinalIgnoreCase)
            ? LogSeverity.Error
            : LogSeverity.Warn;
        return new ScanLogEntry(DateTimeOffset.UtcNow, severity, type, path, message);
    }
}

/// <summary>
/// Cible de journalisation des évènements de scan (accès refusés, jonctions
/// ignorées, erreurs AD…). Les implémentations écrivent vers SQLite, un fichier
/// CSV ou la mémoire. Doit être sûre vis-à-vis du multithreading (lecture ACL parallèle).
/// </summary>
public interface IScanLog
{
    void Write(string type, string path, string message = "");
}

/// <summary>Implémentation neutre (n'enregistre rien). Utile pour les tests.</summary>
public sealed class NullScanLog : IScanLog
{
    public static readonly NullScanLog Instance = new();
    private NullScanLog() { }
    public void Write(string type, string path, string message = "") { }
}

/// <summary>
/// Journal tamponné en mémoire, sûr vis-à-vis du multithreading (la lecture ACL
/// est parallèle). Les entrées sont ensuite persistées en bloc dans <c>scan_log</c>
/// ou exportées en <c>.log.csv</c>.
/// </summary>
public sealed class BufferingScanLog : IScanLog
{
    private readonly List<ScanLogEntry> _entries = new();
    private readonly object _gate = new();

    public void Write(string type, string path, string message = "")
    {
        var entry = ScanLogEntry.Create(type, path, message);
        lock (_gate)
        {
            _entries.Add(entry);
        }
    }

    /// <summary>Instantané des entrées accumulées.</summary>
    public IReadOnlyList<ScanLogEntry> Snapshot()
    {
        lock (_gate)
        {
            return _entries.ToArray();
        }
    }

    public int Count
    {
        get { lock (_gate) { return _entries.Count; } }
    }
}