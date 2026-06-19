// MAAT — Monitoring et Audit des Accès pour la Traçabilité
// Copyright (C) 2026  Pierre-Nicolas MARTIN
//
// This program is free software: you can redistribute it and/or modify it
// under the terms of the GNU General Public License as published by the Free
// Software Foundation, either version 3 of the License, or (at your option)
// any later version. This program is distributed WITHOUT ANY WARRANTY; see
// the GNU General Public License <https://www.gnu.org/licenses/> for details.

namespace MAAT.Storage;

/// <summary>Ligne de synthèse d'un audit (table <c>audit_run</c>).</summary>
public sealed record AuditRunRow(
    long Id,
    string RootPath,
    int Depth,
    bool AuditFiles,
    bool AuditRights,
    bool AuditSize,
    DateTimeOffset StartedUtc,
    DateTimeOffset? FinishedUtc,
    int FolderCount,
    int FileCount,
    int ItemCount,
    int AceTotal,
    int ReparseCount,
    int AclErrors,
    int AdErrors,
    bool AdAvailable,
    long ElapsedMs);

/// <summary>Ligne d'un élément audité (table <c>fs_item</c>), pour l'arbre UI.</summary>
public sealed record FsItemRow(
    long Id,
    long? ParentId,
    string FullPath,
    string Name,
    int Depth,
    bool IsFile,
    bool IsReparse,
    long? SizeBytes,
    bool SizePartial,
    bool HasDeny);

/// <summary>Identité agrégée sur tout l'audit (pour le panneau Identités).</summary>
public sealed record IdentityRow(
    string Identity,
    string? Members,
    int ItemCount);

/// <summary>Ligne d'ACE (table <c>ace</c>), pour le panneau de droits.</summary>
public sealed record AceRow(
    string Identity,
    int AceType,
    string RightsFr,
    string ScopeFr,
    bool IsInherited,
    string SourcePath,
    string? ResolvedMembers);