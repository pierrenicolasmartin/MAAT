// MAAT — Monitoring et Audit des Accès pour la Traçabilité
// Copyright (C) 2026  Pierre-Nicolas MARTIN
//
// This program is free software: you can redistribute it and/or modify it
// under the terms of the GNU General Public License as published by the Free
// Software Foundation, either version 3 of the License, or (at your option)
// any later version. This program is distributed WITHOUT ANY WARRANTY; see
// the GNU General Public License <https://www.gnu.org/licenses/> for details.

using System.Runtime.InteropServices;

namespace MAAT.Core.Common;

/// <summary>
/// P/Invoke Win32 / NT du moteur : lecture du descripteur de sécurité sans exception,
/// listage de répertoire par handle (<c>NtQueryDirectoryFile</c>), identité de fichier,
/// indicateurs de partage réseau.
/// </summary>
internal static class NativeMethods
{
    public const int ERROR_SUCCESS = 0;
    public const int ERROR_FILE_NOT_FOUND = 2;
    public const int ERROR_PATH_NOT_FOUND = 3;
    public const int ERROR_ACCESS_DENIED = 5;

    // --- Lecture du descripteur de sécurité brut (advapi32), sans exception ---
    public const int SE_FILE_OBJECT = 1;
    public const uint DACL_SECURITY_INFORMATION = 0x0000_0004;

    /// <summary>
    /// Récupère le descripteur de sécurité d'un objet nommé. Renvoie un code
    /// d'erreur Win32 (0 = succès) <b>sans lever d'exception</b> sur accès refusé,
    /// contrairement à <c>GetAccessControl</c> — décisif sur des arbres comportant
    /// de nombreux dossiers protégés. Le descripteur renvoyé doit être libéré via
    /// <see cref="LocalFree"/>.
    /// </summary>
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetNamedSecurityInfoW")]
    public static extern int GetNamedSecurityInfo(
        string objectName, int objectType, uint securityInfo,
        out IntPtr ppSidOwner, out IntPtr ppSidGroup,
        out IntPtr ppDacl, out IntPtr ppSacl, out IntPtr ppSecurityDescriptor);

    [DllImport("advapi32.dll")]
    public static extern uint GetSecurityDescriptorLength(IntPtr pSecurityDescriptor);

    [DllImport("kernel32.dll")]
    public static extern IntPtr LocalFree(IntPtr hMem);

    public const uint FILE_ATTRIBUTE_DIRECTORY = 0x0000_0010;
    public const uint FILE_ATTRIBUTE_REPARSE_POINT = 0x0000_0400;
    // Bit « name surrogate » du reparse tag (jonctions / liens symboliques).
    public const uint IO_REPARSE_TAG_NAME_SURROGATE_BIT = 0x2000_0000;

    // ─────────── Listage de répertoire par handle (NtQueryDirectoryFile) ───────────

    public const uint FILE_LIST_DIRECTORY = 0x0000_0001;
    public const uint READ_CONTROL = 0x0002_0000;
    public const uint SYNCHRONIZE = 0x0010_0000;
    public const uint FILE_SHARE_ALL = 0x0000_0007; // lecture | écriture | suppression
    public const uint OPEN_EXISTING = 3;
    public const uint FILE_FLAG_BACKUP_SEMANTICS = 0x0200_0000;
    public const uint FILE_FLAG_OPEN_REPARSE_POINT = 0x0020_0000;

    /// <summary>Classe d'information : FILE_ID_FULL_DIR_INFORMATION (données de listage + identifiant de fichier).</summary>
    public const int FileIdFullDirectoryInformation = 38;
    public const int STATUS_NO_MORE_FILES = unchecked((int)0x8000_0006);
    public const int STATUS_NO_SUCH_FILE = unchecked((int)0xC000_000F);

    /// <summary>Tag de reparse d'un lien DFS (dossier d'espace de noms redirigé vers sa cible).</summary>
    public const uint IO_REPARSE_TAG_DFS = 0x8000_000A;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateFileW")]
    public static extern Microsoft.Win32.SafeHandles.SafeFileHandle CreateFile(
        string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes,
        uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [StructLayout(LayoutKind.Sequential)]
    public struct IO_STATUS_BLOCK
    {
        public IntPtr Status;
        public UIntPtr Information;
    }

    [DllImport("ntdll.dll")]
    public static extern int NtQueryDirectoryFile(
        Microsoft.Win32.SafeHandles.SafeFileHandle fileHandle, IntPtr eventHandle, IntPtr apcRoutine, IntPtr apcContext,
        out IO_STATUS_BLOCK ioStatusBlock, IntPtr fileInformation, uint length, int fileInformationClass,
        [MarshalAs(UnmanagedType.U1)] bool returnSingleEntry, IntPtr fileName,
        [MarshalAs(UnmanagedType.U1)] bool restartScan);

    [DllImport("ntdll.dll")]
    public static extern int RtlNtStatusToDosError(int status);

    [StructLayout(LayoutKind.Sequential)]
    public struct BY_HANDLE_FILE_INFORMATION
    {
        public uint dwFileAttributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME ftCreationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME ftLastAccessTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME ftLastWriteTime;
        public uint dwVolumeSerialNumber;
        public uint nFileSizeHigh;
        public uint nFileSizeLow;
        public uint nNumberOfLinks;
        public uint nFileIndexHigh;
        public uint nFileIndexLow;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetFileInformationByHandle(
        Microsoft.Win32.SafeHandles.SafeFileHandle hFile, out BY_HANDLE_FILE_INFORMATION info);

    /// <summary>Descripteur de sécurité par handle (repli pour un point de reparse ouvert sans être suivi).</summary>
    [DllImport("advapi32.dll")]
    public static extern int GetSecurityInfo(
        Microsoft.Win32.SafeHandles.SafeFileHandle handle, int objectType, uint securityInfo,
        out IntPtr ppSidOwner, out IntPtr ppSidGroup, out IntPtr ppDacl, out IntPtr ppSacl,
        out IntPtr ppSecurityDescriptor);

    // ─────────── Partage réseau : indicateurs (énumération basée sur l'accès) ───────────

    public const uint SHI1005_FLAGS_ACCESS_BASED_DIRECTORY_ENUM = 0x0000_0800;

    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    public static extern int NetShareGetInfo(string serverName, string netName, int level, out IntPtr bufPtr);

    [DllImport("netapi32.dll")]
    public static extern int NetApiBufferFree(IntPtr buffer);
}
