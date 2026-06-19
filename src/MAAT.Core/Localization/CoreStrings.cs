// MAAT — Monitoring et Audit des Accès pour la Traçabilité
// Copyright (C) 2026  Pierre-Nicolas MARTIN
//
// This program is free software: you can redistribute it and/or modify it
// under the terms of the GNU General Public License as published by the Free
// Software Foundation, either version 3 of the License, or (at your option)
// any later version. This program is distributed WITHOUT ANY WARRANTY; see
// the GNU General Public License <https://www.gnu.org/licenses/> for details.

using System.Collections.Generic;

namespace MAAT.Core.Localization;

/// <summary>
/// Libellés localisés du moteur (messages de progression émis pendant l'audit).
/// L'appelant fournit un code de langue (« fr » / « en »). Évolutif : ajouter
/// un dictionnaire et l'enregistrer dans <see cref="Catalog"/>.
/// </summary>
public static class CoreStrings
{
    private static IReadOnlyDictionary<string, string> Catalog(string code) => code switch
    {
        "fr" => Fr,
        _ => En,
    };

    public static string T(string code, string key, params object[] args)
    {
        var dict = Catalog(code);
        string fmt = dict.TryGetValue(key, out var v) ? v
            : (En.TryGetValue(key, out var e) ? e : key);
        return args.Length == 0 ? fmt : string.Format(fmt, args);
    }

    private static readonly IReadOnlyDictionary<string, string> Fr = new Dictionary<string, string>
    {
        ["Prog_FoldersDiscovered"] = "{0} dossiers découverts…",
        ["Prog_Folders"] = "{0} dossiers",
        ["Prog_FilesFound"] = "{0} fichiers trouvés ({1} / {2} dossiers)",
        ["Prog_Files"] = "{0} fichiers",
        ["Prog_SizeEnum"] = "Énumération : {0} dossiers…",
        ["Prog_SizeRead"] = "Lecture fichiers : {0} / {1} dossiers",
        ["Prog_SizeAggr"] = "Agrégation : {0} / {1} dossiers",
        ["Prog_ItemsProcessed"] = "{0} / {1} éléments",
    };

    private static readonly IReadOnlyDictionary<string, string> En = new Dictionary<string, string>
    {
        ["Prog_FoldersDiscovered"] = "{0} folders discovered…",
        ["Prog_Folders"] = "{0} folders",
        ["Prog_FilesFound"] = "{0} files found ({1} / {2} folders)",
        ["Prog_Files"] = "{0} files",
        ["Prog_SizeEnum"] = "Enumerating: {0} folders…",
        ["Prog_SizeRead"] = "Reading files: {0} / {1} folders",
        ["Prog_SizeAggr"] = "Aggregating: {0} / {1} folders",
        ["Prog_ItemsProcessed"] = "{0} / {1} items",
    };
}
