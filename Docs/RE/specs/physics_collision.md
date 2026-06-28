# Spécification Technique : Physique et Collisions 3D

Ce document décrit l'implémentation de la physique et des intersections 3D dans le client Martial Heroes. Il détaille le raycasting du terrain basé sur un Quadtree, les algorithmes géométriques fondamentaux (AABB, Sphère, Triangle) et le format d'enregistrement des collisions 2D/3D (`.sod`).

---

## 1. Raycasting de la Hauteur du Terrain par Quadtree

Le calcul de la hauteur du terrain et le picking du sol reposent sur une structure de partitionnement spatial bidimensionnelle (Quadtree) intégrée aux cellules du terrain (`TerrainCell`).

### 1.1 Algorithme `TerrainCell_RaycastQuadtree` (0x43cc8f)
Cette fonction effectue une descente récursive sur le Quadtree de la cellule courante pour localiser le subtile de feuille contenant l'intersection.

#### Signature
```cpp
float* __thiscall TerrainCell_RaycastQuadtree(
    int this,             // Pointeur vers TerrainCell
    int nodeIdx,          // Index du nœud courant dans le Quadtree
    int depth,            // Profondeur actuelle (décroissante de N à 1)
    float cellX,          // Coordonnée X de la cellule (décalage de grille)
    float* cellZ,         // Pointeur ou décalage de la coordonnée Z
    unsigned int size,    // Taille actuelle de la cellule/quadrant (en unités de grille)
    float* rayData,       // Rayon : [origin.x, origin.y, origin.z, dir.x, dir.y, dir.z]
    float* outHitPoint    // Point d'impact résultant
);
```

