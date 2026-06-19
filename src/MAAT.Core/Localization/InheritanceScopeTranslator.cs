// MAAT — Monitoring et Audit des Accès pour la Traçabilité
// Copyright (C) 2026  Pierre-Nicolas MARTIN
//
// This program is free software: you can redistribute it and/or modify it
// under the terms of the GNU General Public License as published by the Free
// Software Foundation, either version 3 of the License, or (at your option)
// any later version. This program is distributed WITHOUT ANY WARRANTY; see
// the GNU General Public License <https://www.gnu.org/licenses/> for details.

using System.Collections.Concurrent;
using System.Security.AccessControl;

namespace MAAT.Core.Localization;

/// <summary>
/// Traduit la portée d'héritage d'une ACE (combinaison
/// <see cref="InheritanceFlags"/> + <see cref="PropagationFlags"/>) en français
/// ou en anglais, comme l'onglet de sécurité avancé de Windows.
/// Portage de <c>Convert-Portee</c> (V1.0.5), rendu bilingue.
/// </summary>
public static class InheritanceScopeTranslator
{
    private static readonly ConcurrentDictionary<string, string> Cache = new();

    private static bool IsFr(string lang) => !"en".Equals(lang, System.StringComparison.OrdinalIgnoreCase);

    /// <summary>Traduit la portée dans la langue demandée (« fr » / « en »).</summary>
    public static string Translate(InheritanceFlags inheritance, PropagationFlags propagation, string lang = "fr")
    {
        bool fr = IsFr(lang);
        int iVal = (int)inheritance;
        int pVal = (int)propagation;
        string key = $"{(fr ? "fr" : "en")}|{iVal}|{pVal}";
        if (Cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        const int ci = (int)InheritanceFlags.ContainerInherit; // 1
        const int oi = (int)InheritanceFlags.ObjectInherit;    // 2
        const int inheritOnly = (int)PropagationFlags.InheritOnly; // 2

        string result;
        if (iVal == 0)
        {
            result = fr ? "Ce dossier uniquement" : "This folder only";
        }
        else if ((iVal & ci) != 0 && (iVal & oi) != 0)
        {
            result = pVal == inheritOnly
                ? (fr ? "Sous-dossiers et fichiers uniquement" : "Subfolders and files only")
                : (fr ? "Ce dossier, sous-dossiers et fichiers" : "This folder, subfolders and files");
        }
        else if ((iVal & ci) != 0)
        {
            result = pVal == inheritOnly
                ? (fr ? "Sous-dossiers uniquement" : "Subfolders only")
                : (fr ? "Ce dossier et sous-dossiers" : "This folder and subfolders");
        }
        else if ((iVal & oi) != 0)
        {
            result = pVal == inheritOnly
                ? (fr ? "Fichiers uniquement" : "Files only")
                : (fr ? "Ce dossier et fichiers" : "This folder and files");
        }
        else
        {
            result = fr ? "Ce dossier uniquement" : "This folder only";
        }

        Cache[key] = result;
        return result;
    }
}