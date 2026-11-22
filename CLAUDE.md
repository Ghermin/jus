# Jus : notes pour les sessions de développement

Appli Windows qui affiche le niveau de batterie des appareils dans la barre des tâches.

## Compiler / tester

- `pwsh -File build.ps1` → `out\Jus.exe`. Compilateur = Roslyn embarqué dans PowerShell 7, cible .NET Framework 4.8. Ne pas créer de `.csproj`.
- `out\Jus.exe` lancé sans option **s'installe** dans `%LOCALAPPDATA%\Programs\Jus`. Pour un essai : `--portable`.
- `--scan fichier.txt` : ce que dit chaque source et la liste des collections HID. `--preview fichier.png` : rendu de la barre en sombre et en clair. `--preview-8bitdo fichier.png` : la fenêtre du patch 8BitDo. `--quit` : ferme la copie en cours.
- Ne jamais publier une sortie de `--scan` : elle contient les noms et adresses des appareils.

## Organisation

- `src/Sources.cs` : une classe par famille d'appareils (`IBatterySource`). Ajouter un appareil = une source de plus, déclarée dans `BatteryMonitor.sources`.
- `src/BatteryMonitor.cs` : interroge les sources sur un fil à part, toutes les 4 s pour les moins coûteuses, et aussitôt qu'un appareil est branché ou retiré (`DeviceChangeWindow`). Une fenêtre enfant de la barre des tâches partage sa file d'entrée avec l'Explorateur : aucun accès matériel sur le fil de l'interface, sinon la barre des tâches se fige.
- `src/TaskbarWidget.cs` : une fenêtre transparente (`BarWindow`) par barre des tâches : enfant de `Shell_TrayWnd`, à gauche de `TrayNotifyWnd`, et de chaque `Shell_SecondaryTrayWnd` (autres écrans). Sous Windows 11, l'horloge des barres secondaires n'est pas une fenêtre : `TrayEdges` la trouve par UI Automation (éléments `SystemTrayIcon`), sur un fil à part, toutes les 3 s. Le panneau s'ouvre au-dessus du widget cliqué.
- La batterie du PC portable n'est pas affichée : Windows le fait déjà.
- `src/TrayApp.cs` : icône, panneau, alertes, réglages (`%APPDATA%\Jus\settings.txt`).
- `src/MenuTheme.cs` : design du panneau, repris du Gestionnaire RGB. Toutes les lignes sont dessinées à la main (`CustomRow` : `HeaderRow`, `DeviceRow`, `ActionRow`), ramenées au bord par `PanelMenu.OnLayout` et calées sur les mêmes colonnes `Columns` (coche à 20, texte et icônes à 34, à 96 ppp) : le haut et le bas sont alignés à toutes les échelles. Pas de `ToolStripMenuItem` : leur mise en page change avec la mise à l'échelle. L'en-tête affiche la version (`AssemblyVersion`). Couleurs `Palette.Light` / `Palette.Dark` = variables `--pg-*` de `docs/index.html` : les changer aux deux endroits. `--preview-menu fichier.png` le dessine en clair et en sombre.

## Protocoles

