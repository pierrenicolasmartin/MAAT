// MAAT — Monitoring et Audit des Accès pour la Traçabilité
// Copyright (C) 2026  Pierre-Nicolas MARTIN
//
// This program is free software: you can redistribute it and/or modify it
// under the terms of the GNU General Public License as published by the Free
// Software Foundation, either version 3 of the License, or (at your option)
// any later version. This program is distributed WITHOUT ANY WARRANTY; see
// the GNU General Public License <https://www.gnu.org/licenses/> for details.

using System.Collections.Concurrent;
using System.DirectoryServices.AccountManagement;
using System.DirectoryServices.ActiveDirectory;
using MAAT.Core.Common;
using MAAT.Core.Diagnostics;
using MAAT.Core.Models;

namespace MAAT.Core.ActiveDirectory;

/// <summary>
/// Résolution AD des groupes via <see cref="System.DirectoryServices.AccountManagement"/>
/// (aucune dépendance au module RSAT ActiveDirectory). Portage de
/// <c>Resolve-ADGroupMembersRecursive</c> / <c>Resolve-ADGroupMembers</c> /
/// <c>Initialize-AdResolutionCache</c> (V1.0.5) : récursion manuelle avec
/// détection de cycles (SID déjà visités) et profondeur maximale de 15 niveaux.
///
/// Hors domaine ou AD injoignable, <see cref="IsAvailable"/> vaut faux et toutes
/// les résolutions renvoient une chaîne vide (dégradation propre).
/// </summary>
public sealed class AdGroupResolver : IAdGroupResolver, IDisposable
{
    private const int MaxDepth = 15;

    private readonly IScanLog _log;
    private readonly ConcurrentDictionary<string, string> _cache =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly PrincipalContext? _context;
    private int _adErrorCount;

    public AdGroupResolver(IScanLog? log = null)
    {
        _log = log ?? NullScanLog.Instance;
        try
        {
            // Vérifie l'appartenance à un domaine de façon peu coûteuse.
            using (Domain.GetComputerDomain()) { }
            _context = new PrincipalContext(ContextType.Domain);
            IsAvailable = true;
        }
        catch (ActiveDirectoryObjectNotFoundException)
        {
            IsAvailable = false; // machine hors domaine
        }
        catch (Exception)
        {
            IsAvailable = false; // AD injoignable / erreur d'initialisation
        }
    }

    public bool IsAvailable { get; }

    /// <summary>Nombre d'erreurs AD rencontrées (timeouts, échecs de résolution).</summary>
    public int AdErrorCount => _adErrorCount;

    public string Resolve(string sam)
    {
        if (string.IsNullOrEmpty(sam))
        {
            return string.Empty;
        }
        if (_cache.TryGetValue(sam, out var cached))
        {
            return cached;
        }

        string result = string.Empty;
        if (IsAvailable && _context is not null)
        {
            try
            {
                using var group = GroupPrincipal.FindByIdentity(_context, sam);
                if (group is not null)
                {
                    var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    if (group.Sid is not null)
                    {
                        visited.Add(group.Sid.Value); // évite l'auto-référence
                    }
                    var members = ResolveRecursive(group, sam, visited, 0, viaGroup: null);
                    result = string.Join(", ", members);
                }
                // group == null → l'identité n'est pas un groupe : résultat vide (normal).
            }
            catch (PrincipalServerDownException)
            {
                Interlocked.Increment(ref _adErrorCount);
                _log.Write("AD_TIMEOUT", sam, "Serveur AD inaccessible / timeout LDAP");
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref _adErrorCount);
                _log.Write("AD_ERREUR", sam, $"Résolution AD échouée : {ex.Message}");
            }
        }