#### Logique de Fonctionnement
1. **Intersection AABB du Nœud** : La fonction appelle `sub_43C100` (qui vérifie l'intersection du rayon avec l'AABB du nœud courant, située à l'adresse `16 * nodeIdx + this + 10756`). Si le rayon n'intersecte pas l'AABB du nœud, la branche est éliminée immédiatement (`result = nullptr`).
2. **Cas de Base (Feuille de Profondeur, `depth == 1`)** :
   - Si le rayon intersecte l'AABB du nœud feuille, la fonction appelle `TerrainCell_RaycastGroundReturnHit` (`0x44a29a`) pour calculer l'intersection précise avec les triangles du sol du subtile.
   - Elle appelle ensuite `sub_45B332` pour finaliser et renvoyer le point d'impact.
3. **Cas Récursif (`depth > 1`)** :
   - La taille du quadrant est divisée par 2 : `halfSize = size >> 1`.
   - L'index du premier enfant est calculé par la formule d'arbre 4-aire : `firstChildIdx = 4 * nodeIdx + 1`.
   - La fonction boucle sur les 4 quadrants enfants (`v10` de `0` à `3`) :
     - Les coordonnées de base de l'enfant $XZ$ sont calculées en fonction du quadrant :
       - $\text{ChildX} = \text{cellX} + \text{halfSize} \times (v10 \pmod 2)$
       - $\text{ChildZ} = \text{cellZ} + \text{halfSize} \times (v10 / 2)$
     - Elle invoque récursivement `TerrainCell_RaycastQuadtree` (nommée `TerrainCell_RaycastQuadtreeGroundHit` dans le moteur) pour chaque enfant avec une profondeur réduite de 1 (`depth - 1`).

---

### 1.2 Algorithme `TerrainCell_RaycastGroundReturnHit` (0x44a29a)
Cette fonction gère le raycasting final sur la géométrie du sol d'un subtile de terrain.

#### Signature
```cpp
char __thiscall TerrainCell_RaycastGroundReturnHit(
    int this,             // Pointeur vers le subtile ou fragment de cellule
    float* rayData,       // Rayon : [origin.x, origin.y, origin.z, dir.x, dir.y, dir.z]
    float* outHitPoint    // Point d'impact / distance
);
```

#### Logique de Fonctionnement
Le subtile peut être sous deux formes, déterminées par le drapeau à l'offset `this + 1132` :
1. **Subtile Simple (Plat/Sans Détails, `*(this + 1132) == 0`)** :
   - Le sol est traité comme un simple quad formé de ses 4 coins.
   - Les sommets de coin ont un pas (stride) de 44 octets. Les indices de sommets utilisés sont :
     - Sommet 0 : `this + 4` (Coin supérieur gauche)
     - Sommet 4 : `this + 180` (Coin supérieur droit, offset $4 + 4 \times 44$)
     - Sommet 20 : `this + 884` (Coin inférieur gauche, offset $4 + 20 \times 44$)
     - Sommet 24 : `this + 1060` (Coin inférieur droit, offset $4 + 24 \times 44$)
   - Le quad est divisé en 2 triangles :
     - Triangle 1 : Sommets `[0, 4, 20]`
     - Triangle 2 : Sommets `[20, 4, 24]`
   - La fonction teste l'intersection sur le Triangle 1 via `Geom_RaycastTriangleReturnHit` (`0x449da6`). S'il n'y a pas d'impact, elle teste le Triangle 2.
2. **Subtile Détaillé (`*(this + 1132) != 0`)** :
   - La fonction parcourt la liste partagée d'indexation de triangles `unk_7B22D4` (stride de 6 octets, soit 3 index `unsigned short` de sommets par triangle).
   - Pour chaque triangle, elle résout les trois sommets physiques :
     - Sommet A : `44 * idxA + this + 4`
     - Sommet B : `44 * idxB + this + 4`
     - Sommet C : `44 * idxC + this + 4`
   - Elle effectue le test d'intersection `Geom_RaycastTriangleReturnHit`. La boucle s'arrête dès qu'un triangle valide est touché (retourne `1`).

---

## 2. Algorithmes d'Intersection Géométrique de Diamond

### 2.1 Intersection Rayon contre AABB : `Ray_IntersectAABB` (0x44e07a)
Utilisé principalement pour les collisions d'objets physiques et les bounding boxes complexes dans le monde.

#### Signature
```cpp
int __thiscall Ray_IntersectAABB(
    float* aabb,    // AABB : [min.x, min.y, min.z, max.x, max.y, max.z]
    int ray         // Rayon : [origin.x, origin.y, origin.z, dir.x, dir.y, dir.z, tMax]
);
```

#### Algorithme (Méthode des Dalles de Kay-Kajiya)
1. **Validation Initiale** : Vérifie la validité de l'AABB (`aabb[0] <= aabb[3]`, soit `minX <= maxX`). Si invalide, retourne `0`.
2. **Classification des Dalles (Slabs)** :
   Pour chaque axe $i \in \{0, 1, 2\}$ ($X, Y, Z$) :
   - Si l'origine du rayon $O_i < \text{min}_i$, le rayon est à l'extérieur (côté négatif) : la face candidate est $\text{min}_i$. Un drapeau d'axe est défini à `1`.
   - Si l'origine du rayon $O_i > \text{max}_i$, le rayon est à l'extérieur (côté positif) : la face candidate est $\text{max}_i$. Le drapeau d'axe est défini à `0`.
   - Si l'origine $O_i$ est comprise entre $\text{min}_i$ et $\text{max}_i$, le rayon est à l'intérieur de la dalle pour cet axe. Le drapeau d'axe est défini à `2`.
3. **Cas de l'Origine Interne** : Si tous les axes portent le drapeau `2`, l'origine du rayon est à l'intérieur de l'AABB. La fonction retourne immédiatement `2` (collision interne).
4. **Calcul des Distances d'Entrée $t$** :
   Pour chaque axe, si l'axe n'est pas "interne" et que la direction du rayon $D_i \neq 0.0$ :
   $$t_i = \frac{\text{faceCandidate}_i - O_i}{D_i}$$
   Si l'axe est interne ou $D_i == 0.0$, $t_i = -1.0$.
5. **Recherche de la Dalle d'Entrée** :
   Trouve l'axe $k$ qui présente la plus grande valeur de $t$ positive ($t_{\text{entree}}$).
   - Si $t_{\text{entree}} < 0.0$ ou $t_{\text{entree}} > t_{\text{Max}}$ (longueur du segment de rayon), il n'y a pas de collision. Retourne `0`.
6. **Validation sur les Autres Axes** :
   Pour les axes $j \neq k$, calcule le point d'impact potentiel :
   $$\text{coord}_j = O_j + t_{\text{entree}} \times D_j$$
   Si $\text{coord}_j < \text{min}_j$ ou $\text{coord}_j > \text{max}_j$, le point d'impact se situe en dehors des limites de la face de l'AABB. La collision est rejetée (retourne `0`).
7. **Mise à Jour** : Si valide, met à jour la longueur max du rayon $t_{\text{Max}} = t_{\text{entree}}$ et retourne `1`.

---

### 2.2 Intersection Rayon contre Sphère : `Diamond_GBoundingSphere_IntersectSegment` (0x41703b)
Utilisé pour le frustum culling et la détection d'interactivité rapide.

#### Structure de Données
- **Bounding Sphere** (`this`) :
  - `this[0]` : Rayon $R$
  - `this[1..3]` : Centre de la sphère $C = (C_x, C_y, C_z)$
- **Ray/Segment** (`a2`) :
  - `a2[0..2]` : Origine $O = (O_x, O_y, O_z)$
  - `a2[3..5]` : Direction normalisée $D = (D_x, D_y, D_z)$
  - `a2[6]` : Distance maximale $t_{\text{Max}}$

#### Algorithme
1. **Validation de la Sphère** :
   - Si $R == 0.0$, retourne `0` (aucun volume).
   - Si $R < 0.0$, retourne `2` (sphère inversée/infinie).
2. **Calcul de Distance du Centre** :
   Calcule le vecteur entre l'origine du rayon et le centre de la sphère : $V = C - O$.
   Calcule la distance au carré $L^2 = |V|^2$.
   - Si $R^2 \ge L^2$, l'origine du rayon est à l'intérieur (ou sur la surface) de la sphère. Retourne `2`.
3. **Projection et Culling Initial** :
   Calcule la projection du centre sur le rayon : $t_{\text{proj}} = V \cdot D$ (via la fonction `sub_416FE1`).
   - Si $t_{\text{proj}} < 0.0$ (la sphère est derrière le rayon), retourne `0`.
   - Si $t_{\text{proj}} > t_{\text{Max}} + R$ (la sphère est trop éloignée pour être atteinte par le segment), retourne `0`.
4. **Calcul de l'Impact Interne** :
   Calcule la distance orthogonale minimale au carré entre le centre de la sphère et la droite du rayon :
   $$d^2 = L^2 - t_{\text{proj}}^2$$
   $$h^2 = R^2 - d^2 = t_{\text{proj}}^2 + R^2 - L^2$$
   - Si $h^2 < 0.0$, la droite du rayon passe à côté de la sphère. Retourne `0`.
5. **Résolution de la Racine Carrée** :
   Si $h^2 == 0.0$, $h = 0.0$.
   Sinon, calcule $h = \sqrt{h^2}$ en utilisant une table de correspondance flottante rapide (Fast Inv-Sqrt Table `dword_858C20`).
6. **Calcul et Clamping du Point d'Entrée** :
   Le point d'impact le plus proche de l'origine se situe à la distance :
   $$t_{\text{impact}} = t_{\text{proj}} - h$$
   - Si $t_{\text{impact}} > t_{\text{Max}}$, retourne `0` (au-delà de la portée du segment).
7. **Mise à Jour** : Met à jour la distance maximale du segment `a2[6] = t_impact` et retourne `1` (collision réussie).

---

### 2.3 Intersection Rayon contre Triangle 3D : `Geom_RaycastTriangleReturnHit` (0x449da6)
Utilisé pour le calcul de précision des clics (picking de mesh) et l'alignement exact au sol.

#### Algorithme (Möller-Trumbore)
Soit un rayon d'origine $O$ et de direction $D$, et un triangle défini par les sommets $V_0, V_1, V_2$ :

1. **Calcul des Vecteurs de Côté** :
   $$E_1 = V_1 - V_0$$
   $$E_2 = V_2 - V_0$$
2. **Calcul du Déterminant** :
   $$P = D \times E_1$$
   $$\text{det} = P \cdot E_2$$
   - Si $\text{det} < 0.0001$, le rayon est parallèle au triangle ou il frappe la face arrière (back-face culling). Retourne `0`.
3. **Calcul des Coordonnées Barycentriques $u, v$** :
   Distance de l'origine au sommet $V_0$ : $T = O - V_0$.
   - **Coordonnée $u$** :
     $$u = \frac{T \cdot P}{\text{det}}$$
     Si $u < 0.0$ ou $u > 1.0$, le point d'impact est en dehors du triangle. Retourne `0`.
   - **Coordonnée $v$** :
     $$Q = T \times E_2$$
     $$v = \frac{D \cdot Q}{\text{det}}$$
     Si $v < 0.0$ ou $u + v > 1.0$, le point d'impact est en dehors du triangle. Retourne `0`.
4. **Calcul de la Distance d'Intersection $t$** :
   $$t = \frac{E_1 \cdot Q}{\text{det}}$$
5. **Mise à Jour** :
   Si $t > 0.0$ et $t < t_{\text{current\_min}}$ (paramètre `a6` en entrée) :
   $$t_{\text{current\_min}} = t$$
   Retourne `1` (impact valide). Sinon, retourne `0`.

---

## 3. Format de Collision Physique .sod

Le format `.sod` contient les contours de collision physiques (murs 2D projetés sur le plan horizontal $XZ$) utilisés pour empêcher les personnages de traverser le décor et pour glisser le long des murs.

### 3.1 Structure du Fichier Binaire
Le fichier `.sod` est entièrement en *little-endian* et ne possède ni en-tête de magie, ni version, ni compression.

```
+------------------------------------+
| solidCount (u32)                   |
+------------------------------------+
| SolidRecord[solidCount]            | -> Lecture groupée en une passe (108 octets par record)
+------------------------------------+
| Bloc des Quads (Répété)            | -> Pour chaque solide i :
| - quadCount (u32)                  |
| - QuadRecord[quadCount]            | -> 48 octets par record
+------------------------------------+
```

#### Structure `SolidRecord` (Stride 108 octets)
- `+0x00` : `aabbMinX` (f32) - Minimum X de l'AABB du solide dans le monde.
- `+0x04` : `aabbMinZ` (f32) - Minimum Z (le deuxième axe est bien Z, pas Y).
- `+0x08` : `aabbMaxX` (f32) - Maximum X.
- `+0x0C` : `aabbMaxZ` (f32) - Maximum Z.
- `+0x10` : Zone tampon à 0 (44 octets) - Utilisée en mémoire pour les pointeurs de nœud de grille et le centre au runtime.
- `+0x3C` : `quadCount` (u32) - Nombre de segments de mur (`QuadRecord`).
- `+0x40` : `quadArrayPtr` (u32) - Pointeur d'origine disque (stale), écrasé en mémoire lors du chargement.
- `+0x44` : Zone tampon à 0 (40 octets) - Usage interne runtime.

#### Structure `QuadRecord` (Stride 48 octets)
Définit un segment de mur orienté avec son équation de droite précalculée :
- `+0x00` : `footprintMinX` (f32) - Minimum X de la boîte de délimitation du segment.
- `+0x04` : `footprintMinZ` (f32) - Minimum Z.
- `+0x08` : `footprintMaxX` (f32) - Maximum X.
- `+0x0C` : `footprintMaxZ` (f32) - Maximum Z.
- `+0x10` : `p0x` (f32) - Extrémité orientée 0, coordonnée X.
- `+0x14` : `p0z` (f32) - Extrémité orientée 0, coordonnée Z.
- `+0x18` : `p1x` (f32) - Extrémité orientée 1, coordonnée X.
- `+0x1C` : `p1z` (f32) - Extrémité orientée 1, coordonnée Z.
- `+0x20` : `slope` (f32) - Pente de la droite $dz/dx$. Zéro si le segment est vertical.
- `+0x24` : `xConst` (f32) - Coordonnée X constante si le segment est vertical (`axisFlag == 1`).
- `+0x28` : `intercept` (f32) - Ordonnée à l'origine $b$ dans la droite $z = \text{slope} \times x + b$.
- `+0x2C` : `axisFlag` (u32) - Indicateur d'axe (`0` = non-vertical, utiliser l'équation $z$ ; `1` = vertical, utiliser $x = \text{xConst}$).

