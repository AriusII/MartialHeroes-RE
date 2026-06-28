---
verification: confirmed
ida_reverified: 2026-06-28
evidence: [static-ida]
status: confirmed
---

# Spécifications de l'Intégration de l'UI et des Scènes (Diamond UI / Engine Scene Loop)

Ce document décrit en détail l'architecture d'intégration de l'interface utilisateur 2D (**Diamond UI**) au sein du cycle de vie des scènes du client de jeu Martial Heroes. Ces spécifications ont été consolidées à partir des analyses de décompilation et des notes brutes suivantes :
* [high_level_ui_integration.md](file:///C:/Users/Arius/RiderProjects/MartialHeroes/Docs/RE/_dirty/high_level_ui_integration.md) (Notes brutes d'intégration de l'UI)
* [gui_framework.md](file:///C:/Users/Arius/RiderProjects/MartialHeroes/Docs/RE/specs/gui_framework.md) (Spécifications du Framework UI 2D)
* [scene_state_machine_decomp.md](file:///C:/Users/Arius/RiderProjects/MartialHeroes/Docs/RE/_dirty/scene_state_machine_decomp.md) (Décompilation de la machine d'état)
* [run_scene_loop_decomp.md](file:///C:/Users/Arius/RiderProjects/MartialHeroes/Docs/RE/_dirty/run_scene_loop_decomp.md) (Décompilation de la boucle principale)
* [device_step_present_decomp.md](file:///C:/Users/Arius/RiderProjects/MartialHeroes/Docs/RE/_dirty/device_step_present_decomp.md) (Décompilation du Device Step)
* [draw_overlay_pass_decomp.md](file:///C:/Users/Arius/RiderProjects/MartialHeroes/Docs/RE/_dirty/draw_overlay_pass_decomp.md) (Décompilation de l'overlay pass)
* [gui_panel_draw_decomp.md](file:///C:/Users/Arius/RiderProjects/MartialHeroes/Docs/RE/_dirty/gui_panel_draw_decomp.md) (Décompilation de `GUPanel__onDraw`)

---

## 1. Machine d'État Principale (`WinMain_SceneStateMachine` @ `0x5fe316`)

La gestion globale du cycle de vie du client et des transitions de scènes est orchestrée par une machine d'état principale à 9 états (indices `0` à `8`), pilotée par le singleton d'état de jeu `GameState_GetSingleton()`. 

Chaque état possède une responsabilité spécifique et instancie des structures de fenêtres ou de gestionnaires précis dont les tailles en mémoire ont été cartographiées à partir de la décompilation des allocations dynamiques :

```
             +------------------------------+
             |    [État 0] Resolution Setup |
             +------------------------------+
                            |
                            v
             +------------------------------+
             |    [État 1] Login Scene      | <--------+
             +------------------------------+          |
                            |                          |
                            v                          |
             +------------------------------+          |
             |    [État 2] Loading Stage    |          |
             +------------------------------+          |
               /                          \            |
   (SKIP = 0) /                            \ (SKIP = 1)|
             v                              v          |
    +------------------+           +------------------+ |
    | [État 3] Opening |           | [État 4] CharSel | |
    +------------------+           +------------------+ |
             \                              /          |
              \                            /           |
               v                          v            |
             +------------------------------+          |
             |     [État 5] Game Play       |----------+
             +------------------------------+
```

### Signification Technique et Cycle de Vie des États

#### État 0 : Boot / Resolution Setup
* **Responsabilité :** Phase de démarrage initial et configuration graphique de l'exécutable.
* **Fonctionnement :** Résout les configurations vidéo issues de `game.lua` via le singleton `Diamond_LuaConfig_GetInstance()` (notamment les variables globales `vfsmode`, `launcher`, et `debugmode`). Initialise le pilote graphique (`Engine_GetDriverSingleton`), détermine et applique les dimensions d'écran via `Engine_ResolveScreenHeightClamped` (borné à une largeur maximale de `1920` pixels), puis transitionne vers l'état `1`.

#### État 1 : Login Scene (Scène de Connexion)
* **Responsabilité :** Rendu et logique de l'interface de connexion.
* **Allocation :** Instancie la classe `Diamond::LoginWindow` à l'aide du constructeur `Diamond_LoginWindow_ctor` (@ `0x5fa4a2`).
* **Taille en Mémoire :** **1368 octets** (allocation `operator new(0x558u)`).
* **Entrée en Boucle :** Lance la boucle de rendu de scène avec `Engine_RunSceneLoop` en lui transmettant un pointeur vers son interface de table de méthodes virtuelles (vtable) secondaire située à l'offset **+188** (ou `+0xBC` en hexadécimal, référencée statiquement par la table de relocation `off_72E84C`).
* **Initialisation des Polices :** Configure les 15 slots de la table de polices Direct3DX globale (`FontTable_GetSingleton()`), incluant les polices système `DotumChe`, `Dotum`, et `BatangChe` avec des tailles variant de 10 à 32 pixels.
* **Sortie de Scène :** Au retour de la boucle, appelle `LoginScene_DispatchLeaveCmd` pour couper proprement les actions de connexion en cours, détruit l'instance `LoginWindow` via son destructeur virtuel primaire, et transitionne vers l'état `2` (ou l'état d'erreur `7` en cas d'échec d'initialisation matérielle de Direct3D9).

#### État 2 : Loading Stage (Étape de Chargement)
* **Responsabilité :** Affichage d'un écran de chargement durant les phases de transition.
* **Allocation :** Instancie la classe `LoadingScreen` à l'aide de `LoadingScreen_ctor` (@ `0x532dc0`).
* **Taille en Mémoire :** **536 octets** (allocation `operator new(0x218u)`).
* **Cinématique d'Ouverture :** Lit la clé `SKIP` de la section `[OPENNING]` dans le fichier INI de configuration système. Si `SKIP` est activé, l'état suivant est directement configuré sur l'état `4` (Sélection de Personnage), sinon l'état suivant est configuré sur l'état `3` (Vidéo d'Ouverture).
* **Sortie de Scène :** Exécute la boucle de scène avec le gestionnaire d'affichage global, appelle `LoadingScene_Teardown` à la fin du chargement, puis détruit proprement l'instance.

#### État 3 : Opening Video Stage (Cinématique d'Ouverture)
* **Responsabilité :** Lecture de la vidéo d'introduction.
* **Allocation :** Instancie la classe `COpeningWindow` à l'aide de `COpeningWindow_ctor` (@ `0x54581a`).
* **Taille en Mémoire :** **720 octets** (allocation `operator new(0x2D0u)`).
* **Sortie de Scène :** Exécute la boucle de scène, appelle `OpeningScene_DispatchLeaveCmd` à la sortie, puis désalloue l'instance. L'état suivant est forcé à `4`.

#### État 4 : Character Selection Stage (Sélection de Personnage)
* **Responsabilité :** Logique de création et sélection du personnage du joueur.
* **Allocation :** Instancie la classe `SelectWindow` à l'aide de `SelectWindow_ctor` (@ `0x5465ef`).
* **Taille en Mémoire :** **6280 octets** (allocation `operator new(0x1888u)`).
* **Entrée en Boucle :** Lance la boucle de rendu de scène avec `Engine_RunSceneLoop` en lui transmettant un pointeur vers son interface vtable secondaire à l'offset **+188** (ou `+0xBC` en hexadécimal, référencée par `off_729FE8`).
* **Sortie de Scène :** Appelle `SelectWindow_LeaveScene` pour libérer les ressources 3D des personnages affichés en prévisualisation, détruit l'instance `SelectWindow`, et transitionne vers l'état de jeu `5`.

#### État 5 : Game Play Stage (Étape de Jeu Actif)
* **Responsabilité :** Scène principale d'exploration, d'interactions et de combat.
* **Allocation :** Instancie la classe de contrôle principale `MainHandler` via `MainHandler_ctor` (@ `0x539e8f`).
* **Taille en Mémoire :** **200 octets** (allocation `operator new(0xC8u)`).
* **Initialisation :** Associe le gestionnaire de scène `MainHandler` à la structure globale `ActorVisualGlobal` (offset `+320`), appelle `MainWindow_SceneInit` pour configurer l'interface utilisateur en jeu (HUD principal, mini-carte, fenêtres d'inventaire), et envoie un paquet de battement de cœur réseau `Cmsg_KeepaliveToggle_Send(1)` pour maintenir active la liaison serveur.
* **Sortie de Scène :** Lance la boucle de jeu principale. À la déconnexion ou au retour à la sélection de personnage, appelle `MainWindow_SceneTeardown` et libère le gestionnaire. L'état suivant est configuré à `4` (ou `6` / `8` en cas d'arrêt global).

#### État 6 & 8 : Shutdown / Teardown
* **Responsabilité :** Libération globale et arrêt propre du client de jeu.
* **Fonctionnement :** L'état `6` définit immédiatement la machine d'état sur l'état de sortie `8`. L'état `8` exécute la fonction de libération générale `InGameScene_UnloadWorldReleaseManagers()`, désinitialise l'environnement multithreadé, ferme le gestionnaire de rapports de plantage `CrashReporter` en écrivant `"winmain end"` dans le journal de débogage, et retourne `0` au système d'exploitation.

#### État 7 : Fatal Error (Gestion d'Erreur Fatale)
* **Responsabilité :** Interception des pannes logicielles d'initialisation graphique ou réseau.
* **Fonctionnement :** Redirige l'état vers `8`. Récupère un message localisé à l'aide de `ResultCode_FormatLocalizedMessage` ou `MessageDB_GetSceneString`, minimise la fenêtre Win32 principale via `ShowWindow(hwnd, SW_MINIMIZE)` (paramètre `6`), coupe la liaison réseau `NetClient`, affiche la boîte de message bloquante via `MessageBoxA` avec le style modal d'erreur système `0x40010u`, et se branche sur l'état d'arrêt propre `8`.

---

## 2. Cycle de Vie de la Boucle de Jeu par Scène (`Engine_RunSceneLoop` @ `0x61beb1`)

La boucle de scène principale est commune à toutes les scènes de l'exécutable. Elle tourne indéfiniment au sein d'une structure `do-while` tant que le drapeau booléen global `g_EngineRunFlag` est actif (`1`).

```cpp
// Représentation conceptuelle de la boucle de scène issue de run_scene_loop_decomp.md
BOOL __thiscall Engine_RunSceneLoop(int this)
{
  timeBeginPeriod(1u);
  g_EngineRunFlag = 1;
  do
  {
    Engine_PumpInputAndMessages(*(DWORD **)(this + 8), &g_EngineRunFlag);
    Engine_DeviceStepAndPresent(this);
    FrameTickScheduler_TickAll(g_FrameTickScheduler_cached);
    result = Engine_FrameRateLimiter((float *)this, *(float *)(this + 48));
  }
  while ( g_EngineRunFlag );
  return result;
}
```

### Les 4 Étapes Clés du Cycle de Frame

1. **Pump (Traitement des messages et entrées) — `Engine_PumpInputAndMessages` :**
   Pumps les événements système de la boucle Win32 standard (`GetMessage` / `PeekMessage`) et actualise l'état DirectInput des périphériques de saisie. En cas d'événement de fermeture de la fenêtre, le pointeur sur `g_EngineRunFlag` est écrit à `0` pour forcer la sortie de la boucle au terme du cycle.
2. **Device Step (Dessin et Présentation) — `Engine_DeviceStepAndPresent` :**
   Exécute le pipeline de rendu complet de la trame. Rends le terrain, les maillages 3D, les modèles animés et invoque la passe d'affichage d'interface utilisateur 2D. Cette fonction présente également le résultat final à l'écran et gère la perte et récupération de périphérique Direct3D9.
3. **Frame Tick (Mise à Jour Logique) — `FrameTickScheduler_TickAll` :**
   Met à jour toute la logique applicative et les animations. C'est ici que sont évalués le mouvement de la caméra, l'avancement des animations Cal3D, la physique élémentaire des collisions, et les délais des actions planifiées.
4. **Frame Limit (Limiteur de FPS) — `Engine_FrameRateLimiter` :**
   Compare le temps système actuel de haute précision au temps de début de frame. Si la durée est inférieure au temps cible par trame calculé à partir de la valeur de framerate de référence (statiquement définie à `60.0` FPS et stockée à l'offset `+48` du pointeur `this` de la boucle), la fonction force une pause système via `Sleep(1)` ou effectue un yield pour empêcher une surconsommation CPU.

> [!TIP]
> L'appel `timeBeginPeriod(1u)` configuré au démarrage de la boucle est indispensable sous Windows pour forcer une résolution temporelle de 1 ms, garantissant ainsi que les appels de temporisation `Sleep` du limiteur de FPS sont précis et stables.

---

## 3. Pipeline de Transition 3D Projective vers 2D Écran

Le pipeline graphique doit commuter entre les projections de perspective 3D (espace homogène) et le repère orthographique 2D plat de l'interface utilisateur. Ce processus est piloté au sein de `Engine_DeviceStepAndPresent` (@ `0x61bdc0`) et `Diamond_Renderer_DrawOverlayPass` (@ `0x60df56`).

```
+-------------------------------------------------------------+
| Rendu de la Scène 3D                                        |
|  - Géométrie, Squelettes (Cal3D), Terrain, Caméra Frustum  |
+-------------------------------------------------------------+
                              |
                              v
+-------------------------------------------------------------+
| Diamond_Renderer_DrawOverlayPass (@ 0x60df56)              |
|  - Initialise une matrice identité 4x4 (Scale 1.0f)         |
|  - GDevice_SetWorldTransform(device, &identity)            |
+-------------------------------------------------------------+
                              |
                              v
+-------------------------------------------------------------+
| Configuration de la Vue Orthogonale                          |
|  - Renderer_SetViewTransform(device, viewport_coords)      |
+-------------------------------------------------------------+
                              |
                              v
+-------------------------------------------------------------+
| Délégation du Dessin                                        |
|  - Vérification de la fonction de rappel (callback)         |
|  - Appel du Callback (LoginWindow_Draw @ 0x61d75f)          |
+-------------------------------------------------------------+
```

### Mécanisme de Commutation de Repère

1. **Rendu 3D Global :** Le moteur effectue le tracé des éléments 3D. Les repères Direct3D9 mondiaux, de vue, et de projection sont appliqués.
2. **Entrée dans la Passe d'Overlay 2D :** Appelle `Diamond_Renderer_DrawOverlayPass`.
3. **Réinitialisation de la Matrice Mondiale à l'Identité :**
   Afin d'annuler les rotations, translations, et transformations spatiales 3D de l'environnement, une matrice de transformation 4x4 d'Identité (échelle de `1.0f`) est initialisée en mémoire sur la pile sous la forme de 16 valeurs flottantes (64 octets). Les éléments diagonaux `v4[0]`, `v4[5]`, `v4[10]`, et `v4[15]` sont explicitement définis à la valeur entière de `1065353216` (`0x3F800000` en hexadécimal, correspondant au flottant IEEE-754 `1.0f`).
   Cette matrice est immédiatement passée au périphérique Direct3D9 via `GDevice_SetWorldTransform` (équivalent de `SetTransform(D3DTS_WORLD, &identity)`).
4. **Application de la Transformation de Vue Orthogonale :**
   Appelle `Renderer_SetViewTransform` en fournissant la structure de dimensions locales du viewport Direct3D afin de forcer un repère plat allant de `(0,0)` (haut-gauche) à `(largeur, hauteur)` de la résolution de rendu de l'écran.
5. **Délégation et Invocation du Callback :**
   Vérifie si un pointeur vers une fonction de callback de rendu est enregistré à l'offset `this + 204`. Si ce pointeur est non-nul, il est exécuté en transmettant la structure de données d'affichage située à l'offset `this + 208` :
   `(*(void (__cdecl **)(_DWORD))(this + 204))(*(_DWORD *)(this + 208))`
   Pour les scènes de Login (État 1) et Sélection (État 4), ce callback résout vers la fonction `Diamond_LoginWindow_Draw` (@ `0x61d75f`).

---

## 4. Chaînage Récursif du Composite Pattern pour le Dessin de Widgets

Le dessin de l'interface graphique utilisateur (Diamond UI) utilise le motif de conception **Composite**. Un widget conteneur parent gère la visibilité, la propagation de l'opacité et le placement spatial de l'ensemble de ses composants enfants.

Le point d'entrée de ce mécanisme est la méthode virtuelle `Diamond_GUPanel__onDraw` (vtable slot `7`, @ `0x6148a4`), qui se trouve dans la vtable du composant de type conteneur.

### Algorithme de Rendu Récursif et Gestion de l'Opacité

#### Étape 1 : Calcul Cumulé des Transformations
Le panneau commence par mettre à jour ses propres coordonnées absolues à l'écran en exécutant `Diamond_GUComponent__computeTransform`. Cette méthode remonte la hiérarchie des widgets parents via le pointeur de structure parent à l'offset `+0x84` pour calculer ses coordonnées globales absolues d'écran `world_x` (`+0x2C`) et `world_y` (`+0x30`). Elle stocke la matrice de translation Direct3D 4x4 résultante à l'offset `+0x44` (64 octets).

#### Étape 2 : Dessin du Fond et Gestion de l'Alpha Fade
Appelle la fonction de dessin de base du composant : `Diamond_GU_SubmitDrawItem_AlphaFade(this)` (@ `0x614f7b`). Cette routine effectue plusieurs calculs d'atténuation et de rendu :
* **Gestion du Fade (Fondu en Entrée/Sortie) :** Lit l'état cible de visibilité `show_target` à l'offset `+140` (`+0x8C`). Si `show_target` vaut `0` (masqué), l'opacité actuelle `alpha` (`+0x04`) est décrémentée par pas de `64` par cycle de dessin. Si `show_target` vaut `1` (affiché), `alpha` est incrémenté par pas de `64`. La valeur finale de l'opacité est clampée dans l'intervalle `[0, 255]`.
* **Surcharge d'Alpha Forcé :** Si l'octet de canal alpha forcé à l'offset `+0x0F` (dans le membre `tint_and_forced_alpha` à `+0x0C`) est différent de `0xFF`, la valeur d'opacité courante est écrasée par cette valeur.
* **Soumission du Quad Texturé :** Si un identifiant de texture ou de ressource est valide à l'offset `+0x90` et que le composant est visible (alpha courant > 0), il appelle `Diamond_GU2D_SubmitTexturedQuad` en transmettant les informations géométriques à `+0x34` (52), la matrice cumulée à `+0x44` (68), et la couleur teintée RGBA (`(alpha << 24) | (tint & 0xFFFFFF)`).

#### Étape 3 : Propagation Récursive aux Enfants
Le conteneur `GUPanel` parcourt alors son vecteur de pointeurs d'éléments enfants, délimité en mémoire par le pointeur de départ `*(this + 42)` (`_Myfirst` ou offset `0xA8`) et le pointeur de fin `*(this + 43)` (`_Mylast` ou offset `0xAC`).
Pour chaque pointeur de widget enfant `*i` rencontré :
1. Invoque la méthode virtuelle `computeTransform` de l'enfant (vtable offset `36` / slot 9) pour que ce dernier résolve sa géométrie absolue à l'écran par rapport aux coordonnées absolues calculées par son parent.
2. Vérifie si le sous-composant enfant est marqué comme visible en lisant son état cible : `if (*(_BYTE *)(*i + 140) == 1)`.
3. Si l'enfant est visible, invoque récursivement sa propre méthode virtuelle `onDraw` (vtable offset `28` / slot 7) pour dessiner l'enfant et propager à son tour l'arborescence (ex: dessin de boutons ou de labels de texte hébergés dans le panneau).

```cpp
// Représentation conceptuelle du parcours récursif (gui_panel_draw_decomp.md)
unsigned __int8 __thiscall Diamond_GUPanel__onDraw(int *this)
{
  unsigned __int8 result;
  _DWORD *i;

  Diamond_GUComponent__computeTransform(this);
  result = Diamond_GU_SubmitDrawItem_AlphaFade(this);
  
  // Parcours des pointeurs de la liste des enfants
  for ( i = (_DWORD *)*(this + 42); i != (_DWORD *)*(this + 43); ++i )
  {
    // Calcul de la transformation de l'enfant (vtable offset 36)
    result = (*(int (__thiscall **)(_DWORD))(*(_DWORD *)*i + 36))(*i);
    
    // Si l'enfant est visible (show_target à l'offset 140 == 1)
    if ( *(_BYTE *)(*i + 140) == 1 )
    {
      // Dessin récursif de l'enfant (vtable offset 28)
      result = (*(int (__thiscall **)(_DWORD))(*(_DWORD *)*i + 28))(*i);
    }
  }
  return result;
}
```
