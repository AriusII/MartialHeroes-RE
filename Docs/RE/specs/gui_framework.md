# Spécifications du Framework UI 2D (Diamond UI)

Ce document décrit le modèle objet C++, le mécanisme de rendu hiérarchique, le traitement des événements et le rendu de texte du framework d'interface graphique 2D propriétaire (**Diamond UI**) utilisé par le client.

Ces spécifications ont été consolidées à partir des analyses brutes et décompilations suivantes :
* [ui_cartography.md](file:///C:/Users/Arius/RiderProjects/MartialHeroes/Docs/RE/_dirty/ui_cartography.md) (Vtables et constructeurs)
* [gui_draw_decomp.md](file:///C:/Users/Arius/RiderProjects/MartialHeroes/Docs/RE/_dirty/gui_draw_decomp.md) (`SubmitDrawItem_AlphaFade`)
* [gui_panel_draw_decomp.md](file:///C:/Users/Arius/RiderProjects/MartialHeroes/Docs/RE/_dirty/gui_panel_draw_decomp.md) (`GUPanel__onDraw`)
* [gui_label_draw_decomp.md](file:///C:/Users/Arius/RiderProjects/MartialHeroes/Docs/RE/_dirty/gui_label_draw_decomp.md) (`GULabel__onDraw`)
* [gui_event_decomp.md](file:///C:/Users/Arius/RiderProjects/MartialHeroes/Docs/RE/_dirty/gui_event_decomp.md) (`OnEvent_ClickSynth`)
* [gucomponent.md](file:///C:/Users/Arius/RiderProjects/MartialHeroes/Docs/RE/structs/gucomponent.md) (Champs et offsets de base)
* [guwindow.md](file:///C:/Users/Arius/RiderProjects/MartialHeroes/Docs/RE/structs/guwindow.md) (Héritage multiple et fenêtres)
* [ui_system.md](file:///C:/Users/Arius/RiderProjects/MartialHeroes/Docs/RE/specs/ui_system.md) (Comportement global et catalogue de widgets)

---

## 1. Modèle Objet C++ du Framework UI

Le framework UI de Diamond repose sur une hiérarchie de classes C++ à héritage simple pour les widgets de base, et à héritage multiple pour les fenêtres de premier niveau. L'alignement en mémoire est de type **naturel sur 4 octets** (paramètre par défaut de MSVC 32 bits), sans compactage d'octets.

### 1.1 Hiérarchie des Classes et Tailles Mémoire

* `Diamond::GUComponent` (164 octets / `0xA4`) : Classe de base universelle pour tous les widgets 2D.
* `Diamond::GUPanel` (180 octets / `0xB4`) : Conteneur dérivé de `GUComponent` capable d'héberger des enfants.
* `Diamond::GUButton` (~252 octets) : Bouton interactif gérant plusieurs états de sprites (normal, survolé, pressé, désactivé).
* `Diamond::GULabel` (~240 octets) : Widget d'affichage de texte statique ou dynamique.
* `Diamond::GUWindow` (Variable, ~600+ octets) : Fenêtre principale héritant de `GUPanel` et réalisant l'interface abstraite `Diamond::EventHandler` via un sous-objet imbriqué `CmdHandler`.

---

### 1.2 Layout Mémoire et Offsets des Variables Membres

#### Diamond::GUComponent (Taille : 164 octets / 0xA4)
La classe de base gère la géométrie locale/globale, l'opacité (alpha fade), la teinte de couleur, les états de survol et les minuteries d'auto-masquage.

| Offset (Hex) | Offset (Déc) | Type | Variable Membre | Description |
| :--- | :--- | :--- | :--- | :--- |
| `+0x00` | 0 | `ptr` | `vftable` | Pointeur vers la table des méthodes virtuelles (13 slots). |
| `+0x04` | 4 | `u32` | `alpha` | Opacité actuelle (0 à 255). Valeur par défaut : `255`. |
| `+0x08` | 8 | `u32` | `capability_flags` | Masque de bits de type/capacité (ex: `0x0001` = Component, `0x0004` = Panel, `0x0008` = Button). |
| `+0x0C` | 12 | `u32` | `tint_and_forced_alpha` | Couleur RGBA condensée. Poids faible 24-bits = couleur RGB de teinte; poids fort (`+0x0F`) = alpha forcé (si `!= 0xFF`). |
| `+0x10` | 16 | `i32` | `action_id` | Identifiant d'action pour le dispatching d'événements. Valeur par défaut : `-1`. |
| `+0x14` | 20 | `i32` | `local_x` | Position relative en X par rapport au composant parent. |
| `+0x18` | 24 | `i32` | `local_y` | Position relative en Y par rapport au composant parent. |
| `+0x1C` | 28 | `i32` | `width` | Largeur du widget. |
| `+0x20` | 32 | `i32` | `height` | Hauteur du widget. |
| `+0x24` | 36 | `i32` | `pos_x` | Position absolue/relative X saisie par `setPosition`. |
| `+0x28` | 40 | `i32` | `pos_y` | Position absolue/relative Y saisie par `setPosition`. |
| `+0x2C` | 44 | `i32` | `world_x` | Position absolue X calculée à l'écran (somme des coordonnées parentes). |
| `+0x30` | 48 | `i32` | `world_y` | Position absolue Y calculée à l'écran (somme des coordonnées parentes). |
| `+0x34` | 52 | `i32` | `pos_x_copy` | Copie de `pos_x`, souvent utilisée comme origine X du rectangle source de l'atlas. |
| `+0x38` | 56 | `i32` | `pos_y_copy` | Copie de `pos_y`, utilisée comme origine Y du rectangle source de l'atlas. |
| `+0x3C` | 60 | `i32` | `x_extent` | Limite droite calculée (`pos_x + width`). |
| `+0x40` | 64 | `i32` | `y_extent` | Limite basse calculée (`pos_y + height`). |
| `+0x44` | 68 | `matrix` | `transform_matrix` | Matrice de transformation 4x4 (D3D, 64 octets) allant de `+0x44` à `+0x83`. |
| `+0x84` | 132 | `ptr` | `parent` | Pointeur vers le `GUComponent` parent. Nul pour les fenêtres racines. |
| `+0x88` | 136 | `u8` | `hovered` | État stable de survol de la souris (1 = survolé, 0 = non). |
| `+0x89` | 137 | `u8` | `hover_edge` | Verrou de transition de survol pour ne déclencher `onMouseEnter`/`Leave` qu'une fois. |
| `+0x8A` | 138 | `u8` | `interactive` | Indicateur d'interactivité (1 = réagit aux clics, 0 = passif). |
| `+0x8B` | 139 | `u8` | `focused` | Focus clavier / IME actif (1 = reçoit les saisies clavier). |
| `+0x8C` | 140 | `u8` | `show_target` | Cible d'affichage pour la transition d'alpha (1 = visible/fade-in, 0 = masqué/fade-out). |
| `+0x8D` | 141 | `u8` | `remove_mark` | Drapeau de suppression différée de l'enfant (utilisé lors du balayage de nettoyage du parent). |
| `+0x90` | 144 | `ptr` | `draw_handle` | Handle de texture ou d'atlas de sprite lié à ce composant. |
| `+0x95` | 149 | `u8` | `auto_hide_enabled` | Si mis à `1`, le widget se masque automatiquement après expiration du délai. |
| `+0x98` | 152 | `u32` | `auto_hide_start_ms` | Timestamp d'activation de la minuterie d'auto-masquage (en millisecondes). |
| `+0x9C` | 156 | `u32` | `auto_hide_timeout` | Durée avant masquage automatique (par défaut `3000` ms). |
| `+0xA0` | 160 | `ptr` | `on_timeout_callback` | Pointeur de fonction (callback) exécutée à l'expiration de l'auto-masquage. |

#### Diamond::GUPanel (Taille : 180 octets / 0xB4)
Le conteneur étend `GUComponent` en ajoutant la gestion d'un vecteur dynamique d'enfants widgets.

| Offset (Hex) | Offset (Déc) | Type | Variable Membre | Description |
| :--- | :--- | :--- | :--- | :--- |
| `+0x00`..`+0xA3` | 0..163 | — | — | Membres hérités de `GUComponent`. |
| `+0xA4` | 164 | `ptr` | `children_vector._Myproxy` | Pointeur de proxy d'itérateur (spécifique à l'implémentation de débogage MSVC). |
| `+0xA8` | 168 | `ptr` | `children_vector._Myfirst` | Pointeur vers le début du tableau de pointeurs enfants (`GUComponent**`). |
| `+0xAC` | 172 | `ptr` | `children_vector._Mylast` | Pointeur vers le dernier élément utilisé (fin logique de la liste). |
| `+0xB0` | 176 | `ptr` | `children_vector._Myend` | Pointeur vers la fin de l'espace mémoire réservé (capacité du vecteur). |
| `+0xB4` | 180 | `i32` | `active_child` | Index ou indicateur de l'enfant actif / sélectionné par tabulation (vaut `-1` si aucun). |

#### Diamond::GUButton (Taille : ~252 octets)
Hérite de `GUComponent` et implémente la machine à états de texture pour la gestion des boutons à 2, 3 ou 7 états.

| Offset (Hex) | Offset (Déc) | Type | Variable Membre | Description |
| :--- | :--- | :--- | :--- | :--- |
| `+0xA4` | 164 | `string` | `caption` | Chaîne de texte affichée sur le bouton (`std::string` MSVC de 28 octets, CP949). |
| `+0xC0` | 192 | `u8` | `pressed` | Indicateur d'état enfoncé (1 = enfoncé, 0 = relâché). |
| `+0xC4` | 196 | `i32` | `state_count` | Nombre d'états déclarés (ex: 2, 3 ou 7 états). |
| `+0xC8` / `+0xCC` | 200 / 204 | `i32` / `i32` | `normal_origin` | Coordonnées sources `(srcX, srcY)` dans l'atlas pour l'état NORMAL. |
| `+0xD0` / `+0xD4` | 208 / 212 | `i32` / `i32` | `pressed_origin` | Coordonnées sources `(srcX, srcY)` dans l'atlas pour l'état PRESSED. |
| `+0xD8` / `+0xDC` | 216 / 220 | `i32` / `i32` | `hover_origin` | Coordonnées sources `(srcX, srcY)` dans l'atlas pour l'état HOVER (survol). |
| `+0xE0` / `+0xE4` | 224 / 228 | `i32` / `i32` | `disabled_origin` | Coordonnées sources `(srcX, srcY)` pour l'état DISABLED (souvent identique à normal). |
| `+0xE8` | 232 | `i32` | `font_slot` | Police d'affichage sélectionnée pour le texte de légende (Caption). |
| `+0xEC` / `+0xF0` | 236 / 240 | `i32` / `i32` | `caption_offset` | Décalage en pixels `(dx, dy)` pour le positionnement du texte de légende. |
| `+0xF4` | 244 | `i32` | `highlight_color` | Couleur de sélection alternative / surlignage (vaut `-1` si inactive). |
| `+0xF8` | 248 | `u8` | `disabled` | Indicateur d'état désactivé (1 = inactif, le bouton ne réagit pas aux clics). |

#### Diamond::GULabel (Taille : ~240 octets)
Gère le texte avec deux buffers de chaînes distincts (principal et auxiliaire).

| Offset (Hex) | Offset (Déc) | Type | Variable Membre | Description |
| :--- | :--- | :--- | :--- | :--- |
| `+0xA4` | 164 | `string` | `caption` | Chaîne de texte principale (`std::string` de 28 octets avec proxy, CP949). |
| `+0xC0` | 192 | `string` | `aux_text` | Chaîne de texte secondaire/alternative (utilisée si le drapeau est armé). |
| `+0xDC` / `+0xE0` | 220 / 224 | `i32` / `i32` | `text_offset` | Décalage relatif `(dx, dy)` de rendu du texte par rapport au coin du widget. |
| `+0xE4` | 228 | `i32` | `font_slot` | Indice de la police utilisée pour le rendu. |
| `+0xE8` | 232 | `u8` | `use_secondary` | Drapeau forçant l'affichage de `aux_text` au lieu de `caption` (1 = secondaire, 0 = principal). |

#### Diamond::GUWindow (Dérivé de GUPanel, Héritage Multiple)
Possède un layout complexe avec deux pointeurs de tables virtuelles pour réaliser le comportement de dispatch de commandes `Diamond::EventHandler`.

* **`+0x00`** : Table virtuelle primaire de `GUWindow` (héritée de `GUPanel`).
* **`+0xA4`..`+0xB8`** : Région du vecteur d'enfants de `GUPanel`.
* **`+0xBC`** : Table virtuelle secondaire de `CmdHandler` (2 slots), servant de relais d'événement entre le framework d'entrée globale et la fenêtre active.
* **`+0xBC`..`+0xE7`** : Sous-objet command-handler (`CmdHandler`, 44 octets), avec son nom de chaîne de débogage à `+0xC0` (ex: `"MainMaster"` ou `"Loginer"`).
* **`+0xE8`..`+0x21F`** : Sous-objet de rendu 3D embarqué (`Diamond::GView`, 312 octets) permettant d'intégrer les rendus 3D de personnages et de scènes au milieu de l'interface 2D.
* **`+0x220`..(fin)** : Vecteur de textures d'atlas propres à la fenêtre.

---

### 1.3 Layout des Tables de Méthodes Virtuelles (Vtables)

Chaque dérivation de classe ajoute ou surcharge des méthodes dans la structure de vtable correspondante.

#### 1. Vtable de `Diamond::GUComponent` (Vtable @ `0x72979c` — 13 Slots)
La vtable fondamentale du framework définit le contrat de base d'un widget visible et positionnable :

* **`[0] +0`** : Destructeur virtuel de base (scalar deleting dtor).
* **`[1] +4`** : `setVisible(bool)` — Modifie l'état de visibilité `show_target` (`+0x8C`), met à jour l'alpha et gère l'auto-masquage.
* **`[2] +8`** : `setPosition(x, y)` — Définit la position du composant (`+0x24`/`+0x28`).
* **`[3] +12`** : `getPosition()` — Récupère les coordonnées locales.
* **`[4] +16`** : `hitTestVec(Vector2)` — Test de collision par structure vectorielle.
* **`[5] +20`** : `hitTest(x, y)` — Test de collision AABB (boîte englobante) avec mise à jour des variables de survol (`+0x88` et `+0x89`) et appel de `onMouseEnter`/`Leave`.
* **`[6] +24`** : `onEvent(InputEvent*)` — Méthode générique de traitement d'événement (surchargée pour la synthèse de clic).
* **`[7] +28`** : `onDraw()` — Exécute l'alpha-fade, combine les teintes, applique les matrices et dessine le quad texturé via `SubmitDrawItem_AlphaFade`.
* **`[8] +32`** : `onUpdate()` — Met à jour les limites (`x_extent`/`y_extent`) et propage l'appel au slot 9.
* **`[9] +36`** : `computeTransform()` — Résout la position absolue à l'écran (`world_x`/`world_y`) en remontant la chaîne de parents (`+0x84`) et reconstruit la matrice 4x4.
* **`[10] +40`** : `getHitActionId()` — Récupère la valeur d'`action_id` (`+0x10`).
* **`[11] +44`** : `onMouseEnter()` — Callback appelé lorsque la souris entre dans la zone du composant (stub vide).
* **`[12] +48`** : `onMouseLeave()` — Callback appelé lorsque la souris quitte la zone du composant (stub vide).

#### 2. Vtable de `Diamond::GUPanel` (Vtable @ `0x730acc` — 14 Slots)
`GUPanel` redéfinit le comportement de dessin, de mise à jour et de dispatching pour gérer la récursion sur ses enfants :

* **`[0] +0`** : Destructeur de panel (libère le vecteur d'enfants et détruit récursivement les widgets enfants).
* **`[6] +24`** : `Diamond_GUWindow_InputDispatch` — Distribue les entrées clavier/souris aux enfants.
* **`[7] +28`** : `Diamond_GUPanel__onDraw` — Surcharge de dessin hiérarchique parent + propagation récursive aux enfants.
* **`[8] +32`** : `Diamond_GUPanel__onUpdate` — Met à jour l'état du panel puis déclenche l'update de chaque enfant.
* **`[10] +40`** : `Diamond_GUPanel__getActiveChild` — Récupère le pointeur de l'enfant actuellement focalisé.
* **`[13] +52`** (Ajout) : `sweepDeferredChildren()` — Parcourt la liste des enfants, libère ceux marqués pour destruction (`+0x8D == 1`) et nettoie le vecteur.

#### 3. Vtable de `Diamond::GUButton` (Vtable @ `0x730b10` — 13 Slots)
* **`[5] +20`** : `Diamond_GUButton__hitTest` (surchargé pour affiner la zone cliquable du bouton).
* **`[6] +24`** : `Diamond_GUButton__onEvent` (traite les transitions d'état du bouton et envoie l'événement d'action).
* **`[7] +28`** : `Diamond_GUButton__onDraw` (sélectionne le bon sprite d'état et le dessine).
* **`[11] +44`** : `Diamond_GUButton__onMouseEnter` (surchargé pour appliquer un retour visuel/effet de survol).

#### 4. Vtable de `Diamond::GULabel` (Vtable @ `0x730b4c` — 13 Slots)
* **`[6] +24`** : `Diamond_GULabel__onEvent` (gestion sommaire d'événements).
* **`[7] +28`** : `Diamond_GULabel__onDraw` (exécute le rendu de texte via la table de polices).

#### 5. Vtable de `Diamond::GUWindow` (Vtable @ `0x731154` — 15 Slots + Vtable CmdHandler — 2 Slots)
* ** Vtable Primaire (Slots 0..14) :**
  * **`[7] +28`** : Surcharge le dessin global (ex: `Diamond_LoginWindow_Draw` pour la fenêtre de connexion).
  * **`[14] +56`** (Ajout) : Helper d'initialisation de la vue 3D embarquée (`GView`) et configuration des pointeurs d'appartenance.
* ** Vtable Secondaire (CmdHandler, @ `+0xBC` — 2 Slots) :**
  * **`[0] +0`** : Destructeur virtuel secondaire (adjuster thunk).
  * **`[1] +4`** : Récepteur principal de commandes. Il résout l'action ID du widget qui a déclenché l'événement et la transmet à la boucle logique de la scène active de la fenêtre.

---

## 2. Mécanisme de Rendu Hiérarchique (Composite Pattern)

Le dessin de l'interface graphique suit le pattern de conception **Composite**, où le rendu du conteneur parent propage automatiquement et récursivement les calculs géométriques et l'affichage vers ses éléments enfants.

Ce processus est implémenté dans la méthode virtuelle `Diamond_GUPanel__onDraw` (adresse de référence : `0x6148a4`).

```mermaid
graph TD
    A[Appel GUPanel::onDraw] --> B[GUComponent::computeTransform]
    B --> C[Diamond_GU_SubmitDrawItem_AlphaFade]
    C --> D[Boucle sur les enfants du Panel de _Myfirst à _Mylast]
    D --> E[Appel Enfant::computeTransform]
    E --> F{Enfant visible?}
    F -- Oui -- > G[Appel Enfant::onDraw]
    F -- Non --> H[Passer à l'enfant suivant]
    G --> I[Fin de la boucle]
    H --> I
    I --> J[Retourner le résultat du dessin]
```

### 2.1 Propagation Géométrique (`computeTransform`)
Avant de dessiner, le conteneur recalcul sa propre matrice avec `Diamond_GUComponent__computeTransform` (slot 9). Cette méthode accumule les coordonnées absolues à l'écran en suivant récursivement la chaîne des pointeurs de parents `+0x84`. Elle convertit les coordonnées de position locale `(local_x, local_y)` en coordonnées absolues d'écran `(world_x, world_y)`.
Une matrice D3D de transformation de translation 4x4 est alors assemblée à l'offset `+0x44` avec ces coordonnées absolues.

### 2.2 Rendu du Conteneur Parent (`SubmitDrawItem_AlphaFade`)
La fonction `Diamond_GU_SubmitDrawItem_AlphaFade` (adresse : `0x614f7b`) est immédiatement appelée sur le conteneur. Ses responsabilités sont les suivantes :
1. **Mise à jour de l'Alpha de transition (Fade Effect) :**
   * Elle lit la cible visuelle `show_target` (`+140`).
   * Si `show_target == 0` (masqué), l'alpha actuel (`+0x04`) est décrémenté par pas de 64 jusqu'à atteindre `0`.
   * Si `show_target == 1` (visible), l'alpha actuel est incrémenté par pas de 64 jusqu'à atteindre `255`.
   * La valeur finale de l'alpha est clampée entre `0` et `255`.
2. **Gestion de l'Override d'Alpha :**
   * Elle vérifie le champ `forced_alpha` (`+0x0F`). Si cette valeur est différente de `0xFF`, elle écrase l'alpha calculé à l'étape précédente et l'impose comme valeur courante pour ce frame.
3. **Soumission Graphique :**
   * Si un handle de texture valide est enregistré à l'offset `+144` (`+0x90`), et que le widget n'est pas totalement transparent, la méthode soumet l'objet au gestionnaire de sprites de l'application via `Diamond_GU2D_SubmitTexturedQuad`.
   * Les arguments passés comprennent : le pointeur global de sprite, le handle de texture, l'adresse de la structure RECT source (`+0x34` pour les boutons, ou décalage `+52`), l'adresse de la structure RECT de destination (`+0x44` ou calculée), et la couleur finale calculée en combinant l'alpha en cours de validité (décalé à gauche de 24) avec la teinte de couleur RGB présente à l'offset `+0x0C`.

### 2.3 Parcours Récursif des Enfants
Après s'être rendu lui-même, `GUPanel` parcourt la liste de ses enfants stockés dans le vecteur dynamique `std::vector<GUComponent*>` délimité par `_Myfirst` (`+0xA8`) et `_Mylast` (`+0xAC`) :
* Pour chaque pointeur d'enfant trouvé dans la liste :
  * Le panel appelle la méthode virtuelle `computeTransform` (slot 9) sur l'enfant. Cela garantit que la géométrie absolue de l'enfant est parfaitement synchronisée avec les éventuels déplacements ou transformations récents du panel parent.
  * Il vérifie ensuite la visibilité en examinant le drapeau `show_target` de l'enfant à l'offset `+140`.
  * Si l'enfant est visible (valeur `1`), le panel invoque la méthode virtuelle `onDraw` (slot 7) de cet enfant. Cela déclenche le dessin de l'enfant (soit un dessin de feuille comme `GULabel` ou `GUButton`, soit un nouveau panel, propageant le rendu plus profondément).

---

## 3. Traitement des Événements et Synthèse de Clic (Click-vs-Drag)

Le framework UI intègre un mécanisme robuste de distinction entre le clic réel et le glissement de souris (drag), afin d'éviter le déclenchement accidentel d'actions lorsque l'utilisateur enfonce le bouton sur un widget mais relâche le pointeur à l'extérieur.

Cette logique est orchestrée dans la méthode `Diamond_GUComponent_OnEvent_ClickSynth` (adresse : `0x615048`).

### 3.1 Types d'Événements du Système d'Entrée
* **Type 4 (Mouse Down / Button Press) :** L'utilisateur a appuyé sur le bouton de la souris.
* **Type 5 (Mouse Up / Button Release) :** L'utilisateur a relâché le bouton de la souris.
* **Type 6 (Synthetic Click) :** Événement logique de clic généré par le framework. C'est cet événement qui est intercepté par les écouteurs de boutons du jeu pour déclencher l'action associée à l'ID de widget.

### 3.2 Algorithme de Synthèse et Capture
La variable globale `dword_9D6EB8` sert de registre système pour stocker l'adresse du widget capturé (`CapturedWidget`).

```cpp
// Pseudocode conceptuel de Diamond_GUComponent_OnEvent_ClickSynth
char __thiscall Diamond_GUComponent_OnEvent_ClickSynth(GUComponent *this, InputEvent *event)
{
    // Si l'événement est un Mouse Down (Type 4)
    if ( event->type == 4 )
    {
        // On vérifie si le composant est interactif (offset +0x8A / +138)
        if ( this->interactive )
        {
            // Le widget capture l'interaction globale
            CapturedWidget = (int)this;
        }
    }
    // Si l'événement est un Mouse Up (Type 5)
    else if ( event->type == 5 )
    {
        // On valide si le relâchement se produit sur le widget qui a reçu le Mouse Down
        if ( CapturedWidget == (int)this )
        {
            // Libération de la capture
            CapturedWidget = 0;

            // Instanciation d'un événement logique synthétique de CLIC (Type 6)
            InputEvent clickEvent;
            Diamond_InputEvent_Init(
                &clickEvent, 
                6, // Type 6 = CLICK
                event->mouseX, 
                event->mouseY, 
                event->paramA, 
                event->paramB
            );

            // Envoi de l'événement synthétique dans la file globale de l'application
            EngineInputCtx *inputCtx = EngineInputCtx_GetSingleton();
            Diamond_AppEventQueue_Post(inputCtx, &clickEvent);
        }
    }
    return 0;
}
```

### 3.3 Conséquences Comportementales
Ce système garantit qu'un bouton ou composant interactif ne réagira qu'aux clics complets et focalisés. Si l'utilisateur clique sur le widget, glisse la souris en dehors et relâche (scénario classique d'annulation de clic), l'événement Mouse Up de Type 5 sera reçu par un autre composant (ou le fond de la fenêtre), causant une inadéquation avec le pointeur enregistré dans `CapturedWidget`. La capture sera annulée sans déclencher l'événement logique de Type 6.

---

## 4. Rendu de Texte via `GULabel`

Le dessin de texte dans l'interface utilisateur est géré par la classe `GULabel` et sa méthode virtuelle de dessin surchargée `Diamond_GULabel__onDraw` (adresse : `0x615c8e`).

### 4.1 Sélection du Buffer de Texte
Un widget `GULabel` encadre deux instances d'objets `std::string` :
* `primary_text` (`this + 164`) : Le libellé principal du widget.
* `aux_text` (`this + 192`) : Un texte secondaire (souvent utilisé pour des traductions dynamiques ou des infobulles/variations d'état).

Pendant le rendu, le code évalue la valeur de l'octet à l'offset `+232` (`use_secondary`) :
* Si `use_secondary == 0`, le pointeur vers la chaîne de texte choisie pointe vers le début de `primary_text` à `+164`.
* Si `use_secondary != 0`, le pointeur est redirigé vers `aux_text` à `+192`.

Une fois le buffer sélectionné, la méthode extrait le pointeur de caractères CP949 brut en se conformant à la structure d'optimisation de petites chaînes (SSO) de MSVC :
* Si la capacité de la chaîne (`_Myres`, situé à l'offset relatif `+24` de la structure de chaîne) est inférieure à 16 (`0x10`), le pointeur pointe directement vers la zone tampon locale `_Buf` (offset relatif `+4` de la structure de chaîne).
* Sinon, les caractères sont alloués sur le tas, et le pointeur est extrait en lisant l'adresse stockée à l'offset relatif `+4` de la structure de chaîne (qui correspond alors au membre pointeur `_Ptr`).

### 4.2 Calcul des Paramètres d'Affichage
1. **Couleur finale (ARGB) :**
   La méthode extrait l'alpha actuel (`+0x04`) et le combine avec les composants RGB de teinte à l'offset `+0x0C` pour forger la couleur ARGB définitive :
   $$\text{ColorARGB} = (\text{Alpha} \ll 24) \mid (\text{TintRGB} \ \& \ \text{0xFFFFFF})$$
2. **Calcul des Coordonnées Relatives :**
   Les décalages locaux définis à l'offset `+220` (`textOffsetX`) et `+224` (`textOffsetY`) sont extraits et ajoutés aux coordonnées mondiales absolues calculées du widget (`world_x` à `+0x2C` / `44` et `world_y` à `+0x30` / `48`) :
   $$\text{RenderX} = \text{world\_x} + \text{textOffsetX}$$
   $$\text{RenderY} = \text{world\_y} + \text{textOffsetY}$$
3. **Appel de Dessin :**
   Le système récupère le gestionnaire de polices de l'application via `FontTable_GetSingleton()`.
   Puis, elle invoque la fonction utilitaire du système graphique `GUWidget_DrawTextInRect` avec les arguments suivants :
   * Le pointeur vers la table globale de polices (récupérée par le singleton).
   * La coordonnée X absolue (`RenderX`).
   * La coordonnée Y absolue (`RenderY`).
   * Le pointeur vers la chaîne de caractères (codée en CP949).
   * La couleur ARGB calculée.
   * L'alignement du texte configuré à l'offset `+228` (`textAlignment`).
   * Un indicateur d'activation du rendu de texte (`1`).
