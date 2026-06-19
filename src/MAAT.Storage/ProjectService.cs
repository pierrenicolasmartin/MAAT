// MAAT — Monitoring et Audit des Accès pour la Traçabilité
// Copyright (C) 2026  Pierre-Nicolas MARTIN
//
// This program is free software: you can redistribute it and/or modify it
// under the terms of the GNU General Public License as published by the Free
// Software Foundation, either version 3 of the License, or (at your option)
// any later version. This program is distributed WITHOUT ANY WARRANTY; see
// the GNU General Public License <https://www.gnu.org/licenses/> for details.

using System.IO.Compression;

namespace MAAT.Storage;

/// <summary>
/// Gestion du cycle de vie du fichier projet <c>.maat</c>.
///
/// Sauvegarde : <c>VACUUM INTO</c> écrit une copie propre et autonome de la base
/// (compactée), qui est ensuite <b>compressée en gzip</b> dans le <c>.maat</c> — les
/// bases SQLite (chemins et libellés très répétitifs) se compressent fortement, d'où
/// un fichier projet bien plus léger. L'ouverture détecte et décompresse
/// automatiquement (voir <see cref="AuditDatabase.Open"/>), les anciens <c>.maat</c>
/// bruts restant lisibles. Si l'utilisateur ne sauvegarde pas, la base temporaire est
/// <b>supprimée</b> à la fermeture (confidentialité des données auditées).
/// </summary>
public static class ProjectService
{
    /// <summary>Extension du fichier projet MAAT.</summary>
    public const string ProjectExtension = ".maat";

    /// <summary>
    /// Sauvegarde la base courante vers <paramref name="targetPath"/> (fichier
    /// <c>.maat</c>) : copie compactée (<c>VACUUM INTO</c>) puis compression gzip. La
    /// base de travail (temporaire) reste utilisable et sera détruite à la fermeture ;
    /// le <c>.maat</c> sauvegardé persiste indépendamment.
    /// </summary>
    public static void SaveAs(AuditDatabase db, string targetPath)
    {
        // 1) Copie SQLite propre et compacte vers un fichier temporaire.
        string tempSqlite = Path.Combine(Path.GetTempPath(), $"maat_save_{Guid.NewGuid():N}.maatdb");
        try
        {
            using (var cmd = db.Connection.CreateCommand())
            {
                cmd.CommandText = "VACUUM main INTO $target;";
                cmd.Parameters.AddWithValue("$target", tempSqlite);
                cmd.ExecuteNonQuery();
            }

            // 2) Compression gzip vers le .maat final.
            if (File.Exists(targetPath))
            {
                File.Delete(targetPath);
            }
            using (var input = File.OpenRead(tempSqlite))
            using (var output = File.Create(targetPath))
            using (var gz = new GZipStream(output, CompressionLevel.Optimal))
            {
                input.CopyTo(gz);
            }
        }
        finally
        {
            TryDelete(tempSqlite);
        }
    }

    /// <summary>
    /// Ferme et supprime le fichier de la base si elle est temporaire (non
    /// sauvegardée). Sans effet sur une base ouverte depuis un projet existant.
    /// </summary>
    public static void DiscardIfTemporary(AuditDatabase db)
    {
        bool temporary = db.IsTemporary;
        string path = db.FilePath;
        db.Dispose();

        if (temporary)
        {
            TryDelete(path);
            TryDelete(path + "-journal");
            TryDelete(path + "-wal");
            TryDelete(path + "-shm");
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Suppression best-effort : un fichier verrouillé sera nettoyé au prochain démarrage.
        }
    }
}