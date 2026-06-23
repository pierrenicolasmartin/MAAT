// MAAT — Monitoring et Audit des Accès pour la Traçabilité
// Copyright (C) 2026  Pierre-Nicolas MARTIN
//
// This program is free software: you can redistribute it and/or modify it
// under the terms of the GNU General Public License as published by the Free
// Software Foundation, either version 3 of the License, or (at your option)
// any later version. This program is distributed WITHOUT ANY WARRANTY; see
// the GNU General Public License <https://www.gnu.org/licenses/> for details.

using MAAT.Core.Models;

namespace MAAT.Core.ActiveDirectory;

/// <summary>
/// Résolveur neutre : se comporte comme une machine hors domaine. Toutes les
/// résolutions renvoient une chaîne vide. Utilisé en repli et pour les tests.
/// </summary>
public sealed class NullAdGroupResolver : IAdGroupResolver
{
    public static readonly NullAdGroupResolver Instance = new();
    private NullAdGroupResolver() { }

    public bool IsAvailable => false;

    public string Resolve(string sam) => string.Empty;

    public void ApplyMembers(IEnumerable<AuditItem> items) { }

    public void ApplyMembers(AuditItem item) { }
}