// MAAT — Monitoring et Audit des Accès pour la Traçabilité
// Copyright (C) 2026  Pierre-Nicolas MARTIN
//
// This program is free software: you can redistribute it and/or modify it
// under the terms of the GNU General Public License as published by the Free
// Software Foundation, either version 3 of the License, or (at your option)
// any later version. This program is distributed WITHOUT ANY WARRANTY; see
// the GNU General Public License <https://www.gnu.org/licenses/> for details.

using System.Reflection;
using MAAT.Core;
using MAAT.Core.Models;
using Microsoft.Data.Sqlite;

namespace MAAT.Storage;

/// <summary>
/// Puits d'audit écrivant les éléments en SQLite au fil de l'eau, par lots
/// transactionnels (validation tous les <see cref="BatchSize"/> éléments). Le
/// <c>parent_id</c> est résolu via le chemin du dossier parent (les éléments
/// arrivent dans l'ordre de l'arbre, parents avant enfants).
/// </summary>
public sealed class SqliteAuditSink : IAuditSink, IDisposable
{
    private const int BatchSize = 1000;

    private readonly AuditDatabase _db;
    private readonly SqliteConnection _conn;

    // Chemin de dossier → id de ligne fs_item (seuls les dossiers peuvent être parents).
    private readonly Dictionary<string, long> _folderIds =
        new(StringComparer.OrdinalIgnoreCase);

    private SqliteTransaction? _tx;
    private SqliteCommand? _itemCmd;
    private SqliteCommand? _aceCmd;
    private long _runId;
    private int _pending;

    public SqliteAuditSink(AuditDatabase db)
    {
        _db = db;
        _conn = db.Connection;
    }

    public void Begin(AuditParameters parameters, string rootPath)
    {
        _runId = InsertRun(parameters, rootPath);
        _tx = _conn.BeginTransaction();
        PrepareCommands();
    }

    public void Emit(AuditItem item)
    {
        long? parentId = ResolveParentId(item.FullPath);

        long itemId = InsertItem(item, parentId);
        if (!item.IsFile)
        {
            // Clé normalisée (sans antislash final) : une racine de lecteur « C:\ » et le
            // parent « C:\ » d'un enfant doivent coïncider, sinon les enfants directs de
            // la racine deviendraient orphelins (invisibles dans l'arbre).
            _folderIds[NormalizeFolderKey(item.FullPath)] = itemId;
        }

        foreach (var ace in item.Acl)
        {
            InsertAce(itemId, ace);
        }

        if (++_pending >= BatchSize)
        {
            CommitAndRenewTransaction();
            _pending = 0;
        }
    }

    public void Complete(AuditSummary summary)
    {
        _tx?.Commit();
        _tx?.Dispose();
        _tx = null;
        UpdateRunCompletion(summary);
    }

    /// <summary>Id de la ligne <c>audit_run</c> de cet audit.</summary>
    public long RunId => _runId;

    private long? ResolveParentId(string fullPath)
    {
        string? parentPath = Path.GetDirectoryName(fullPath);
        if (parentPath is not null && _folderIds.TryGetValue(NormalizeFolderKey(parentPath), out long pid))
        {
            return pid;
        }
        return null;
    }

    /// <summary>Clé de dossier robuste : antislash final retiré (« C:\ » et « C: » coïncident).</summary>
    private static string NormalizeFolderKey(string path) => path.TrimEnd('\\');

