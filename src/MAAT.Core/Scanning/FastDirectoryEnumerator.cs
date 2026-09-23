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

/// <summary>Une entrée immédiate d'un répertoire (issue du listage noyau, sans appel système additionnel).</summary>
internal readonly struct FsEntry
{
    public FsEntry(string fullPath, string name, bool isDirectory, bool isNameSurrogate, bool isReparse,
        uint reparseTag, long size, long fileId)
    {
        FullPath = fullPath;
        Name = name;
        IsDirectory = isDirectory;
        IsNameSurrogate = isNameSurrogate;
        IsReparse = isReparse;
        ReparseTag = reparseTag;
        Size = size;
        FileId = fileId;
    }

    public string FullPath { get; }
    public string Name { get; }
    public bool IsDirectory { get; }
    /// <summary>Jonction / lien symbolique : à NE PAS parcourir ni compter (anti-boucle, anti-doublon).</summary>
    public bool IsNameSurrogate { get; }
    /// <summary>Point de reparse quelconque (inclut les liens DFS et espaces cloud, eux parcourus).</summary>
    public bool IsReparse { get; }
    /// <summary>Tag de reparse (0 si ce n'est pas un point de reparse).</summary>
    public uint ReparseTag { get; }
    /// <summary>Taille logique du fichier (0 pour un répertoire).</summary>
    public long Size { get; }
    /// <summary>Identifiant de fichier sur son volume (0 ou -1 si inconnu : FAT, certains serveurs).</summary>
    public long FileId { get; }

    /// <summary>Vrai pour un lien d'espace de noms DFS.</summary>
    public bool IsDfsLink => IsReparse && ReparseTag == NativeMethods.IO_REPARSE_TAG_DFS;
}

/// <summary>
/// Identité d'un répertoire : (numéro de série du volume, identifiant de fichier).
/// Sert à détecter les boucles de traversée, y compris celles invisibles par le
/// chemin (lien DFS vers un espace de noms parent, lien symbolique suivi côté
/// serveur sur un NAS Linux…), là où le simple test « point de reparse » ne suffit pas.
/// </summary>
internal readonly record struct DirIdentity(uint Volume, long FileId)
{
    /// <summary>Faux si le système de fichiers ne fournit pas d'identifiant stable (FAT, ReFS 128 bits…).</summary>
    public bool IsKnown => FileId != 0 && FileId != -1;

    /// <summary>Identité d'un enfant du même volume, d'après l'identifiant fourni par le listage du parent.</summary>
    public DirIdentity Child(long fileId) => new(Volume, fileId);
}

/// <summary>
/// Énumération native des entrées immédiates d'un répertoire par handle
/// (<c>NtQueryDirectoryFile</c>, classe <c>FileIdFullDirectoryInformation</c>) : une seule
/// passe noyau fournit nom, attributs, taille, tag de reparse <b>et identifiant de
/// fichier</b> — exactement les données de <c>FindFirstFileEx</c>, plus l'identifiant qui
/// permet la détection des boucles. Les erreurs survenant EN COURS de listage (coupure
/// réseau…) sont remontées, au lieu de tronquer silencieusement la liste.
/// </summary>
internal static class FastDirectoryEnumerator
{
    private const int BufferSize = 64 * 1024;

    // Offsets de FILE_ID_FULL_DIR_INFORMATION (x64 et x86 identiques : champs naturellement alignés).
    private const int OffNextEntry = 0;
    private const int OffEndOfFile = 40;
    private const int OffAttributes = 56;
    private const int OffNameLength = 60;
    private const int OffEaSize = 64;     // = tag de reparse si FILE_ATTRIBUTE_REPARSE_POINT
    private const int OffFileId = 72;
    private const int OffFileName = 80;

    // Tampon de listage par thread (alloué une fois ; les traversées sont séquentielles).
    [ThreadStatic] private static IntPtr t_buffer;

