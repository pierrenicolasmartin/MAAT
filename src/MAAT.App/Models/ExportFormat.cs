// MAAT — Monitoring et Audit des Accès pour la Traçabilité
// Copyright (C) 2026  Pierre-Nicolas MARTIN
//
// This program is free software: you can redistribute it and/or modify it
// under the terms of the GNU General Public License as published by the Free
// Software Foundation, either version 3 of the License, or (at your option)
// any later version. This program is distributed WITHOUT ANY WARRANTY; see
// the GNU General Public License <https://www.gnu.org/licenses/> for details.

namespace MAAT.App.Models;

/// <summary>
/// Format d'export choisi avant le scan (étape 5 du script). Le CSV temporaire
/// du mode HTML seul est géré au moment de l'export.
/// </summary>
public enum ExportFormat
{
    /// <summary>CSV uniquement (par défaut).</summary>
    CsvOnly = 1,

    /// <summary>CSV + rapport HTML interactif.</summary>
    CsvAndHtml = 2,

    /// <summary>HTML interactif uniquement.</summary>
    HtmlOnly = 3,
}