// MAAT — Monitoring et Audit des Accès pour la Traçabilité
// Copyright (C) 2026  Pierre-Nicolas MARTIN
//
// This program is free software: you can redistribute it and/or modify it
// under the terms of the GNU General Public License as published by the Free
// Software Foundation, either version 3 of the License, or (at your option)
// any later version. This program is distributed WITHOUT ANY WARRANTY; see
// the GNU General Public License <https://www.gnu.org/licenses/> for details.

namespace MAAT.Core.Progress;

/// <summary>
/// Phases successives d'un audit. Correspondent aux différents
/// <c>Write-Progress -Activity</c> du script et alimentent la barre
/// « étape en cours » de l'interface.
/// </summary>
public enum ScanPhase
{
    /// <summary>Énumération BFS des dossiers.</summary>
    EnumeratingFolders,

    /// <summary>Énumération des fichiers.</summary>
    EnumeratingFiles,

    /// <summary>Lecture des ACL (séquentielle ou parallèle).</summary>
    ReadingAcl,

    /// <summary>Résolution AD des identités.</summary>
    ResolvingAd,

    /// <summary>Calcul des tailles (BFS, lecture fichiers, agrégation).</summary>
    ComputingSizes,

    /// <summary>Génération des exports (CSV / HTML).</summary>
    Exporting,
}

/// <summary>
/// Un instantané de progression émis par le moteur vers l'UI.
/// <see cref="Percent"/> vaut -1 quand l'avancement est indéterminé
/// (ex. découverte BFS, total inconnu) ; l'UI affiche alors une barre indéterminée.
/// </summary>
public sealed record ScanProgress(
    ScanPhase Phase,
    string Status,
    int Current = 0,
    int Total = 0,
    int Percent = -1)
{
    /// <summary>Crée une progression indéterminée (total inconnu).</summary>
    public static ScanProgress Indeterminate(ScanPhase phase, string status)
        => new(phase, status, Percent: -1);

    /// <summary>Crée une progression déterminée à partir d'un courant/total.</summary>
    public static ScanProgress Of(ScanPhase phase, string status, int current, int total)
    {
        int pct = total > 0 ? Math.Min(100, (int)Math.Round(current / (double)total * 100)) : 0;
        return new ScanProgress(phase, status, current, total, pct);
    }
}