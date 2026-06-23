// MAAT — Monitoring et Audit des Accès pour la Traçabilité
// Copyright (C) 2026  Pierre-Nicolas MARTIN
//
// This program is free software: you can redistribute it and/or modify it
// under the terms of the GNU General Public License as published by the Free
// Software Foundation, either version 3 of the License, or (at your option)
// any later version. This program is distributed WITHOUT ANY WARRANTY; see
// the GNU General Public License <https://www.gnu.org/licenses/> for details.

using MAAT.Core.Diagnostics;

namespace MAAT.Storage;

/// <summary>
/// Persiste les entrées de journal accumulées (<see cref="BufferingScanLog"/>)
/// dans la table <c>scan_log</c>, en une transaction. Équivaut au fichier
/// <c>.log.csv</c> du script.
/// </summary>
public static class ScanLogWriter
{
    public static int Write(AuditDatabase db, IEnumerable<ScanLogEntry> entries)
    {
        var conn = db.Connection;
        using var tx = conn.BeginTransaction();
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT INTO scan_log (ts, severity, type, path, message)
            VALUES ($ts, $sev, $type, $path, $msg);
            """;
        var ts = cmd.CreateParameter();   ts.ParameterName = "$ts";   cmd.Parameters.Add(ts);
        var sev = cmd.CreateParameter();  sev.ParameterName = "$sev";  cmd.Parameters.Add(sev);
        var type = cmd.CreateParameter(); type.ParameterName = "$type"; cmd.Parameters.Add(type);
        var path = cmd.CreateParameter(); path.ParameterName = "$path"; cmd.Parameters.Add(path);
        var msg = cmd.CreateParameter();  msg.ParameterName = "$msg";  cmd.Parameters.Add(msg);
        cmd.Prepare();

        int count = 0;
        foreach (var e in entries)
        {
            ts.Value = e.Timestamp.ToString("O");
            sev.Value = e.Severity == LogSeverity.Error ? "ERROR" : "WARN";
            type.Value = e.Type;
            path.Value = e.Path;
            msg.Value = e.Message;
            cmd.ExecuteNonQuery();
            count++;
        }

        tx.Commit();
        return count;
    }
}