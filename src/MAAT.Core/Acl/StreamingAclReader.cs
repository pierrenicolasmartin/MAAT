// MAAT — Monitoring et Audit des Accès pour la Traçabilité
// Copyright (C) 2026  Pierre-Nicolas MARTIN
//
// This program is free software: you can redistribute it and/or modify it
// under the terms of the GNU General Public License as published by the Free
// Software Foundation, either version 3 of the License, or (at your option)
// any later version. This program is distributed WITHOUT ANY WARRANTY; see
// the GNU General Public License <https://www.gnu.org/licenses/> for details.

using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using MAAT.Core.Common;
using MAAT.Core.Diagnostics;

namespace MAAT.Core.Acl;

/// <summary>
/// Lecture des ACL d'un chemin, élément par élément (streaming), pour le moteur
/// <see cref="StreamingAuditEngine"/>. On lit l'instantané de sécurité puis les
/// règles d'accès en <b>SID</b> (<c>typeof(SecurityIdentifier)</c>) : la
/// canonicalisation / fusion des ACE est faite par le code .NET, mais SANS la
/// résolution LSA SID → nom par ACE — celle-ci est déléguée au
/// <see cref="SidNameResolver"/> mis en cache (une fois par identité unique).
/// C'est le levier de performance, à fiabilité inchangée.
///
/// Sémantique d'erreur : accès refusé et erreur générique incrémentent
/// <see cref="AclErrorCount"/> ; un chemin introuvable est seulement journalisé ;
/// un échec de lecture des règles est journalisé et l'élément est ignoré (non émis).
/// </summary>
internal sealed class StreamingAclReader
{
    private readonly IScanLog _log;
    private int _aclErrorCount;

    public StreamingAclReader(IScanLog? log = null)
    {
        _log = log ?? NullScanLog.Instance;
    }

    public int AclErrorCount => _aclErrorCount;

    /// <summary>
    /// Renvoie les règles d'accès (en SID) du chemin, ou <c>null</c> si l'ACL n'a
    /// pu être lue (l'élément ne sera alors pas émis). <paramref name="isDirectory"/>
    /// évite un appel système de test de type (déjà connu via l'énumération).
    /// </summary>
    public AuthorizationRuleCollection? TryReadRules(string path, bool isDirectory)
    {
        // 1) Lecture du descripteur brut SANS exception (rapide sur dossiers protégés).
        //    Préfixe \\?\ pour les chemins longs (>260) : sinon GetNamedSecurityInfo
        //    échoue alors que l'énumération (préfixée) a trouvé l'élément → l'item
        //    serait omis (régression). Cohérent avec FastDirectoryEnumerator.
        int status = NativeMethods.GetNamedSecurityInfo(
            LongPath.ToExtended(path), NativeMethods.SE_FILE_OBJECT, NativeMethods.DACL_SECURITY_INFORMATION,
            out _, out _, out _, out _, out IntPtr pSd);
        if (status != NativeMethods.ERROR_SUCCESS)
        {
            RecordReadError(path, status);
            return null;
        }

        byte[] sd;
        try
        {
            int len = (int)NativeMethods.GetSecurityDescriptorLength(pSd);
            sd = new byte[len];
            Marshal.Copy(pSd, sd, 0, len);
        }
        finally
        {
            NativeMethods.LocalFree(pSd);
        }

        // 2) Canonicalisation / fusion des ACE par le code .NET (identique à l'ancien
        //    moteur), mais en mémoire et en SID — donc sans résolution LSA par ACE.
        try
        {
            FileSystemSecurity security = isDirectory ? new DirectorySecurity() : new FileSecurity();
            security.SetSecurityDescriptorBinaryForm(sd, AccessControlSections.Access);
            return security.GetAccessRules(true, true, typeof(SecurityIdentifier));
        }
        catch (Exception ex)
        {
            _log.Write("ACL_ACCESS_ERREUR", path,
                $"Impossible de lire les règles d'accès : {ex.Message}");
            return null;
        }
    }

    private void RecordReadError(string path, int status)
    {
        switch (status)
        {
            case NativeMethods.ERROR_ACCESS_DENIED:
                Interlocked.Increment(ref _aclErrorCount);
                _log.Write("ACL_ACCES_REFUSE", path, "Accès refusé lors de la lecture ACL");
                break;
            case NativeMethods.ERROR_FILE_NOT_FOUND:
            case NativeMethods.ERROR_PATH_NOT_FOUND:
                _log.Write("ACL_CHEMIN_INTROUVABLE", path, "Chemin introuvable lors de la lecture ACL");
                break;
            default:
                Interlocked.Increment(ref _aclErrorCount);
                _log.Write("ACL_ERREUR", path, $"Erreur lecture ACL (code {status})");
                break;
        }
    }
}
