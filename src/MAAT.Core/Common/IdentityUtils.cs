// MAAT — Monitoring et Audit des Accès pour la Traçabilité
// Copyright (C) 2026  Pierre-Nicolas MARTIN
//
// This program is free software: you can redistribute it and/or modify it
// under the terms of the GNU General Public License as published by the Free
// Software Foundation, either version 3 of the License, or (at your option)
// any later version. This program is distributed WITHOUT ANY WARRANTY; see
// the GNU General Public License <https://www.gnu.org/licenses/> for details.

using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;

namespace MAAT.Core.Common;

/// <summary>
/// Utilitaires sur les identités NTFS (« DOMAINE\compte »).
/// Reprend le filtrage et l'extraction du nom SAM réalisés par
/// <c>Initialize-AdResolutionCache</c> dans le script.
/// </summary>
public static partial class IdentityUtils
{
    [GeneratedRegex(@"^(NT AUTHORITY|AUTORITE NT|BUILTIN|NT SERVICE)\\",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SystemPrefixRegex();

    /// <summary>
    /// Vrai si l'identité porte un préfixe de compte système / intégré
    /// (NT AUTHORITY, AUTORITE NT, BUILTIN, NT SERVICE) — inutile à résoudre en AD.
    /// </summary>
    public static bool IsSystemPrefixed(string identity)
        => !string.IsNullOrEmpty(identity) && SystemPrefixRegex().IsMatch(identity);

    // Préfixes d'autorité locale / intégrée (EN + FR). Stockés NORMALISÉS
    // (majuscules, sans accents ni apostrophes) via Norm() — robuste aux
    // variantes d'encodage (É composé/précomposé, ' vs ').
    private static readonly HashSet<string> LocalAuthorities = Normalized(
        "BUILTIN", "INTEGRE",
        "NT AUTHORITY", "AUTORITE NT",
        "NT SERVICE", "SERVICE NT",
        "NT VIRTUAL MACHINE",
        "APPLICATION PACKAGE AUTHORITY", "AUTORITE DE PACKAGE D'APPLICATION",
        "CONSOLE LOGON", "OUVERTURE DE SESSION CONSOLE",
        "WINDOW MANAGER", "FONT DRIVER HOST", "IIS APPPOOL");

    // Comptes connus sans préfixe de domaine (donc non rattachés à l'AD), normalisés.
    private static readonly HashSet<string> WellKnownNoDomain = Normalized(
        "TOUT LE MONDE", "EVERYONE",
        "CREATEUR PROPRIETAIRE", "CREATOR OWNER",
        "GROUPE CREATEUR PROPRIETAIRE", "CREATOR GROUP",
        "ANONYMOUS LOGON", "OUVERTURE DE SESSION ANONYME",
        "ALL APPLICATION PACKAGES", "TOUS LES PACKAGES D'APPLICATION",
        "ALL RESTRICTED APPLICATION PACKAGES", "RESTRICTED",
        "AUTHENTICATED USERS", "UTILISATEURS AUTHENTIFIES");

    private static HashSet<string> Normalized(params string[] values)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var v in values) { set.Add(Norm(v)); }
        return set;
    }

