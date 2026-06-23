// MAAT — Monitoring et Audit des Accès pour la Traçabilité
// Copyright (C) 2026  Pierre-Nicolas MARTIN
//
// This program is free software: you can redistribute it and/or modify it
// under the terms of the GNU General Public License as published by the Free
// Software Foundation, either version 3 of the License, or (at your option)
// any later version. This program is distributed WITHOUT ANY WARRANTY; see
// the GNU General Public License <https://www.gnu.org/licenses/> for details.

namespace MAAT.Storage;

/// <summary>
/// Schéma SQLite du fichier d'audit (base temporaire et fichier projet <c>.maat</c>).
/// Une ligne <c>audit_run</c>, un arbre <c>fs_item</c> (parent_id), les ACE
/// rattachées, et le journal <c>scan_log</c>.
/// </summary>
internal static class SqlSchema
{
    public const int Version = 1;

    public const string CreateScript = """
        PRAGMA synchronous = NORMAL;

        CREATE TABLE schema_info (
            version INTEGER NOT NULL
        );
        INSERT INTO schema_info(version) VALUES (1);

        CREATE TABLE audit_run (
            id            INTEGER PRIMARY KEY,
            root_path     TEXT    NOT NULL,
            depth         INTEGER NOT NULL,
            audit_files   INTEGER NOT NULL,
            audit_droits  INTEGER NOT NULL,
            audit_taille  INTEGER NOT NULL,
            started_utc   TEXT    NOT NULL,
            finished_utc  TEXT,
            app_version   TEXT,
            machine       TEXT,
            user_name     TEXT,
            folder_count  INTEGER,
            file_count    INTEGER,
            item_count    INTEGER,
            ace_total     INTEGER,
            reparse_count INTEGER,
            acl_errors    INTEGER,
            ad_errors     INTEGER,
            ad_resolved   INTEGER,
            ad_available  INTEGER,
            elapsed_ms    INTEGER
        );

        CREATE TABLE fs_item (
            id           INTEGER PRIMARY KEY,
            run_id       INTEGER NOT NULL,
            parent_id    INTEGER,
            full_path    TEXT    NOT NULL,
            name         TEXT    NOT NULL,
            depth        INTEGER NOT NULL,
            is_file      INTEGER NOT NULL,
            is_reparse   INTEGER NOT NULL,
            size_bytes   INTEGER,
            size_partial INTEGER NOT NULL,
            has_deny     INTEGER NOT NULL
        );
        CREATE INDEX ix_fs_item_parent ON fs_item(parent_id);
        CREATE INDEX ix_fs_item_run    ON fs_item(run_id);

        CREATE TABLE ace (
            id               INTEGER PRIMARY KEY,
            item_id          INTEGER NOT NULL,
            identity         TEXT    NOT NULL,
            ace_type         INTEGER NOT NULL,
            rights_fr        TEXT    NOT NULL,
            scope_fr         TEXT    NOT NULL,
            is_inherited     INTEGER NOT NULL,
            source_path      TEXT    NOT NULL,
            resolved_members TEXT
        );
        CREATE INDEX ix_ace_item ON ace(item_id);

        CREATE TABLE scan_log (
            id       INTEGER PRIMARY KEY,
            ts       TEXT NOT NULL,
            severity TEXT NOT NULL,
            type     TEXT NOT NULL,
            path     TEXT NOT NULL,
            message  TEXT
        );
        """;
}