---

### 3.2 Algorithme de Chargement : `Sod_LoadCollisionBlob` (0x458f13)

```cpp
bool __thiscall Sod_LoadCollisionBlob(int this, _BYTE* fileStream, float* cellBounds);
```

#### Déroulement de la Lecture
1. **Nettoyage Mémoire** : Si un tableau de solides existe déjà dans l'objet (`*(this + 148) != 0`), appelle `sub_458EAD` pour libérer la mémoire.
2. **Initialisation de la Grille** : Appelle `sub_65A390` pour réinitialiser la structure spatiale de collision interne de la cellule à partir des dimensions `cellBounds`.
3. **Lecture du Compteur de Solides** : Lit un entier de 4 octets à partir du flux de fichier pour remplir `solidCount` à l'offset `this + 152`.
4. **Allocation du Tableau de Solides** :
   - Calcule la taille nécessaire : `size = 108 * solidCount + 4` (les 4 octets supplémentaires stockent la taille du tableau en-tête).
   - Alloue la mémoire via `operator new` et instancie les constructeurs de `SolidRecord` (qui mettent à zéro les champs internes).
5. **Chargement des Solides** : Lit un bloc contigu de `108 * solidCount` octets depuis le fichier pour remplir d'un coup le tableau de `SolidRecord`.
6. **Boucle de Lecture des Murs (`QuadRecord`)** :
   Pour chaque solide indexé de `0` à `solidCount - 1` :
   - Lit `quadCount` (4 octets) à partir du fichier.
   - Appelle `Sod_AllocSolidQuadArray` qui alloue la mémoire pour `48 * quadCount` octets et injecte le pointeur résultant à l'offset `+64` du solide.
   - Lit les données du fichier directement vers cette adresse allouée (`48 * quadCount` octets).
   - Appelle `Sod_BuildSolidQuadtree` pour construire la structure spatiale d'accélération en mémoire (grille 16x16 partitionnant les quads par leurs coordonnées footprint $XZ$).
7. **Finalisation** : Positionne l'indicateur d'état `*(_BYTE *)(this + 156) = (solidsPointer != 0)` et retourne `true` si le chargement a réussi.
