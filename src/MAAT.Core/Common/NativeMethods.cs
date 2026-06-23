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
/// P/Invoke Win32 d'énumération de répertoire à faible coût (FindFirstFileEx) :
/// une seule passe noyau fournit nom, attributs, taille et reparse tag, sans
/// allouer un <c>DirectoryInfo</c>/<c>FileInfo</c> par élément.
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

    private const int FindExInfoBasic = 1;      // n'alloue pas cAlternateFileName (plus rapide)
    private const int FindExSearchNameMatch = 0;
    private const int FIND_FIRST_EX_LARGE_FETCH = 0x2;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct WIN32_FIND_DATA
    {
        public uint dwFileAttributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME ftCreationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME ftLastAccessTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME ftLastWriteTime;
        public uint nFileSizeHigh;
        public uint nFileSizeLow;
        public uint dwReserved0; // reparse tag si FILE_ATTRIBUTE_REPARSE_POINT
        public uint dwReserved1;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string cFileName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)] public string cAlternateFileName;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr FindFirstFileEx(
        string lpFileName,
        int fInfoLevelId,
        out WIN32_FIND_DATA lpFindFileData,
        int fSearchOp,
        IntPtr lpSearchFilter,
        int dwAdditionalFlags);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool FindNextFile(IntPtr hFindFile, out WIN32_FIND_DATA lpFindFileData);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool FindClose(IntPtr hFindFile);

    /// <summary>Ouvre une recherche « répertoire\* » optimisée (info basique + large fetch).</summary>
    public static IntPtr FindFirst(string searchPattern, out WIN32_FIND_DATA data)
        => FindFirstFileEx(searchPattern, FindExInfoBasic, out data,
            FindExSearchNameMatch, IntPtr.Zero, FIND_FIRST_EX_LARGE_FETCH);
}
