// Dev/ClientPathResolver.cs
//
// Résolution du répertoire client Martial Heroes, sans variable d'environnement obligatoire.
//
// ORDRE DE RÉSOLUTION :
//   1. Variable d'environnement MH_CLIENT_DIR (override explicite, optionnel).
//   2. Fichier de configuration res://client_dir.cfg (à côté de project.godot) — clé client_dir=.
//   3. Auto-détection : D:\MartialHeroesClient, C:\MartialHeroesClient, LegacyClient/ relatif.
//   Si aucune source ne fournit un répertoire valide, retourne null → mode synthétique.
//
// ACTIVATION DU RENDU RÉEL :
//   TRUE dès qu'un répertoire valide est trouvé ; il n'existe plus de bascule dev pour le désactiver
//   (plus de MH_REAL_ASSETS / real_assets= / load_models=). Le client utilise toujours le vrai VFS ;
//   sans VFS, le rendu est vide (rien n'est fabriqué).
//
// Un répertoire est « valide » s'il contient data.inf ET data/data.vfs.
//
// Ce helper est SANS ÉTAT et SANS DÉPENDANCE GODOT pour faciliter les tests unitaires.
// La seule dépendance Godot est l'appel optionnel à ProjectSettings.GlobalizePath pour
// résoudre res:// — passé en paramètre depuis ClientContext (qui a using Godot).
//
// spec: PRESERVATION_AND_ARCHITECTURE.md §Non-distribution rules — user supplies originals.
// spec: PRESERVATION_AND_ARCHITECTURE.md §05.Presentation — composition root.

using Godot;
using Environment = System.Environment;

namespace MartialHeroes.Client.Godot.Composition;

/// <summary>
///     Helper statique partagé pour résoudre le répertoire du client Martial Heroes original
///     et décider si le rendu sur assets réels est activé.
///     Tous les appelants (<see cref="ClientContext" />, <see cref="RealClientAssets" />,
///     <see cref="MartialHeroes.Client.Godot.World.RealWorldRenderer" />) passent ici
///     plutôt que de lire MH_CLIENT_DIR directement.
/// </summary>
public static class ClientPathResolver
{
    // -------------------------------------------------------------------------
    // Chemins auto-détectés (sondés si aucune config explicite ne réussit)
    // spec: PRESERVATION_AND_ARCHITECTURE.md §Non-distribution rules.
    // -------------------------------------------------------------------------

    // Dossier client embarqué dans le projet Godot (gitignoré — voir clientdata/.gitignore).
    // Emplacement natif recommandé : résolu via res:// pour être indépendant du CWD.
    private const string ClientDataResPath = "res://clientdata";

    // Nom du fichier de config, à côté de project.godot.
    // res://client_dir.cfg → résolu via ProjectSettings.GlobalizePath côté Godot.
    private const string ConfigResPath = "res://client_dir.cfg";

    private static readonly string[] AutoDetectCandidates =
    [
        @"D:\MartialHeroesClient",
        @"C:\MartialHeroesClient",
        "LegacyClient" // relatif au répertoire de travail (dossier projet)
    ];

    // -------------------------------------------------------------------------
    // API publique
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Résout le répertoire racine du client Martial Heroes.
    ///     Ordre :
    ///     1. MH_CLIENT_DIR (env override, optionnel).
    ///     2. client_dir= dans res://client_dir.cfg.
    ///     3. Auto-détection des chemins courants.
    ///     Retourne null si aucune source ne produit un répertoire valide
    ///     (contenant data.inf + data/data.vfs).
    /// </summary>
    public static string? ResolveClientDir()
    {
        // 1. Env override (System.Environment pour éviter l'ambiguïté avec Godot.Environment).
        var envDir = Environment.GetEnvironmentVariable("MH_CLIENT_DIR");
        if (!string.IsNullOrWhiteSpace(envDir))
        {
            if (IsValidClientDir(envDir))
            {
                GD.Print($"[ClientPath] Resolved from MH_CLIENT_DIR env: {envDir}");
                return envDir;
            }

            GD.PrintErr($"[ClientPath] MH_CLIENT_DIR='{envDir}' invalide (data.inf / data/data.vfs introuvables).");
        }

        // 2. Fichier de configuration client_dir.cfg.
        var cfgDir = ReadClientDirFromConfig();
        if (cfgDir is not null)
        {
            // Valeur relative (ex: clientdata) → résolue contre res:// (racine du projet),
            // pas contre le répertoire de travail qui varie selon le mode de lancement.
            if (!Path.IsPathRooted(cfgDir))
                cfgDir = GlobalizeRes("res://" + cfgDir.Replace('\\', '/'));

            if (IsValidClientDir(cfgDir))
            {
                GD.Print($"[ClientPath] Resolved from client_dir.cfg: {cfgDir}");
                return cfgDir;
            }

            GD.PrintErr($"[ClientPath] client_dir.cfg pointe sur '{cfgDir}' mais les fichiers VFS sont absents.");
        }

        // 3a. Dossier clientdata/ embarqué dans le projet Godot (emplacement natif).
        var embedded = GlobalizeRes(ClientDataResPath);
        if (IsValidClientDir(embedded))
        {
            GD.Print($"[ClientPath] Resolved from project-local clientdata/: {embedded}");
            return embedded;
        }

        // 3b. Auto-détection des emplacements externes courants.
        foreach (var candidate in AutoDetectCandidates)
            if (IsValidClientDir(candidate))
            {
                GD.Print($"[ClientPath] Resolved via auto-détection: {candidate}");
                return candidate;
            }

        GD.Print("[ClientPath] Aucun répertoire client trouvé — mode synthétique (aucun asset réel).");
        return null;
    }

