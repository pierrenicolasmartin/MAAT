// MAAT — Monitoring et Audit des Accès pour la Traçabilité
// Copyright (C) 2026  Pierre-Nicolas MARTIN
//
// This program is free software: you can redistribute it and/or modify it
// under the terms of the GNU General Public License as published by the Free
// Software Foundation, either version 3 of the License, or (at your option)
// any later version. This program is distributed WITHOUT ANY WARRANTY; see
// the GNU General Public License <https://www.gnu.org/licenses/> for details.

using System.Text;
using MAAT.Core;
using MAAT.Core.Localization;
using MAAT.Core.Models;

namespace MAAT.Export;

/// <summary>
/// Export CSV « fichier » : une colonne <c>Dossier</c>, l'arborescence décomposée
/// en colonnes <c>Niveau_1..N</c>, puis la taille et/ou les colonnes de droits
/// (une ligne par ACE). Portage des en-têtes et du format de lignes de
/// <c>New-CsvAndHtmlData</c> (V1.0.5). Encodage UTF-8 avec BOM, délimiteur « ; ».
/// </summary>
public sealed class CsvExporter
{
    private readonly string _delimiter;
    private readonly string _lang;

    public CsvExporter(string lang = "fr", string delimiter = ";")
    {
        _lang = lang;
        _delimiter = delimiter;
    }

    private string L(string key) => ExportStrings.T(_lang, key);

    /// <summary>
    /// Écrit le CSV dans un fichier (UTF-8 BOM) <b>en streaming</b> : les éléments
    /// sont consommés un à un depuis <paramref name="items"/> (jamais matérialisés en
    /// liste). <paramref name="maxLevels"/> (profondeur max + 1) est fourni par
    /// l'appelant pour dimensionner les colonnes Niveau_n sans pré-passe en mémoire.
    /// </summary>
    public void ExportToFile(string path, IEnumerable<AuditItem> items, AuditParameters p, string rootPath, int maxLevels, CancellationToken ct = default)
    {
        using var writer = new StreamWriter(path, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        Export(writer, items, p, rootPath, maxLevels, ct);
    }

    /// <summary>Écrit le CSV dans un <see cref="TextWriter"/> quelconque, en streaming.</summary>
    public void Export(TextWriter writer, IEnumerable<AuditItem> items, AuditParameters p, string rootPath, int maxLevels, CancellationToken ct = default)
    {
        string rootName = rootPath.TrimEnd('\\').Split('\\')[^1];
        int rootLen = rootPath.Length;
        if (maxLevels < 1) { maxLevels = 1; }

        writer.WriteLine(BuildHeader(p, maxLevels));

        var sb = new StringBuilder(512);
        foreach (var item in items)
        {
            ct.ThrowIfCancellationRequested();
            string[] levels = RelativeParts(item.FullPath, rootPath, rootLen, rootName);
            string pathField = Quote(item.FullPath);
            string levelsCsv = BuildLevels(levels, maxLevels);
            string sizeField = p.AuditSize ? Quote(SizeFormatter.Format(item.SizeBytes, item.SizePartial, _lang)) : null!;

            if (!p.AuditRights)
            {
                // Mode Taille uniquement : Dossier + Niveaux + Taille
                sb.Clear();
                sb.Append(pathField).Append(_delimiter)
                  .Append(levelsCsv).Append(_delimiter)
                  .Append(sizeField);
                writer.WriteLine(sb.ToString());
                continue;
            }

            // Mode Droits : une ligne par ACE
            foreach (var ace in item.Acl)
            {
                sb.Clear();
                sb.Append(pathField).Append(_delimiter)
                  .Append(levelsCsv).Append(_delimiter);
                if (p.AuditSize)
                {
                    sb.Append(sizeField).Append(_delimiter);
                }
                sb.Append(Quote(ace.Identity)).Append(_delimiter)
                  .Append(Quote(ace.ResolvedMembers ?? string.Empty)).Append(_delimiter)
                  .Append(Quote(L(ace.Type == AceType.Deny ? "Deny" : "Allow"))).Append(_delimiter)
                  .Append(Quote(ace.RightsFr)).Append(_delimiter)
                  .Append(Quote(ace.ScopeFr)).Append(_delimiter)
                  .Append(Quote(L(ace.IsInherited ? "Yes" : "No"))).Append(_delimiter)
                  .Append(Quote(ace.SourcePath));
                writer.WriteLine(sb.ToString());
            }
        }
    }

    private string BuildHeader(AuditParameters p, int maxLevels)
    {
        // Numérotation alignée sur la sélection de profondeur : la racine est le
        // niveau 0 (« racine seule »), ses enfants directs le niveau 1, etc.
        string lvl = L("Col_Level");
        var niveaux = string.Join(_delimiter, Enumerable.Range(0, maxLevels).Select(i => $"{lvl}_{i}"));
        string folder = L("Col_Folder"), size = L("Col_Size");
        string rightsCols = $"{L("Col_Identity")}{_delimiter}{L("Col_Members")}{_delimiter}{L("Col_Type")}{_delimiter}{L("Col_Rights")}{_delimiter}{L("Col_Scope")}{_delimiter}{L("Col_Inherited")}{_delimiter}{L("Col_Source")}";
        if (p.AuditRights && p.AuditSize)
        {
            return $"{folder}{_delimiter}{niveaux}{_delimiter}{size}{_delimiter}{rightsCols}";
        }
        if (p.AuditRights)
        {
            return $"{folder}{_delimiter}{niveaux}{_delimiter}{rightsCols}";
        }
        return $"{folder}{_delimiter}{niveaux}{_delimiter}{size}";
    }

    private string BuildLevels(string[] levels, int maxLevels)
    {
        var sb = new StringBuilder(maxLevels * 16);
        for (int i = 0; i < maxLevels; i++)
        {
            if (i > 0) { sb.Append(_delimiter); }
            sb.Append('"');
            if (i < levels.Length)
            {
                sb.Append(levels[i].Replace("\"", "\"\""));
            }
            sb.Append('"');
        }
        return sb.ToString();
    }

    private static string[] RelativeParts(string fullPath, string rootPath, int rootLen, string rootName)
    {
        string relPath = fullPath.Length > rootLen
            ? fullPath[rootLen..].TrimStart('\\')
            : string.Empty;
        return string.IsNullOrEmpty(relPath)
            ? new[] { rootName }
            : new[] { rootName }.Concat(relPath.Split('\\')).ToArray();
    }

    private static string Quote(string value)
        => "\"" + (value ?? string.Empty).Replace("\"", "\"\"") + "\"";
}