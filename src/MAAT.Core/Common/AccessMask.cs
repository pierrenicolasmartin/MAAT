// MAAT — Monitoring et Audit des Accès pour la Traçabilité
// Copyright (C) 2026  Pierre-Nicolas MARTIN
//
// This program is free software: you can redistribute it and/or modify it
// under the terms of the GNU General Public License as published by the Free
// Software Foundation, either version 3 of the License, or (at your option)
// any later version. This program is distributed WITHOUT ANY WARRANTY; see
// the GNU General Public License <https://www.gnu.org/licenses/> for details.

namespace MAAT.Core.Common;

/// <summary>
/// Normalisation d'un masque d'accès NTFS pour le <b>rapprochement de source
/// d'héritage</b> : mappe les droits GÉNÉRIQUES (GENERIC_READ/WRITE/EXECUTE/ALL)
/// vers leurs équivalents SPÉCIFIQUES de fichier (FILE_GENERIC_*).
///
/// Indispensable car Windows stocke souvent un droit héritable sous sa forme
/// générique sur le parent, et le mappe en forme spécifique sur l'objet enfant
/// lui-même (l'ACE « ce dossier »). Sans normalisation, l'ACE spécifique héritée
/// ne retrouverait pas son ancêtre générique → « Source inconnue » à tort.
///
/// N'affecte QUE la clé de rapprochement ; les droits affichés conservent leur
/// forme brute (générique ou spécifique) telle qu'elle figure dans l'ACL.
/// </summary>
internal static class AccessMask
{
    private const int GENERIC_READ = unchecked((int)0x8000_0000);
    private const int GENERIC_WRITE = 0x4000_0000;
    private const int GENERIC_EXECUTE = 0x2000_0000;
    private const int GENERIC_ALL = 0x1000_0000;

    // Équivalents spécifiques « fichier » (winnt.h).
    private const int FILE_GENERIC_READ = 0x0012_0089;
    private const int FILE_GENERIC_WRITE = 0x0012_0116;
    private const int FILE_GENERIC_EXECUTE = 0x0012_00A0;
    private const int FILE_ALL_ACCESS = 0x001F_01FF;

    /// <summary>Renvoie le masque avec les bits génériques convertis en bits spécifiques.</summary>
    public static int NormalizeForMatch(int mask)
    {
        int m = mask;
        if ((m & GENERIC_READ) != 0) { m = (m & ~GENERIC_READ) | FILE_GENERIC_READ; }
        if ((m & GENERIC_WRITE) != 0) { m = (m & ~GENERIC_WRITE) | FILE_GENERIC_WRITE; }
        if ((m & GENERIC_EXECUTE) != 0) { m = (m & ~GENERIC_EXECUTE) | FILE_GENERIC_EXECUTE; }
        if ((m & GENERIC_ALL) != 0) { m = (m & ~GENERIC_ALL) | FILE_ALL_ACCESS; }
        return m;
    }
}