    // Erreurs réseau transitoires : une nouvelle tentative évite de perdre un sous-arbre
    // entier sur une micro-coupure (VPN, WAN, serveur DFS chargé).
    private static readonly int[] TransientNetworkErrors =
        { 51, 53, 54, 58, 59, 64, 121, 1231, 1232, 1236 };

    /// <summary>Renvoie les entrées immédiates de <paramref name="directory"/> (hors « . » et « .. »).</summary>
    public static List<FsEntry> List(string directory, out int error)
        => List(directory, wantIdentity: false, out error, out _);

    /// <summary>
    /// Renvoie les entrées immédiates de <paramref name="directory"/> (hors « . » et « .. »).
    /// <paramref name="error"/> reçoit le code Win32 : 0 si succès ; sinon la liste est vide
    /// (ouverture impossible) ou <b>partielle</b> (erreur en cours de listage) et l'appelant
    /// journalise / marque l'échec. Si <paramref name="wantIdentity"/>, l'identité du
    /// répertoire lui-même (volume + identifiant) est lue sur le handle déjà ouvert.
    /// </summary>
    public static List<FsEntry> List(string directory, bool wantIdentity, out int error, out DirIdentity identity)
    {
        var entries = ListOnce(directory, wantIdentity, out error, out identity);
        for (int attempt = 1; attempt <= 2 && IsTransient(error); attempt++)
        {
            Thread.Sleep(400 * attempt);
            entries = ListOnce(directory, wantIdentity, out error, out identity);
        }
        return entries;
    }

    /// <summary>Vrai si le code Win32 correspond à une erreur réseau susceptible d'être passagère.</summary>
    public static bool IsTransient(int error) => error != 0 && Array.IndexOf(TransientNetworkErrors, error) >= 0;

    private static List<FsEntry> ListOnce(string directory, bool wantIdentity, out int error, out DirIdentity identity)
    {
        var entries = new List<FsEntry>();
        error = NativeMethods.ERROR_SUCCESS;
        identity = default;

        using var handle = NativeMethods.CreateFile(
            LongPath.ToExtended(directory),
            NativeMethods.FILE_LIST_DIRECTORY | NativeMethods.SYNCHRONIZE,
            NativeMethods.FILE_SHARE_ALL, IntPtr.Zero, NativeMethods.OPEN_EXISTING,
            NativeMethods.FILE_FLAG_BACKUP_SEMANTICS, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            error = Marshal.GetLastWin32Error();
            return entries;
        }

        if (wantIdentity && NativeMethods.GetFileInformationByHandle(handle, out var info))
        {
            identity = new DirIdentity(info.dwVolumeSerialNumber,
                ((long)info.nFileIndexHigh << 32) | info.nFileIndexLow);
        }

        IntPtr buffer = t_buffer != IntPtr.Zero ? t_buffer : (t_buffer = Marshal.AllocHGlobal(BufferSize));
        bool restart = true;
        while (true)
        {
            int status = NativeMethods.NtQueryDirectoryFile(
                handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, out _, buffer, BufferSize,
                NativeMethods.FileIdFullDirectoryInformation, returnSingleEntry: false, IntPtr.Zero, restart);
            restart = false;

            if (status is NativeMethods.STATUS_NO_MORE_FILES or NativeMethods.STATUS_NO_SUCH_FILE)
            {
                break; // fin normale du listage
            }
            if (status < 0)
            {
                // Échec (ou avertissement) en cours de listage : liste PARTIELLE, signalée.
                error = NativeMethods.RtlNtStatusToDosError(status);
                if (error == NativeMethods.ERROR_SUCCESS) { error = status; }
                break;
            }
            Parse(buffer, directory, entries);
        }
        return entries;
    }

