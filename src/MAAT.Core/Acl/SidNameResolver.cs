// MAAT — Monitoring et Audit des Accès pour la Traçabilité
// Copyright (C) 2026  Pierre-Nicolas MARTIN
//
// This program is free software: you can redistribute it and/or modify it
// under the terms of the GNU General Public License as published by the Free
// Software Foundation, either version 3 of the License, or (at your option)
// any later version. This program is distributed WITHOUT ANY WARRANTY; see
// the GNU General Public License <https://www.gnu.org/licenses/> for details.

using System.Collections.Concurrent;
using System.Security.Principal;

namespace MAAT.Core.Acl;

/// <summary>
/// Traduction SID → nom de compte (<c>DOMAINE\compte</c>) avec mise en cache.
/// C'est le levier de performance majeur : un partage ne contient qu'une poignée
/// d'identités distinctes répétées des millions de fois ; chacune n'est résolue
/// (LookupAccountSid / LSA) qu'<b>une seule fois</b>. En cas d'échec (compte
/// orphelin), on renvoie la forme <c>S-1-5-…</c> — exactement ce que produisait
/// <c>GetAccessRules(…, typeof(NTAccount))</c>.
/// </summary>
public sealed class SidNameResolver
{
    private readonly ConcurrentDictionary<SecurityIdentifier, string> _cache = new();

    /// <summary>Nom traduit du SID (valeur <c>NTAccount</c>), ou sa forme SDDL si non résolu.</summary>
    public string Resolve(SecurityIdentifier sid)
    {
        if (_cache.TryGetValue(sid, out var name))
        {
            return name;
        }
        string resolved;
        try
        {
            resolved = ((NTAccount)sid.Translate(typeof(NTAccount))).Value;
        }
        catch
        {
            resolved = sid.Value; // SID non mappable : on conserve la forme brute.
        }
        _cache[sid] = resolved;
        return resolved;
    }
}
