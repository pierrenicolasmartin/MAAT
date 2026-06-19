// MAAT — Monitoring et Audit des Accès pour la Traçabilité
// Copyright (C) 2026  Pierre-Nicolas MARTIN
//
// This program is free software: you can redistribute it and/or modify it
// under the terms of the GNU General Public License as published by the Free
// Software Foundation, either version 3 of the License, or (at your option)
// any later version. This program is distributed WITHOUT ANY WARRANTY; see
// the GNU General Public License <https://www.gnu.org/licenses/> for details.

using System.IO.Compression;
using Microsoft.Data.Sqlite;

namespace MAAT.Storage;

/// <summary>
/// Encapsule la connexion SQLite d'un audit. Deux fabriques : une base
/// <b>temporaire</b> dans <c>%TEMP%</c> (créée pendant le scan, détruite si non
/// sauvegardée — exigence de confidentialité) et l'<b>ouverture</b> d'un fichier
/// projet <c>.maat</c> existant.
///
/// Le pooling de connexion est désactivé pour que le fichier temporaire puisse
/// être supprimé immédiatement après fermeture.
/// </summary>
public sealed class AuditDatabase : IDisposable
{
    // Fichier SQLite décompressé temporaire à supprimer à la fermeture, quand le
    // projet ouvert est un .maat compressé (gzip). Null pour une base brute.
    private string? _decompressedTemp;

    private AuditDatabase(SqliteConnection connection, string filePath, bool isTemporary)
    {
        Connection = connection;
        FilePath = filePath;
        IsTemporary = isTemporary;
    }

    /// <summary>Connexion ouverte sur le fichier.</summary>
    public SqliteConnection Connection { get; }

    /// <summary>Chemin du fichier SQLite.</summary>
    public string FilePath { get; }

    /// <summary>
    /// Vrai si le fichier sous-jacent est la base de travail temporaire (générée
    /// dans <c>%TEMP%</c>). Indépendant d'une éventuelle sauvegarde : un
    /// <c>.maat</c> sauvegardé est une copie distincte ; le fichier de travail
    /// est toujours détruit à la fermeture.
    /// </summary>
    public bool IsTemporary { get; }

    /// <summary>Crée une base temporaire vide (schéma initialisé) dans <c>%TEMP%</c>.</summary>
    public static AuditDatabase CreateTemporary()
    {
        string path = Path.Combine(Path.GetTempPath(), $"maat_{Guid.NewGuid():N}.maatdb");
        var connection = OpenConnection(path);
        var db = new AuditDatabase(connection, path, isTemporary: true);
        db.ExecuteScript(SqlSchema.CreateScript);
        return db;
    }

    /// <summary>
    /// Ouvre un fichier projet <c>.maat</c> existant en lecture. Les projets récents
    /// sont compressés (gzip) : ils sont décompressés vers un fichier SQLite temporaire
    /// (détruit à la fermeture). Les anciens <c>.maat</c> bruts (SQLite non compressé)
    /// restent ouverts directement — rétro-compatibilité.
    /// </summary>
    public static AuditDatabase Open(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Fichier projet introuvable.", path);
        }

        string fileToOpen = path;
        string? decompressedTemp = null;
        if (IsGzip(path))
        {
            decompressedTemp = Path.Combine(Path.GetTempPath(), $"maat_open_{Guid.NewGuid():N}.maatdb");
            using (var input = File.OpenRead(path))
            using (var gz = new GZipStream(input, CompressionMode.Decompress))
            using (var output = File.Create(decompressedTemp))
            {
                gz.CopyTo(output);
            }
            fileToOpen = decompressedTemp;
        }

        try
        {
            var connection = OpenConnection(fileToOpen);
            return new AuditDatabase(connection, fileToOpen, isTemporary: false)
            {
                _decompressedTemp = decompressedTemp,
            };
        }
        catch
        {
            // Échec d'ouverture après décompression : ne pas laisser fuir le temporaire.
            if (decompressedTemp is not null)
            {
                try { File.Delete(decompressedTemp); } catch { /* best-effort */ }
            }
            throw;
        }
    }

    /// <summary>Vrai si le fichier commence par la signature gzip (1F 8B).</summary>
    private static bool IsGzip(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            return fs.ReadByte() == 0x1F && fs.ReadByte() == 0x8B;
        }
        catch
        {
            return false;
        }
    }

    private static SqliteConnection OpenConnection(string path)
    {
        var csb = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Pooling = false, // autorise la suppression immédiate du fichier temporaire
        };
        var connection = new SqliteConnection(csb.ToString());
        connection.Open();
        return connection;
    }

    private void ExecuteScript(string sql)
    {
        using var cmd = Connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Demande à SQLite de relâcher la mémoire de cache qu'il peut libérer
    /// (<c>PRAGMA shrink_memory</c>), après une opération de lecture/écriture
    /// massive (export, sauvegarde) pour que la RAM redescende.
    /// </summary>
    public void ReleaseMemory()
    {
        try
        {
            using var cmd = Connection.CreateCommand();
            cmd.CommandText = "PRAGMA shrink_memory;";
            cmd.ExecuteNonQuery();
        }
        catch
        {
            // Best-effort : l'échec d'un PRAGMA de confort ne doit jamais interrompre l'app.
        }
    }

    public void Dispose()
    {
        Connection.Dispose();
        // Nettoie le fichier décompressé temporaire d'un projet compressé (best-effort).
        if (_decompressedTemp is not null)
        {
            try { if (File.Exists(_decompressedTemp)) { File.Delete(_decompressedTemp); } }
            catch { /* fichier verrouillé : nettoyé au prochain démarrage du système. */ }
            _decompressedTemp = null;
        }
    }
}