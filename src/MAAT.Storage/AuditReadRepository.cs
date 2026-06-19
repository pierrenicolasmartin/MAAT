// MAAT — Monitoring et Audit des Accès pour la Traçabilité
// Copyright (C) 2026  Pierre-Nicolas MARTIN
//
// This program is free software: you can redistribute it and/or modify it
// under the terms of the GNU General Public License as published by the Free
// Software Foundation, either version 3 of the License, or (at your option)
// any later version. This program is distributed WITHOUT ANY WARRANTY; see
// the GNU General Public License <https://www.gnu.org/licenses/> for details.

using MAAT.Core.Models;
using Microsoft.Data.Sqlite;

namespace MAAT.Storage;

/// <summary>
/// Lectures de la base d'audit pour l'interface. Conçue pour un chargement
/// <b>paresseux et paginé</b> de l'arbre : on ne charge les enfants d'un nœud
/// qu'au moment où il est déplié (essentiel pour de très gros volumes).
/// </summary>
public sealed class AuditReadRepository
{
    private readonly SqliteConnection _conn;

    public AuditReadRepository(AuditDatabase db) => _conn = db.Connection;

    /// <summary>Dernier audit enregistré dans la base (ou null si aucun).</summary>
    public AuditRunRow? GetLatestRun()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM audit_run ORDER BY id DESC LIMIT 1;";
        using var r = cmd.ExecuteReader();
        return r.Read() ? MapRun(r) : null;
    }

    /// <summary>Élément racine de l'audit (parent_id NULL).</summary>
    public FsItemRow? GetRoot(long runId)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText =
            "SELECT * FROM fs_item WHERE run_id = $run AND parent_id IS NULL ORDER BY id LIMIT 1;";
        cmd.Parameters.AddWithValue("$run", runId);
        using var r = cmd.ExecuteReader();
        return r.Read() ? MapItem(r) : null;
    }

    /// <summary>
    /// Enfants directs d'un nœud (dossiers d'abord, puis fichiers, par nom).
    /// Pagination optionnelle via <paramref name="offset"/> / <paramref name="limit"/>.
    /// </summary>
    public IReadOnlyList<FsItemRow> GetChildren(long parentId, int offset = 0, int limit = int.MaxValue)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT * FROM fs_item
            WHERE parent_id = $parent
            ORDER BY is_file, name COLLATE NOCASE
            LIMIT $limit OFFSET $offset;
            """;
        cmd.Parameters.AddWithValue("$parent", parentId);
        cmd.Parameters.AddWithValue("$limit", limit);
        cmd.Parameters.AddWithValue("$offset", offset);

        var list = new List<FsItemRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(MapItem(r));
        }
        return list;
    }

    /// <summary>Nombre d'enfants directs d'un nœud (pour la virtualisation).</summary>
    public int CountChildren(long parentId)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM fs_item WHERE parent_id = $parent;";
        cmd.Parameters.AddWithValue("$parent", parentId);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    /// <summary>
    /// Recherche à plat : éléments dont le nom ou le chemin contient le terme.
    /// Dossiers d'abord, limité (pagination simple) pour rester réactif.
    /// </summary>
    public IReadOnlyList<FsItemRow> Search(long runId, string term, int limit = 500)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT * FROM fs_item
            WHERE run_id = $run AND (name LIKE $term OR full_path LIKE $term)
            ORDER BY is_file, name COLLATE NOCASE
            LIMIT $limit;
            """;
        cmd.Parameters.AddWithValue("$run", runId);
        cmd.Parameters.AddWithValue("$term", "%" + term + "%");
        cmd.Parameters.AddWithValue("$limit", limit);

        var list = new List<FsItemRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(MapItem(r));
        }
        return list;
    }

    /// <summary>
    /// Éléments sur lesquels une identité possède un accès EFFECTIF : une ACE
    /// directe à son nom, OU une ACE d'un groupe dont elle est membre (via les
    /// membres AD résolus, repérés par le SAM). Résultat à plat, limité.
    /// </summary>
    public IReadOnlyList<FsItemRow> GetItemsForAccess(long runId, string identity, string sam, int limit = 1000)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT DISTINCT f.* FROM fs_item f
            JOIN ace a ON a.item_id = f.id
            WHERE f.run_id = $run AND (
                a.identity = $idn
                OR a.identity IN (
                    SELECT ax.identity FROM ace ax
                    JOIN fs_item fx ON ax.item_id = fx.id
                    WHERE fx.run_id = $run AND ax.resolved_members LIKE $member
                )
            )
            ORDER BY f.is_file, f.full_path COLLATE NOCASE
            LIMIT $limit;
            """;
        cmd.Parameters.AddWithValue("$run", runId);
        cmd.Parameters.AddWithValue("$idn", identity);
        cmd.Parameters.AddWithValue("$member", "%(" + sam + ")%"); // membres au format « Nom (sam) »
        cmd.Parameters.AddWithValue("$limit", limit);

        var list = new List<FsItemRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(MapItem(r));
        }
        return list;
    }

    /// <summary>ACE d'un élément (refus d'abord, puis par identité).</summary>
    public IReadOnlyList<AceRow> GetAces(long itemId)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT identity, ace_type, rights_fr, scope_fr, is_inherited, source_path, resolved_members
            FROM ace WHERE item_id = $item
            ORDER BY ace_type DESC, identity COLLATE NOCASE;
            """;
        cmd.Parameters.AddWithValue("$item", itemId);

        var list = new List<AceRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new AceRow(
                r.GetString(0), r.GetInt32(1), r.GetString(2), r.GetString(3),
                r.GetInt32(4) != 0, r.GetString(5), r.IsDBNull(6) ? null : r.GetString(6)));
        }
        return list;
    }

    /// <summary>
    /// Énumère <b>paresseusement</b> tous les éléments d'un audit (avec leurs ACL),
    /// dans l'ordre de l'arbre (id = insertion). Un seul élément vit en mémoire à la
    /// fois : essentiel pour streamer les exports CSV/HTML sans matérialiser des
    /// millions d'<see cref="AuditItem"/> (ancienne cause de pics RAM non libérés).
    ///
    /// Jointure externe items→ace triée par (item, ace) : on agrège les ACE d'un même
    /// élément puis on le rend dès que l'identifiant change.
    /// </summary>
    public IEnumerable<AuditItem> EnumerateAuditItems(long runId)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT i.id, i.full_path, i.name, i.depth, i.is_file, i.is_reparse,
                   i.size_bytes, i.size_partial, i.has_deny,
                   a.identity, a.ace_type, a.rights_fr, a.scope_fr,
                   a.is_inherited, a.source_path, a.resolved_members
            FROM fs_item i
            LEFT JOIN ace a ON a.item_id = i.id
            WHERE i.run_id = $run
            ORDER BY i.id, a.id;
            """;
        cmd.Parameters.AddWithValue("$run", runId);

        using var r = cmd.ExecuteReader();
        AuditItem? current = null;
        long currentId = -1;
        while (r.Read())
        {
            long id = r.GetInt64(0);
            if (current is null || id != currentId)
            {
                if (current is not null) { yield return current; }
                currentId = id;
                current = new AuditItem
                {
                    FullPath = r.GetString(1),
                    Name = r.GetString(2),
                    Depth = r.GetInt32(3),
                    IsFile = r.GetInt32(4) != 0,
                    IsReparse = r.GetInt32(5) != 0,
                    SizeBytes = r.IsDBNull(6) ? null : r.GetInt64(6),
                    SizePartial = r.GetInt32(7) != 0,
                    HasDeny = r.GetInt32(8) != 0,
                };
            }
            if (!r.IsDBNull(9)) // l'élément a au moins une ACE sur cette ligne
            {
                current.Acl.Add(new AceEntry
                {
                    Identity = r.GetString(9),
                    Type = (AceType)r.GetInt32(10),
                    RightsFr = r.GetString(11),
                    ScopeFr = r.GetString(12),
                    IsInherited = r.GetInt32(13) != 0,
                    SourcePath = r.GetString(14),
                    ResolvedMembers = r.IsDBNull(15) ? null : r.GetString(15),
                });
            }
        }
        if (current is not null) { yield return current; }
    }

    /// <summary>Profondeur maximale des éléments (pour dimensionner les colonnes Niveau_n du CSV).</summary>
    public int GetMaxDepth(long runId)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT COALESCE(MAX(depth), 0) FROM fs_item WHERE run_id = $run;";
        cmd.Parameters.AddWithValue("$run", runId);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    /// <summary>
    /// Éléments portant au moins une ACE explicite (inherited=false) ou héritée
    /// (inherited=true). Liste à plat pour le filtre de type de droits.
    /// </summary>
    public IReadOnlyList<FsItemRow> GetItemsByAclKind(long runId, bool inherited, int limit = 1000)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT DISTINCT f.* FROM fs_item f
            JOIN ace a ON a.item_id = f.id
            WHERE f.run_id = $run AND a.is_inherited = $inh
            ORDER BY f.is_file, f.full_path COLLATE NOCASE
            LIMIT $limit;
            """;
        cmd.Parameters.AddWithValue("$run", runId);
        cmd.Parameters.AddWithValue("$inh", inherited ? 1 : 0);
        cmd.Parameters.AddWithValue("$limit", limit);

        var list = new List<FsItemRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(MapItem(r));
        }
        return list;
    }

    /// <summary>Nombre d'identités distinctes de l'audit (pour le badge).</summary>
    public int CountIdentities(long runId)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(DISTINCT a.identity)
            FROM ace a JOIN fs_item f ON a.item_id = f.id
            WHERE f.run_id = $run;
            """;
        cmd.Parameters.AddWithValue("$run", runId);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    /// <summary>
    /// Identités de l'audit, agrégées : membres résolus (si groupe AD) et nombre
    /// d'éléments concernés. Pour le panneau Identités et le filtre d'accès.
    /// </summary>
    public IReadOnlyList<IdentityRow> GetIdentities(long runId)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT a.identity, MAX(a.resolved_members) AS members, COUNT(DISTINCT a.item_id) AS items
            FROM ace a JOIN fs_item f ON a.item_id = f.id
            WHERE f.run_id = $run
            GROUP BY a.identity
            ORDER BY a.identity COLLATE NOCASE;
            """;
        cmd.Parameters.AddWithValue("$run", runId);

        var list = new List<IdentityRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new IdentityRow(
                r.GetString(0),
                r.IsDBNull(1) ? null : r.GetString(1),
                r.GetInt32(2)));
        }
        return list;
    }

    private static AuditRunRow MapRun(SqliteDataReader r)
    {
        int O(string n) => r.GetOrdinal(n);
        int I(string n) => r.IsDBNull(O(n)) ? 0 : r.GetInt32(O(n));
        return new AuditRunRow(
            r.GetInt64(O("id")),
            r.GetString(O("root_path")),
            I("depth"),
            I("audit_files") != 0,
            I("audit_droits") != 0,
            I("audit_taille") != 0,
            DateTimeOffset.Parse(r.GetString(O("started_utc"))),
            r.IsDBNull(O("finished_utc")) ? null : DateTimeOffset.Parse(r.GetString(O("finished_utc"))),
            I("folder_count"), I("file_count"), I("item_count"), I("ace_total"),
            I("reparse_count"), I("acl_errors"), I("ad_errors"),
            I("ad_available") != 0,
            r.IsDBNull(O("elapsed_ms")) ? 0 : r.GetInt64(O("elapsed_ms")));
    }

    private static FsItemRow MapItem(SqliteDataReader r)
    {
        int O(string n) => r.GetOrdinal(n);
        return new FsItemRow(
            r.GetInt64(O("id")),
            r.IsDBNull(O("parent_id")) ? null : r.GetInt64(O("parent_id")),
            r.GetString(O("full_path")),
            r.GetString(O("name")),
            r.GetInt32(O("depth")),
            r.GetInt32(O("is_file")) != 0,
            r.GetInt32(O("is_reparse")) != 0,
            r.IsDBNull(O("size_bytes")) ? null : r.GetInt64(O("size_bytes")),
            r.GetInt32(O("size_partial")) != 0,
            r.GetInt32(O("has_deny")) != 0);
    }
}