    /// <summary>
    /// Forme canonique d'une chaîne pour comparaison : sans espaces de bord,
    /// majuscules invariantes, accents supprimés et apostrophes (' ' ` ) retirées.
    /// « AUTORITÉ DE PACKAGE D'APPLICATION » → « AUTORITE DE PACKAGE DAPPLICATION ».
    /// </summary>
    private static string Norm(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) { return string.Empty; }
        var sb = new StringBuilder(s.Length);
        foreach (char c in s.Trim().Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) { continue; }
            if (c is '\'' or '’' or '‘' or '`') { continue; }
            sb.Append(char.ToUpperInvariant(c));
        }
        return sb.ToString();
    }

    /// <summary>
    /// Vrai si l'identité désigne un compte / groupe du <b>domaine Active
    /// Directory</b> : une identité <c>PRÉFIXE\nom</c> dont le préfixe n'est ni
    /// une autorité locale/intégrée connue ni le nom de la machine d'audit.
    /// Les comptes locaux, intégrés et connus (Tout le monde…) renvoient faux.
    /// </summary>
    public static bool IsDomainAccount(string identity, string machineName)
    {
        if (string.IsNullOrWhiteSpace(identity)) { return false; }

        int sep = identity.IndexOf('\\');
        if (sep < 0)
        {
            return false; // pas de préfixe : compte connu (non-domaine) ou SID brut
        }

        string prefix = Norm(identity[..sep]);
        if (LocalAuthorities.Contains(prefix)) { return false; }
        if (!string.IsNullOrEmpty(machineName) && prefix == Norm(machineName))
        {
            return false;
        }
        return !WellKnownNoDomain.Contains(Norm(identity));
    }

    /// <summary>
    /// Classification par <b>SID</b> (indépendante de la langue du système audité) :
    /// vrai si le SID désigne un compte/groupe d'un domaine Active Directory, c.-à-d.
    /// un SID de compte <c>S-1-5-21-…</c> dont le domaine n'est ni intégré (BUILTIN
    /// <c>S-1-5-32</c>), ni un identifiant connu (Everyone, SYSTEM… → AccountDomainSid
    /// nul), ni le SID de la machine locale d'audit.
    /// </summary>
    public static bool IsDomainSid(SecurityIdentifier sid, SecurityIdentifier? machineSid)
    {
        var domain = sid.AccountDomainSid;
        if (domain is null)
        {
            return false; // SID bien connu (Everyone, NT AUTHORITY, app packages…)
        }
        if (!domain.Value.StartsWith("S-1-5-21", StringComparison.Ordinal))
        {
            return false; // BUILTIN (S-1-5-32) ou autre autorité non-domaine
        }
        if (machineSid is not null && domain.Equals(machineSid))
        {
            return false; // compte LOCAL de la machine
        }
        return true;
    }

    /// <summary>
    /// SID du <b>domaine de comptes LOCAL</b> de la machine (préfixe des comptes du
    /// SAM local), via LSA (<c>PolicyAccountDomainInformation</c>). Sert à écarter les
    /// comptes locaux du filtre « domaine uniquement ».
    ///
    /// <para><b>Important</b> : on n'utilise PAS le SID de domaine de l'utilisateur
    /// courant — sur une machine jointe à un domaine, l'utilisateur est un compte de
    /// domaine, donc ce SID serait celui du DOMAINE, et tous les comptes/groupes AD
    /// seraient à tort classés « locaux » puis filtrés. LSA donne le SID propre à la
    /// machine, distinct de celui du domaine.</para>
    /// </summary>
    public static SecurityIdentifier? GetLocalMachineSid()
    {
        var localSid = QueryLocalAccountDomainSid();
        if (localSid is null)
        {
            return null;
        }

        // Garde-fou décisif : si le « domaine de comptes local » égale le domaine de
        // l'utilisateur courant, on ne peut PAS distinguer local et domaine par SID —
        // c'est le cas sur un <b>contrôleur de domaine</b> (le domaine de comptes local
        // EST le domaine AD) ou hors-domaine. On renvoie alors null pour ne JAMAIS
        // filtrer à tort les comptes/groupes AD (mieux vaut montrer quelques comptes
        // locaux en trop que de masquer tout l'AD).
        try
        {
            using var id = WindowsIdentity.GetCurrent();
            var userDomain = id.User?.AccountDomainSid;
            if (userDomain is not null && localSid.Equals(userDomain))
            {
                return null;
            }
        }
        catch
        {
            // Indéterminable : on conserve le SID local (filtrage best-effort).
        }
        return localSid;
    }

    /// <summary>SID du domaine de comptes LOCAL via LSA (<c>PolicyAccountDomainInformation</c>), ou null.</summary>
    private static SecurityIdentifier? QueryLocalAccountDomainSid()
    {
        IntPtr policyHandle = IntPtr.Zero;
        IntPtr infoBuffer = IntPtr.Zero;
        try
        {
            var attr = new LSA_OBJECT_ATTRIBUTES { Length = Marshal.SizeOf<LSA_OBJECT_ATTRIBUTES>() };
            if (LsaOpenPolicy(IntPtr.Zero, ref attr, POLICY_VIEW_LOCAL_INFORMATION, out policyHandle) != 0)
            {
                return null;
            }
            if (LsaQueryInformationPolicy(policyHandle, PolicyAccountDomainInformation, out infoBuffer) != 0
                || infoBuffer == IntPtr.Zero)
            {
                return null;
            }
            var info = Marshal.PtrToStructure<POLICY_ACCOUNT_DOMAIN_INFO>(infoBuffer);
            return info.DomainSid != IntPtr.Zero ? new SecurityIdentifier(info.DomainSid) : null;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (infoBuffer != IntPtr.Zero) { LsaFreeMemory(infoBuffer); }
            if (policyHandle != IntPtr.Zero) { LsaClose(policyHandle); }
        }
    }

    // --- LSA : lecture du SID du domaine de comptes local (sans privilège particulier) ---
    private const int PolicyAccountDomainInformation = 5;
    private const uint POLICY_VIEW_LOCAL_INFORMATION = 0x0000_0001;

    [StructLayout(LayoutKind.Sequential)]
    private struct LSA_OBJECT_ATTRIBUTES
    {
        public int Length;
        public IntPtr RootDirectory;
        public IntPtr ObjectName;
        public uint Attributes;
        public IntPtr SecurityDescriptor;
        public IntPtr SecurityQualityOfService;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LSA_UNICODE_STRING
    {
        public ushort Length;
        public ushort MaximumLength;
        public IntPtr Buffer;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POLICY_ACCOUNT_DOMAIN_INFO
    {
        public LSA_UNICODE_STRING DomainName;
        public IntPtr DomainSid;
    }

    [DllImport("advapi32.dll")]
    private static extern uint LsaOpenPolicy(
        IntPtr systemName, ref LSA_OBJECT_ATTRIBUTES objectAttributes, uint desiredAccess, out IntPtr policyHandle);

    [DllImport("advapi32.dll")]
    private static extern uint LsaQueryInformationPolicy(IntPtr policyHandle, int informationClass, out IntPtr buffer);

    [DllImport("advapi32.dll")]
    private static extern uint LsaFreeMemory(IntPtr buffer);

    [DllImport("advapi32.dll")]
    private static extern uint LsaClose(IntPtr objectHandle);

    /// <summary>
    /// Extrait le nom SAM (dernier segment après « \ ») d'une identité.
    /// « DOMAINE\GroupeRH » → « GroupeRH » ; « GroupeRH » → « GroupeRH ».
    /// </summary>
    public static string ExtractSam(string identity)
    {
        if (string.IsNullOrEmpty(identity))
        {
            return string.Empty;
        }
        int idx = identity.LastIndexOf('\\');
        return idx >= 0 ? identity[(idx + 1)..] : identity;
    }
}