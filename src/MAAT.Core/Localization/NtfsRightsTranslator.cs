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
/// Traduit les droits NTFS (<see cref="FileSystemRights"/>) en français ou en
/// anglais. Portage de <c>Convert-DroitFR</c> (V1.0.5) rendu bilingue : la langue
/// est figée au moment du scan. « Synchronize » est omis (bruit technique).
/// Évolutif : ajouter une langue = ajouter une colonne dans <see cref="Map"/>.
/// </summary>
public static class NtfsRightsTranslator
{
    private static readonly ConcurrentDictionary<string, string> Cache = new();

    // Clé d'énumération → (français, anglais). Null = droit ignoré (Synchronize).
    private static readonly Dictionary<string, (string? Fr, string? En)> Map = new(StringComparer.Ordinal)
    {
        ["FullControl"]                  = ("Contrôle total", "Full control"),
        ["Modify"]                       = ("Modification", "Modify"),
        ["ReadAndExecute"]               = ("Lecture et exécution", "Read & execute"),
        ["Read"]                         = ("Lecture", "Read"),
        ["Write"]                        = ("Écriture", "Write"),
        ["ReadData"]                     = ("Lecture des données", "Read data"),
        ["ListDirectory"]                = ("Listage du dossier", "List folder"),
        ["WriteData"]                    = ("Écriture des données", "Write data"),
        ["CreateFiles"]                  = ("Création de fichiers", "Create files"),
        ["AppendData"]                   = ("Ajout de données", "Append data"),
        ["CreateDirectories"]            = ("Création de dossiers", "Create folders"),
        ["ExecuteFile"]                  = ("Exécution", "Execute"),
        ["Delete"]                       = ("Suppression", "Delete"),
        ["DeleteSubdirectoriesAndFiles"] = ("Suppression sous-dossiers/fichiers", "Delete subfolders/files"),
        ["ReadAttributes"]               = ("Lecture des attributs", "Read attributes"),
        ["WriteAttributes"]              = ("Écriture des attributs", "Write attributes"),
        ["ReadExtendedAttributes"]       = ("Lecture attributs étendus", "Read extended attributes"),
        ["WriteExtendedAttributes"]      = ("Écriture attributs étendus", "Write extended attributes"),
        ["ChangePermissions"]            = ("Modification des autorisations", "Change permissions"),
        ["TakeOwnership"]                = ("Prise de possession", "Take ownership"),
        ["ReadPermissions"]              = ("Lecture des autorisations", "Read permissions"),
        ["Synchronize"]                  = (null, null),
    };

    private static bool IsFr(string lang) => !"en".Equals(lang, StringComparison.OrdinalIgnoreCase);

    /// <summary>Traduit un ensemble de droits NTFS dans la langue demandée (« fr » / « en »).</summary>
    public static string Translate(FileSystemRights rights, string lang = "fr")
    {
        bool fr = IsFr(lang);
        int numVal = (int)rights;
        string cacheKey = (fr ? "fr|" : "en|") + numVal;
        if (Cache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        // --- Droits génériques (GENERIC_*) : bits hauts non mappés par FileSystemRights ---
        const uint GenericAll = 0x10000000;
        const uint GenericExecute = 0x20000000;
        const uint GenericWrite = 0x40000000;
        const uint GenericRead = 0x80000000;

        uint mask = unchecked((uint)numVal);
        var translated = new List<string>(4);

        if ((mask & 0xF0000000) != 0)
        {
            if ((mask & GenericAll) != 0) { translated.Add(fr ? "Contrôle total (générique)" : "Full control (generic)"); }
            if ((mask & GenericRead) != 0) { translated.Add(fr ? "Lecture (générique)" : "Read (generic)"); }
            if ((mask & GenericWrite) != 0) { translated.Add(fr ? "Écriture (générique)" : "Write (generic)"); }
            if ((mask & GenericExecute) != 0) { translated.Add(fr ? "Exécution (générique)" : "Execute (generic)"); }
            mask &= 0x0FFFFFFF;
        }

        string special = fr ? "Droits spéciaux" : "Special permissions";

        if (mask != 0)
        {
            string rightsStr = ((FileSystemRights)mask).ToString();
            foreach (var raw in rightsStr.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                string p = raw.Trim();
                if (Map.TryGetValue(p, out var v))
                {
                    string? label = fr ? v.Fr : v.En;
                    if (!string.IsNullOrEmpty(label)) { translated.Add(label); }
                }
                else if (int.TryParse(p, out _))
                {
                    translated.Add(special);
                }
                else
                {
                    translated.Add(p);
                }
            }
        }

        string result = string.Join(", ", translated.Distinct());
        if (string.IsNullOrWhiteSpace(result))
        {
            result = special;
        }

        Cache[cacheKey] = result;
        return result;
    }
}