    private static void Parse(IntPtr buffer, string directory, List<FsEntry> entries)
    {
        int offset = 0;
        while (true)
        {
            IntPtr e = buffer + offset;
            int next = Marshal.ReadInt32(e, OffNextEntry);
            int nameBytes = Marshal.ReadInt32(e, OffNameLength);
            string name = Marshal.PtrToStringUni(e + OffFileName, nameBytes / 2);

            if (name is not ("." or ".."))
            {
                uint attributes = unchecked((uint)Marshal.ReadInt32(e, OffAttributes));
                bool isDir = (attributes & NativeMethods.FILE_ATTRIBUTE_DIRECTORY) != 0;
                bool isReparse = (attributes & NativeMethods.FILE_ATTRIBUTE_REPARSE_POINT) != 0;
                uint tag = isReparse ? unchecked((uint)Marshal.ReadInt32(e, OffEaSize)) : 0u;
                bool isNameSurrogate = isReparse && (tag & NativeMethods.IO_REPARSE_TAG_NAME_SURROGATE_BIT) != 0;
                long size = isDir ? 0L : Marshal.ReadInt64(e, OffEndOfFile);
                long fileId = Marshal.ReadInt64(e, OffFileId);
                entries.Add(new FsEntry(Combine(directory, name), name, isDir, isNameSurrogate, isReparse,
                    tag, size, fileId));
            }

            if (next == 0) { break; }
            offset += next;
        }
    }

    private static string Combine(string directory, string name)
        => directory.EndsWith('\\') ? directory + name : directory + '\\' + name;
}

/// <summary>
/// Garde de traversée partagée par les passes du moteur : refuse de redescendre dans
/// un répertoire déjà présent dans l'ascendance courante (boucle) et borne la
/// profondeur (protection de pile même quand l'identité est inconnue).
///
/// Une collision d'identité n'est déclarée boucle que si le contenu est <b>le même</b>
/// que celui de l'ancêtre : certains filtres système (virtualisation des applications
/// MSIX, conteneurs…) font annoncer à un dossier distinct l'identifiant d'un autre —
/// sans cette vérification, son contenu serait masqué à tort.
/// </summary>
internal sealed class TraversalGuard
{
    /// <summary>Profondeur de sécurité : très au-delà des arborescences réelles (&lt; 100 niveaux).</summary>
    public const int MaxDepth = 512;

    // Identité → chemin, pour les répertoires de l'ascendance courante.
    private readonly Dictionary<DirIdentity, string> _chain = new();

    /// <summary>
    /// Entre dans un répertoire (déjà listé). Renvoie faux s'il s'agit d'une vraie boucle.
    /// <paramref name="tracked"/> indique si l'identité a été empilée (et devra être
    /// retirée par <see cref="Leave"/>).
    /// </summary>
    public bool TryEnter(DirIdentity id, string path, IReadOnlyList<FsEntry> listing, out bool tracked)
    {
        tracked = false;
        if (!id.IsKnown)
        {
            return true; // identité indisponible : seule la profondeur de sécurité protège
        }
        if (_chain.TryGetValue(id, out string? ancestorPath))
        {
            var ancestorListing = FastDirectoryEnumerator.List(ancestorPath, out int error);
            bool sameContent = error == NativeMethods.ERROR_SUCCESS && SameNames(ancestorListing, listing);
            return !sameContent; // contenu identique : vraie boucle ; sinon homonymie d'identifiant
        }
        _chain.Add(id, path);
        tracked = true;
        return true;
    }

    /// <summary>Sort d'un répertoire ; sans effet si son identité n'avait pas été empilée.</summary>
    public void Leave(DirIdentity id, bool tracked)
    {
        if (tracked) { _chain.Remove(id); }
    }

    public void Reset() => _chain.Clear();

    private static bool SameNames(IReadOnlyList<FsEntry> a, IReadOnlyList<FsEntry> b)
    {
        if (a.Count != b.Count || a.Count == 0)
        {
            return false;
        }
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in a) { names.Add(e.Name); }
        foreach (var e in b)
        {
            if (!names.Contains(e.Name)) { return false; }
        }
        return true;
    }
}
