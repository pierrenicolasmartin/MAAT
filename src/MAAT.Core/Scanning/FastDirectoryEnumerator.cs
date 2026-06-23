// MAAT — Monitoring et Audit des Accès pour la Traçabilité
// Copyright (C) 2026  Pierre-Nicolas MARTIN
//
// This program is free software: you can redistribute it and/or modify it
// under the terms of the GNU General Public License as published by the Free
// Software Foundation, either version 3 of the License, or (at your option)
// any later version. This program is distributed WITHOUT ANY WARRANTY; see
// the GNU General Public License <https://www.gnu.org/licenses/> for details.

using System.Runtime.InteropServices;
using MAAT.Core.Common;

namespace MAAT.Core.Scanning;

/// <summary>Une entrée immédiate d'un répertoire (issue de WIN32_FIND_DATA, sans appel système additionnel).</summary>
internal readonly struct FsEntry
{
    public FsEntry(string fullPath, string name, bool isDirectory, bool isNameSurrogate, bool isReparse, long size)
    {
        FullPath = fullPath;
        Name = name;
        IsDirectory = isDirectory;
        IsNameSurrogate = isNameSurrogate;
        IsReparse = isReparse;
        Size = size;
    }

    public string FullPath { get; }
    public string Name { get; }
    public bool IsDirectory { get; }
    /// <summary>Jonction / lien symbolique : à NE PAS parcourir ni compter (anti-boucle, anti-doublon).</summary>
    public bool IsNameSurrogate { get; }
    /// <summary>Point de reparse quelconque (inclut les espaces cloud OneDrive, eux à parcourir).</summary>
    public bool IsReparse { get; }
    /// <summary>Taille logique du fichier (0 pour un répertoire).</summary>
    public long Size { get; }
}

/// <summary>
/// Énumération native (FindFirstFileEx/FindNextFile) des entrées immédiates d'un
/// répertoire. Une seule passe noyau fournit nom, attributs, taille et reparse
/// tag — sans allouer un <c>DirectoryInfo</c>/<c>FileInfo</c> par élément.
/// </summary>
internal static class FastDirectoryEnumerator
{
    /// <summary>
    /// Renvoie les entrées immédiates de <paramref name="directory"/> (hors « . » et « .. »).
    /// <paramref name="error"/> reçoit le code Win32 d'ouverture : 0 si succès, sinon
    /// la liste est vide et l'appelant journalise / marque l'échec.
    /// </summary>
    public static List<FsEntry> List(string directory, out int error)
    {
        var entries = new List<FsEntry>();
        error = NativeMethods.ERROR_SUCCESS;

        string pattern = BuildSearchPattern(directory);
        IntPtr handle = NativeMethods.FindFirst(pattern, out var data);
        if (handle == new IntPtr(-1))
        {
            error = Marshal.GetLastWin32Error();
            return entries;
        }

        try
        {
            do
            {
                string name = data.cFileName;
                if (name is "." or "..")
                {
                    continue;
                }

                bool isDir = (data.dwFileAttributes & NativeMethods.FILE_ATTRIBUTE_DIRECTORY) != 0;
                bool isReparse = (data.dwFileAttributes & NativeMethods.FILE_ATTRIBUTE_REPARSE_POINT) != 0;
                bool isNameSurrogate = isReparse
                    && (data.dwReserved0 & NativeMethods.IO_REPARSE_TAG_NAME_SURROGATE_BIT) != 0;
                long size = isDir ? 0L : ((long)data.nFileSizeHigh << 32) | data.nFileSizeLow;

                string full = Combine(directory, name);
                entries.Add(new FsEntry(full, name, isDir, isNameSurrogate, isReparse, size));
            }
            while (NativeMethods.FindNextFile(handle, out data));
        }
        finally
        {
            NativeMethods.FindClose(handle);
        }

        return entries;
    }

    // Motif « répertoire\* », avec préfixe long-chemin pour les chemins de lecteur
    // (parité avec DirectoryInfo qui gère les chemins > MAX_PATH en local).
    private static string BuildSearchPattern(string directory)
    {
        string baseDir = directory.EndsWith('\\') ? directory : directory + '\\';
        return LongPath.ToExtended(baseDir) + '*';
    }

    private static string Combine(string directory, string name)
        => directory.EndsWith('\\') ? directory + name : directory + '\\' + name;
}
