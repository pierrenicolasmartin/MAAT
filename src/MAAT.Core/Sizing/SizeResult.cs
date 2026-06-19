// MAAT — Monitoring et Audit des Accès pour la Traçabilité
// Copyright (C) 2026  Pierre-Nicolas MARTIN
//
// This program is free software: you can redistribute it and/or modify it
// under the terms of the GNU General Public License as published by the Free
// Software Foundation, either version 3 of the License, or (at your option)
// any later version. This program is distributed WITHOUT ANY WARRANTY; see
// the GNU General Public License <https://www.gnu.org/licenses/> for details.

namespace MAAT.Core.Sizing;

/// <summary>
/// Résultat du calcul des tailles : la taille (en octets) de chaque dossier et
/// fichier, et l'ensemble des chemins dont la taille est partielle (au moins un
/// sous-élément inaccessible — marqué « ≈ » dans les rapports).
/// </summary>
public sealed class SizeResult
{
    private readonly IReadOnlyDictionary<string, long> _sizes;

    internal SizeResult(IReadOnlyDictionary<string, long> sizes, IReadOnlySet<string> incomplete)
    {
        _sizes = sizes;
        Incomplete = incomplete;
    }

    /// <summary>Chemins dont la taille agrégée est partielle.</summary>
    public IReadOnlySet<string> Incomplete { get; }

    /// <summary>Nombre d'entrées (dossiers + fichiers) avec une taille calculée.</summary>
    public int Count => _sizes.Count;

    /// <summary>
    /// Renvoie la taille du chemin si elle a été calculée, sinon <c>null</c>.
    /// Un échec de lookup (jonction exclue, élément disparu) ne doit jamais
    /// produire un faux « 0 » : l'appelant affiche alors un champ vide (V1.0.5).
    /// </summary>
    public long? TryGet(string path)
        => _sizes.TryGetValue(path, out long v) ? v : null;

    /// <summary>Vrai si la taille de ce chemin est partielle.</summary>
    public bool IsPartial(string path) => Incomplete.Contains(path);
}