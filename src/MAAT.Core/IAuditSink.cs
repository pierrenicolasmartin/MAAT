// MAAT — Monitoring et Audit des Accès pour la Traçabilité
// Copyright (C) 2026  Pierre-Nicolas MARTIN
//
// This program is free software: you can redistribute it and/or modify it
// under the terms of the GNU General Public License as published by the Free
// Software Foundation, either version 3 of the License, or (at your option)
// any later version. This program is distributed WITHOUT ANY WARRANTY; see
// the GNU General Public License <https://www.gnu.org/licenses/> for details.

using MAAT.Core.Models;

namespace MAAT.Core;

/// <summary>
/// Puits recevant les éléments d'audit au fil de l'eau (streaming). Permet
/// l'écriture incrémentale en base SQLite et l'affichage temps réel sans
/// accumuler tous les éléments en mémoire (essentiel pour des millions de fichiers).
///
/// Séquence d'appel : <see cref="Begin"/> une fois, puis <see cref="Emit"/> pour
/// chaque élément complet (ACL + taille + membres AD déjà renseignés), puis
/// <see cref="Complete"/> pour vider les tampons restants.
/// </summary>
public interface IAuditSink
{
    /// <summary>Démarre une session de réception pour un audit donné.</summary>
    void Begin(AuditParameters parameters, string rootPath);

    /// <summary>Reçoit un élément complet. Appelé dans l'ordre de l'arbre (parents avant enfants).</summary>
    void Emit(AuditItem item);

    /// <summary>
    /// Termine la session : vide les tampons, valide la transaction finale et
    /// enregistre les compteurs de synthèse.
    /// </summary>
    void Complete(AuditSummary summary);
}

/// <summary>
/// Puits qui accumule simplement les éléments en mémoire. Utile pour les tests
/// et le mode « consultation » sur de petits volumes.
/// </summary>
public sealed class CollectingAuditSink : IAuditSink
{
    private readonly List<AuditItem> _items = new();

    public IReadOnlyList<AuditItem> Items => _items;

    public void Begin(AuditParameters parameters, string rootPath) => _items.Clear();
    public void Emit(AuditItem item) => _items.Add(item);
    public void Complete(AuditSummary summary) { }
}