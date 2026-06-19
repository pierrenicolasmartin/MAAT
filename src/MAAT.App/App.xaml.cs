// MAAT — Monitoring et Audit des Accès pour la Traçabilité
// Copyright (C) 2026  Pierre-Nicolas MARTIN
//
// This program is free software: you can redistribute it and/or modify it
// under the terms of the GNU General Public License as published by the Free
// Software Foundation, either version 3 of the License, or (at your option)
// any later version. This program is distributed WITHOUT ANY WARRANTY; see
// the GNU General Public License <https://www.gnu.org/licenses/> for details.

using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace MAAT.App;

public partial class App : Application
{
    /// <summary>Fichier .maat passé en argument (association de fichiers), sinon null.</summary>
    public string? StartupProjectPath { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (e.Args.Length > 0 && File.Exists(e.Args[0])
            && string.Equals(Path.GetExtension(e.Args[0]), ".maat", StringComparison.OrdinalIgnoreCase))
        {
            StartupProjectPath = e.Args[0];
        }

        // Capture globale : aucune exception ne doit faire planter l'app en silence.
        DispatcherUnhandledException += OnDispatcherException;
        AppDomain.CurrentDomain.UnhandledException += (_, ex) =>
            LogException("AppDomain", ex.ExceptionObject as Exception);
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, ex) =>
        {
            LogException("Task", ex.Exception);
            ex.SetObserved();
        };
    }

    private void OnDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        string path = LogException("UI", e.Exception);
        MessageBox.Show(
            $"Une erreur est survenue :\n\n{e.Exception.Message}\n\nDétails enregistrés dans :\n{path}",
            "MAAT — Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true; // évite le crash brutal du processus
    }

    private static string LogException(string source, Exception? ex)
    {
        string path = Path.Combine(Path.GetTempPath(), "maat_crash.log");
        try
        {
            string entry = $"[{DateTimeOffset.Now:O}] ({source})\n{ex}\n\n";
            File.AppendAllText(path, entry);
        }
        catch { /* journalisation best-effort */ }
        return path;
    }
}