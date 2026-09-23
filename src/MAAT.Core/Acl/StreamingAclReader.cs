// MAAT — Monitoring et Audit des Accès pour la Traçabilité
// Copyright (C) 2026  Pierre-Nicolas MARTIN
//
// This program is free software: you can redistribute it and/or modify it
// under the terms of the GNU General Public License as published by the Free
// Software Foundation, either version 3 of the License, or (at your option)
// any later version. This program is distributed WITHOUT ANY WARRANTY; see
// the GNU General Public License <https://www.gnu.org/licenses/> for details.

using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using MAAT.Core.Common;
using MAAT.Core.Diagnostics;
using MAAT.Core.Scanning;

namespace MAAT.Core.Acl;

/// <summary>
/// Lecture des ACL d'un chemin, élément par élément (streaming), pour le moteur
/// <see cref="StreamingAuditEngine"/>. On lit l'instantané de sécurité puis les
/// règles d'accès en <b>SID</b> (<c>typeof(SecurityIdentifier)</c>) : la
/// canonicalisation / fusion des ACE est faite par le code .NET, mais SANS la
/// résolution LSA SID → nom par ACE — celle-ci est déléguée au
/// <see cref="SidNameResolver"/> mis en cache (une fois par identité unique).
///
/// Robustesse : nouvelle tentative sur erreur réseau transitoire ; repli par handle
/// ouvert SANS suivre le point de reparse quand celui-ci n'est pas suivable (code
/// 1920 : sockets AF_UNIX, liens d'exécution d'applications…) ; détection de la DACL
/// nulle (accès total pour tous).
///
/// Sémantique d'erreur : accès refusé et erreur générique incrémentent
/// <see cref="AclErrorCount"/> ; un chemin introuvable est seulement journalisé.
/// </summary>
internal sealed class StreamingAclReader
{
    private const int ERROR_CANT_ACCESS_FILE = 1920;
    private const ushort SE_DACL_PRESENT = 0x0004;

    private readonly IScanLog _log;
    private int _aclErrorCount;

    public StreamingAclReader(IScanLog? log = null)
    {
        _log = log ?? NullScanLog.Instance;
    }

    public int AclErrorCount => _aclErrorCount;

    /// <summary>
    /// Renvoie le descripteur de sécurité brut (auto-relatif, DACL seule) du chemin, ou
    /// <c>null</c> si l'ACL n'a pu être lue. <paramref name="nullDacl"/> signale une DACL
    /// nulle (aucune restriction). En mode <paramref name="quiet"/>, un échec n'est ni
    /// journalisé ni compté (lecture de contexte, ex. parents de la racine). Les octets
    /// servent de clé de cache : la plupart des éléments partagent un descripteur identique.
    /// </summary>
    public byte[]? TryReadDescriptor(string path, out bool nullDacl, bool quiet = false)
    {
        nullDacl = false;
        string extended = LongPath.ToExtended(path);

        // 1) Descripteur brut SANS exception (rapide sur dossiers protégés). Préfixe \\?\
        //    (lecteur) ou \\?\UNC\ (réseau) : chemins longs et noms à point/espace final.
        int status = ReadNamed(extended, out IntPtr pSd);
        for (int attempt = 1; attempt <= 2 && FastDirectoryEnumerator.IsTransient(status); attempt++)
        {
            Thread.Sleep(400 * attempt);
            status = ReadNamed(extended, out pSd);
        }
        if (status == ERROR_CANT_ACCESS_FILE)
        {
            // Point de reparse non suivable : on lit l'ACL du point de reparse lui-même.
            status = ReadReparsePoint(extended, out pSd);
        }
        if (status != NativeMethods.ERROR_SUCCESS)
        {
            if (!quiet) { RecordReadError(path, status); }
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
        nullDacl = IsNullDacl(sd);
        return sd;
    }

    /// <summary>
    /// Canonicalisation / fusion des ACE d'un descripteur par le code .NET, en mémoire et en
    /// SID — donc sans résolution LSA par ACE. Une DACL nulle y devient « Tout le monde :
    /// contrôle total », ce qui est sa sémantique exacte. Lève une exception si le
    /// descripteur est inexploitable (l'appelant journalise).
    /// </summary>
    public static AuthorizationRuleCollection ParseRules(byte[] sd, bool isDirectory)
    {
        FileSystemSecurity security = isDirectory ? new DirectorySecurity() : new FileSecurity();
        security.SetSecurityDescriptorBinaryForm(sd, AccessControlSections.Access);
        return security.GetAccessRules(true, true, typeof(SecurityIdentifier));
    }

    private static int ReadNamed(string extendedPath, out IntPtr pSd)
        => NativeMethods.GetNamedSecurityInfo(
            extendedPath, NativeMethods.SE_FILE_OBJECT, NativeMethods.DACL_SECURITY_INFORMATION,
            out _, out _, out _, out _, out pSd);

    private static int ReadReparsePoint(string extendedPath, out IntPtr pSd)
    {
        pSd = IntPtr.Zero;
        using var handle = NativeMethods.CreateFile(
            extendedPath, NativeMethods.READ_CONTROL, NativeMethods.FILE_SHARE_ALL, IntPtr.Zero,
            NativeMethods.OPEN_EXISTING,
            NativeMethods.FILE_FLAG_BACKUP_SEMANTICS | NativeMethods.FILE_FLAG_OPEN_REPARSE_POINT, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            return Marshal.GetLastWin32Error();
        }
        return NativeMethods.GetSecurityInfo(
            handle, NativeMethods.SE_FILE_OBJECT, NativeMethods.DACL_SECURITY_INFORMATION,
            out _, out _, out _, out _, out pSd);
    }

    /// <summary>
    /// DACL nulle dans un descripteur auto-relatif : DACL absente, ou présente avec un
    /// décalage nul. Sémantique Windows : aucune restriction (accès total pour tous).
    /// </summary>
    private static bool IsNullDacl(byte[] sd)
    {
        if (sd.Length < 20) { return false; }
        ushort control = BitConverter.ToUInt16(sd, 2);
        uint daclOffset = BitConverter.ToUInt32(sd, 16);
        return (control & SE_DACL_PRESENT) == 0 || daclOffset == 0;
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
                _log.Write("ACL_ERREUR", path, $"Erreur lecture ACL : {Win32Text.Describe(status)}");
                break;
        }
    }
}

/// <summary>Libellé lisible d'un code d'erreur Win32 (message système + code).</summary>
internal static class Win32Text
{
    public static string Describe(int code)
    {
        string message;
        try { message = new Win32Exception(code).Message.TrimEnd('.', ' '); }
        catch { message = string.Empty; }
        return string.IsNullOrWhiteSpace(message) ? $"code {code}" : $"{message} (code {code})";
    }
}