    private long InsertRun(AuditParameters p, string rootPath)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO audit_run
                (root_path, depth, audit_files, audit_droits, audit_taille,
                 started_utc, app_version, machine, user_name)
            VALUES
                ($root, $depth, $files, $droits, $taille,
                 $started, $version, $machine, $user)
            RETURNING id;
            """;
        cmd.Parameters.AddWithValue("$root", rootPath);
        cmd.Parameters.AddWithValue("$depth", p.Depth);
        cmd.Parameters.AddWithValue("$files", p.AuditFiles ? 1 : 0);
        cmd.Parameters.AddWithValue("$droits", p.AuditRights ? 1 : 0);
        cmd.Parameters.AddWithValue("$taille", p.AuditSize ? 1 : 0);
        cmd.Parameters.AddWithValue("$started", DateTimeOffset.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$version",
            Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0");
        cmd.Parameters.AddWithValue("$machine", Environment.MachineName);
        cmd.Parameters.AddWithValue("$user", Environment.UserName);
        return (long)cmd.ExecuteScalar()!;
    }

    private void PrepareCommands()
    {
        _itemCmd = _conn.CreateCommand();
        _itemCmd.Transaction = _tx;
        _itemCmd.CommandText = """
            INSERT INTO fs_item
                (run_id, parent_id, full_path, name, depth, is_file, is_reparse,
                 size_bytes, size_partial, has_deny)
            VALUES
                ($run, $parent, $path, $name, $depth, $file, $reparse,
                 $size, $partial, $deny)
            RETURNING id;
            """;
        AddParams(_itemCmd, "$run", "$parent", "$path", "$name", "$depth",
            "$file", "$reparse", "$size", "$partial", "$deny");
        _itemCmd.Prepare();

        _aceCmd = _conn.CreateCommand();
        _aceCmd.Transaction = _tx;
        _aceCmd.CommandText = """
            INSERT INTO ace
                (item_id, identity, ace_type, rights_fr, scope_fr,
                 is_inherited, source_path, resolved_members)
            VALUES
                ($item, $id, $type, $rights, $scope, $inh, $src, $members);
            """;
        AddParams(_aceCmd, "$item", "$id", "$type", "$rights", "$scope",
            "$inh", "$src", "$members");
        _aceCmd.Prepare();
    }

    private long InsertItem(AuditItem item, long? parentId)
    {
        var c = _itemCmd!;
        c.Parameters["$run"].Value = _runId;
        c.Parameters["$parent"].Value = (object?)parentId ?? DBNull.Value;
        c.Parameters["$path"].Value = item.FullPath;
        c.Parameters["$name"].Value = item.Name;
        c.Parameters["$depth"].Value = item.Depth;
        c.Parameters["$file"].Value = item.IsFile ? 1 : 0;
        c.Parameters["$reparse"].Value = item.IsReparse ? 1 : 0;
        c.Parameters["$size"].Value = (object?)item.SizeBytes ?? DBNull.Value;
        c.Parameters["$partial"].Value = item.SizePartial ? 1 : 0;
        c.Parameters["$deny"].Value = item.HasDeny ? 1 : 0;
        return (long)c.ExecuteScalar()!;
    }

    private void InsertAce(long itemId, AceEntry ace)
    {
        var c = _aceCmd!;
        c.Parameters["$item"].Value = itemId;
        c.Parameters["$id"].Value = ace.Identity;
        c.Parameters["$type"].Value = (int)ace.Type;
        c.Parameters["$rights"].Value = ace.RightsFr;
        c.Parameters["$scope"].Value = ace.ScopeFr;
        c.Parameters["$inh"].Value = ace.IsInherited ? 1 : 0;
        c.Parameters["$src"].Value = ace.SourcePath;
        c.Parameters["$members"].Value = (object?)ace.ResolvedMembers ?? DBNull.Value;
        c.ExecuteNonQuery();
    }

    private void CommitAndRenewTransaction()
    {
        _tx!.Commit();
        _tx.Dispose();
        _tx = _conn.BeginTransaction();
        _itemCmd!.Transaction = _tx;
        _aceCmd!.Transaction = _tx;
    }

    private void UpdateRunCompletion(AuditSummary s)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            UPDATE audit_run SET
                finished_utc = $finished, folder_count = $folders, file_count = $files,
                item_count = $items, ace_total = $aces, reparse_count = $reparse,
                acl_errors = $aclErr, ad_errors = $adErr, ad_resolved = $adRes,
                ad_available = $adAvail, elapsed_ms = $elapsed
            WHERE id = $run;
            """;
        cmd.Parameters.AddWithValue("$finished", DateTimeOffset.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$folders", s.FolderCount);
        cmd.Parameters.AddWithValue("$files", s.FileCount);
        cmd.Parameters.AddWithValue("$items", s.ItemCount);
        cmd.Parameters.AddWithValue("$aces", s.AceTotal);
        cmd.Parameters.AddWithValue("$reparse", s.ReparseCount);
        cmd.Parameters.AddWithValue("$aclErr", s.AclErrorCount);
        cmd.Parameters.AddWithValue("$adErr", s.AdErrorCount);
        cmd.Parameters.AddWithValue("$adRes", s.AdResolved);
        cmd.Parameters.AddWithValue("$adAvail", s.AdAvailable ? 1 : 0);
        cmd.Parameters.AddWithValue("$elapsed", (long)s.Elapsed.TotalMilliseconds);
        cmd.Parameters.AddWithValue("$run", _runId);
        cmd.ExecuteNonQuery();
    }

    private static void AddParams(SqliteCommand cmd, params string[] names)
    {
        foreach (var n in names)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = n;
            cmd.Parameters.Add(p);
        }
    }

    public void Dispose()
    {
        _itemCmd?.Dispose();
        _aceCmd?.Dispose();
        _tx?.Dispose();
    }
}