    /// <summary>
    ///     Retourne true si le rendu sur assets réels doit être activé : vrai dès qu'un répertoire client
    ///     valide est résolu (<paramref name="clientDir" /> non null), faux sinon. Le client utilise
    ///     TOUJOURS le vrai VFS quand il est présent — il n'existe plus de bascule dev (MH_REAL_ASSETS /
    ///     real_assets= / load_models=) pour forcer un mode synthétique ou terrain-seul. Sans VFS, le
    ///     rendu est simplement vide : rien n'est fabriqué.
    /// </summary>
    public static bool RealAssetsEnabled(string? clientDir)
    {
        return clientDir is not null;
    }

    // -------------------------------------------------------------------------
    // Validation
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Un répertoire est valide s'il contient data.inf ET data/data.vfs.
    ///     spec: Docs/RE/formats/pak.md §Two-file scheme.
    /// </summary>
    public static bool IsValidClientDir(string dir)
    {
        try
        {
            return File.Exists(Path.Combine(dir, "data.inf"))
                   && File.Exists(Path.Combine(dir, "data", "data.vfs"));
        }
        catch
        {
            return false;
        }
    }

    // -------------------------------------------------------------------------
    // Lecture du fichier de config
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Lit la valeur de client_dir= dans client_dir.cfg.
    ///     Retourne null si le fichier est absent, illisible, ou si la clé est absente.
    /// </summary>
    private static string? ReadClientDirFromConfig()
    {
        var cfgPath = ResolveCfgPath();
        if (cfgPath is null) return null;

        return ReadKeyFromCfg(cfgPath, "client_dir");
    }

    /// <summary>
    ///     Traduit un chemin res:// en chemin absolu via ProjectSettings.GlobalizePath.
    ///     Hors contexte Godot (tests unitaires), retombe sur le chemin relatif brut
    ///     (res://X → X, relatif au répertoire de travail).
    /// </summary>
    private static string GlobalizeRes(string resPath)
    {
        try
        {
            return ProjectSettings.GlobalizePath(resPath);
        }
        catch
        {
            return resPath.StartsWith("res://", StringComparison.Ordinal)
                ? resPath["res://".Length..]
                : resPath;
        }
    }

    /// <summary>
    ///     Résout le chemin absolu de client_dir.cfg via ProjectSettings.GlobalizePath
    ///     (disponible en éditeur et en export). Retourne null si le fichier n'existe pas.
    /// </summary>
    private static string? ResolveCfgPath()
    {
        try
        {
            // ProjectSettings.GlobalizePath traduit res:// en chemin absolu.
            // Fonctionne en éditeur Godot, en export, et en headless.
            var absPath = ProjectSettings.GlobalizePath(ConfigResPath);
            return File.Exists(absPath) ? absPath : null;
        }
        catch
        {
            // Hors contexte Godot (tests unitaires) : essai relatif au répertoire de travail.
            const string fallbackName = "client_dir.cfg";
            return File.Exists(fallbackName) ? fallbackName : null;
        }
    }

    /// <summary>
    ///     Lit la valeur associée à <paramref name="key" /> dans un fichier .cfg simple.
    ///     Format : une clé=valeur par ligne, # = commentaire, blancs ignorés.
    /// </summary>
    private static string? ReadKeyFromCfg(string cfgPath, string key)
    {
        try
        {
            foreach (var rawLine in File.ReadLines(cfgPath))
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith('#')) continue;

                var eq = line.IndexOf('=');
                if (eq < 0) continue;

                var k = line[..eq].Trim();
                var v = line[(eq + 1)..].Trim();

                if (k.Equals(key, StringComparison.OrdinalIgnoreCase) && v.Length > 0)
                    return v;
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[ClientPath] Erreur lecture '{cfgPath}': {ex.Message}");
        }

        return null;
    }
}