        _cache[sam] = result;
        return result;
    }

    /// <param name="viaGroup">Groupe intermédiaire par lequel ces membres sont obtenus
    /// (null pour les membres directs du groupe racine). Sert à annoter « (via X) ».</param>
    private List<string> ResolveRecursive(
        GroupPrincipal group, string groupSam, HashSet<string> visited, int depth, string? viaGroup)
    {
        var members = new List<string>();
        if (depth >= MaxDepth)
        {
            _log.Write("AD_PROFONDEUR_MAX", groupSam, $"Profondeur max ({MaxDepth}) atteinte");
            return members;
        }

        PrincipalSearchResult<Principal> direct;
        try
        {
            direct = group.GetMembers(false);
        }
        catch (PrincipalServerDownException)
        {
            Interlocked.Increment(ref _adErrorCount);
            _log.Write("AD_TIMEOUT", groupSam, "Timeout LDAP lors de l'énumération des membres");
            return members;
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _adErrorCount);
            _log.Write("AD_ERREUR", groupSam, $"Erreur GetMembers : {ex.Message}");
            return members;
        }

        using (direct)
        {
            EnumerateMembers(direct, members, groupSam, visited, depth, viaGroup);
        }
        return members;
    }

    private void EnumerateMembers(
        PrincipalSearchResult<Principal> direct, List<string> members,
        string groupSam, HashSet<string> visited, int depth, string? viaGroup)
    {
        IEnumerator<Principal> enumerator = direct.GetEnumerator();
        while (true)
        {
            Principal? member;
            try
            {
                if (!enumerator.MoveNext()) { break; }
                member = enumerator.Current;
            }
            catch (Exception ex)
            {
                // Un membre non résolvable (FSP, compte orphelin) ne doit pas
                // interrompre l'énumération des autres membres.
                Interlocked.Increment(ref _adErrorCount);
                _log.Write("AD_ERREUR", groupSam, $"Membre illisible : {ex.Message}");
                continue;
            }
            if (member is null) { continue; }

            try
            {
                string memberSid = member.Sid?.Value ?? member.SamAccountName ?? member.Name ?? string.Empty;
                if (!visited.Add(memberSid))
                {
                    _log.Write("AD_CYCLE", groupSam,
                        $"Référence circulaire détectée pour {member.SamAccountName}");
                    continue;
                }

                if (member is GroupPrincipal subGroup)
                {
                    // Le groupe intermédiaire n'est PAS listé : on descend chercher ses
                    // membres finaux, annotés « (via <ce groupe>) » pour tracer l'origine.
                    string subName = subGroup.SamAccountName ?? subGroup.Name ?? memberSid;
                    members.AddRange(ResolveRecursive(subGroup, subName, visited, depth + 1, viaGroup: subName));
                }
                else
                {
                    // Nom affiché : le nom (DisplayName, sinon Name) ; repli sur le nom
                    // SAM seulement si le nom est absent. Plus de suffixe « (sam) ».
                    string name = member.DisplayName ?? member.Name ?? string.Empty;
                    string label = !string.IsNullOrEmpty(name)
                        ? name
                        : (member.SamAccountName ?? string.Empty);
                    members.Add(Annotate(label, viaGroup));
                }
            }
            finally
            {
                member.Dispose();
            }
        }
    }

    /// <summary>Ajoute « (via &lt;groupe&gt;) » à un membre obtenu indirectement (sinon le membre tel quel).</summary>
    private static string Annotate(string label, string? viaGroup)
        => string.IsNullOrEmpty(viaGroup) ? label : $"{label} (via {viaGroup})";

    public void ApplyMembers(IEnumerable<AuditItem> items)
    {
        if (!IsAvailable)
        {
            return;
        }
        foreach (var item in items)
        {
            ApplyMembers(item);
        }
    }

    public void ApplyMembers(AuditItem item)
    {
        if (!IsAvailable)
        {
            return;
        }
        foreach (var ace in item.Acl)
        {
            if (IdentityUtils.IsSystemPrefixed(ace.Identity))
            {
                continue;
            }
            string members = Resolve(IdentityUtils.ExtractSam(ace.Identity));
            if (!string.IsNullOrEmpty(members))
            {
                ace.ResolvedMembers = members;
            }
        }
    }

    public void Dispose() => _context?.Dispose();
}