- Requêtes en lecture seule uniquement : ne jamais envoyer de commande qui modifie un réglage d'un appareil, ni de commande non documentée pour « voir ce que ça répond ». Seule exception : le patch 8BitDo (`src/EightBitDo.cs`), lancé uniquement après un clic de l'utilisateur dans sa fenêtre.
- 8BitDo Ultimate 2C : avec les firmwares d'origine, aucun niveau lisible, elle s'affiche avec `NoLevel`. Pas d'estimation d'après le temps : l'utilisateur ne veut que des valeurs réelles.
  - Avec une manette et un dongle aux firmwares modifiés (faits pour l'utilisateur, hors de ce dépôt) : la manette (1.09) envoie chaque paquet radio sur 22 octets avec le % en octet 18 et `0xBA` en octet 19 ; le dongle (1.00) les garde et les met dans sa réponse d'identification, **octet 8 = %, octet 9 = `0xBA`** (indices côté Windows, ID de rapport `0x02` compris). `XInputSource.EightBitDoLevel` redemande l'identification toutes les 20 s quand la manette est sur le dongle ; sans signature, `NoLevel` comme avant. Par câble : « branchée ».
  - Pourquoi pas sans modification : le dongle 2,4 GHz ne transmet pas la batterie en jeu (8BitDo ne l'envoie qu'en mode « logiciel », où le dongle n'est plus une manette Xbox) ; Windows.Gaming.Input renvoie un faux 1000/1000 ; XInput dit « filaire ». Le firmware de la manette (Telink B80, v1.09) garde le % en RAM `0x843415` et les mV en `0x843424`, mais la lecture `81 05 05` ne lit que la **flash** (testé sur la manette et le dongle). Référence : [arithon-f/8bitdo-ultimate-2c-toolkit](https://github.com/arithon-f/8bitdo-ultimate-2c-toolkit), `docs/firmware-notes.md`.
  - Identités USB : manette en câble et dongle relié = `2DC8:310A` (XInput, page FF7A) ; dongle seul, manette éteinte = `2DC8:301C`. L'identification `81 05 00 21 01` sur FF7A répond `02 22 <version ×100 ?> … <produit LE en octets 6-7>` : `1B 30` par câble, `1C 30` par le dongle. Un dongle d'usine répond `0x64` en octet 2 et n'a pas la 1.03 connue du toolkit.
  - Requêtes autorisées : identification `81 05 00 21 01` (seule utilisée par Jus) et lecture `81 05 05` (longueur octets 6-7, adresse 14-17, 46 octets max, réponse `02 05 00 .. 05`, données en octet 18). Ne **jamais** envoyer : `81 05 c1` (lève la protection d'écriture), `81 05 00 51 00` (mode mise à jour), `81 05 02/03/04/0b/c4` (écrit/efface la flash, adresse MAC comprise), `81 05 00 38`, `81 05 00 61`, `81 xx 66 AA 63/64`.
  - Patch dans Jus (`src/EightBitDo.cs`, fenêtre `src/EightBitDoWizard.cs`) : même code que l'outil public `github.com/Ghermin/8bitdo-2c-battery-patch` (en local `../8bitdo-2c-battery-patch`), à garder en phase. Une 8BitDo sans niveau (sans fil sans signature, ou par câble sans le patch : 4 octets lus en `0xddd2`) est `Patchable` : la fenêtre s'ouvre seule une fois (`8bitdo=offered` dans les réglages), ensuite l'entrée reste dans le panneau. La fenêtre attend chaque appareil (dongle seul ; manette par câble), sauvegarde (`%LOCALAPPDATA%\Jus\8bitdo\sauvegardes`, privé), télécharge la 1.09 officielle chez 8BitDo (gardée dans ce dossier), vérifie les SHA-256, écrit seulement la zone du programme, relit. `EightBitDo.Busy` coupe les requêtes de `XInputSource` pendant ce temps. Une écriture interrompue (2DC8:3208 présent) est reprise à la prochaine ouverture.
  - Dongle patché et manette d'origine : l'octet 9 porte une somme de contrôle qui peut valoir `0xBA` par hasard. `EightBitDoLevel` exige deux réponses signées de suite.
- Logitech : une souris en veille ne répond plus, mais `LogitechSource` garde sa dernière valeur lue tant que le récepteur est branché (sinon elle disparaissait quelques secondes après chaque pause).
- Manettes virtuelles (ViGEm : DS4Windows et autres) : `HidInfo.IsVirtual` (un ancêtre a l'ID matériel `ViGEmBus`). XInput les voit comme des manettes filaires en double : `XInputSource` en retire autant.
- Panneau : `AutoClose = false`, il ne se ferme que sur un clic en dehors de sa fenêtre (`PanelMenu.Watch`).
- Logitech HID++ : `softwareId` change à chaque requête, sinon une réponse en retard d'une souris en veille passe pour la suivante.
- Arctis Nova 7 : requête `00 B0`, réponse niveau en `b[3]`, état en `b[4]` (0 éteint, 1 en charge).
- Arctis 9 (`1038:12C2`, dongle ; `12C4` d'après HeadsetControl, pas vérifié) : requête d'état `00 20` sur la collection `FFC0` (interface 0), celle de HeadsetControl. Réponse `AA` en `b[1]`, `b[2]` = `01` casque allumé / `03` éteint (le dongle garde alors l'ancien niveau : ne rien afficher), niveau brut en `b[4]` de `0x64` (0 %) à `0x9A` (100 %), état en `b[5]` (`1` en charge ; `0xFF` éteint d'après HeadsetControl, jamais vu). Pas de `00 B0` sur ce casque. Testé sur le dongle `12C2` : allumé `AA 01 01 6F 00` (20 %), éteint `AA 03 01 6F 00`, en charge `AA 01 01 72 01` (le niveau remonte aussitôt, tension de charge).
- Les noms Bluetooth peuvent contenir le prénom du propriétaire : `Naming.Clean` le retire.

## Publier

`AssemblyVersion` dans `src/Program.cs`, commit, étiquette `vX.Y.Z` poussée → `.github/workflows/build.yml` compile et crée la release avec `Jus.exe`.

Toutes les copies installées la récupèrent seules (`src/Updater.cs`) : 2 min après le lancement puis toutes les 6 h, téléchargement dans le dossier d'installation, vérification de la taille et du SHA-256 annoncés par GitHub, puis `Jus-update.exe --updated` remplace l'ancienne version et la relance. Une version publiée doit donc toujours être une version qui marche : elle part chez tout le monde.

Lancement au démarrage : clé Run et tâche planifiée `Jus` à l'ouverture de session (sans droits administrateur). La clé Run seule n'est pas lancée sur tous les Windows.

## Git

Dépôt public `github.com/Ghermin/jus`. Identité des commits : `Ghermin <Ghermin@users.noreply.github.com>`.

## Bored Dev Playground

Ce projet fait partie du Bored Dev Playground : https://ghermin.github.io/ (repo `Ghermin/Ghermin.github.io`, en local `../Ghermin.github.io`).

- `docs/playground.css` est une copie de `kit/playground.css` du playground : modifier le kit là-bas puis lancer `sh kit/sync.sh`. Les couleurs propres à Jus se règlent dans la page, avec les variables `--pg-*`.
- Icône : `brand/icons/jus.svg` du playground, puis `node build.js jus` dans `brand/`.
- Si le nom, la description ou les tags changent, mettre à jour la carte dans `index.html` du playground.
- Garder sur la page le bandeau `pg-strip`, le thème clair / sombre automatique et le responsive. L'appli garde le lien « un projet du bored dev playground » en bas de son menu.
