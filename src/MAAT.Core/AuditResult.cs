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
/// Résultat d'un audit accumulé en mémoire (<see cref="StreamingAuditEngine.RunToList"/>) :
/// la synthèse plus la liste des éléments. À réserver aux petits volumes / tests ;
/// l'audit normal streame vers un <see cref="IAuditSink"/> et ne renvoie qu'un
/// <see cref="AuditSummary"/>.
/// </summary>
public sealed class AuditResult
{
    public required AuditSummary Summary { get; init; }
    public required IReadOnlyList<AuditItem> Items { get; init; }
}