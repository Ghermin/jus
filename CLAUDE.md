# Jus : notes pour les sessions de développement

Appli Windows qui affiche le niveau de batterie des appareils dans la barre des tâches.

## Compiler / tester

- `pwsh -File build.ps1` → `out\Jus.exe`. Compilateur = Roslyn embarqué dans PowerShell 7, cible .NET Framework 4.8. Ne pas créer de `.csproj`.
- `out\Jus.exe` lancé sans option **s'installe** dans `%LOCALAPPDATA%\Programs\Jus`. Pour un essai : `--portable`.
- `--scan fichier.txt` : ce que dit chaque source et la liste des collections HID. `--preview fichier.png` : rendu de la barre en sombre et en clair. `--quit` : ferme la copie en cours.
- Ne jamais publier une sortie de `--scan` : elle contient les noms et adresses des appareils.

## Organisation

- `src/Sources.cs` : une classe par famille d'appareils (`IBatterySource`). Ajouter un appareil = une source de plus, déclarée dans `BatteryMonitor.sources`.
- `src/BatteryMonitor.cs` : interroge les sources sur un fil à part, toutes les 4 s pour les moins coûteuses, et aussitôt qu'un appareil est branché ou retiré (`DeviceChangeWindow`). Une fenêtre enfant de la barre des tâches partage sa file d'entrée avec l'Explorateur : aucun accès matériel sur le fil de l'interface, sinon la barre des tâches se fige.
- `src/TaskbarWidget.cs` : fenêtre transparente enfant de `Shell_TrayWnd`, placée à gauche de `TrayNotifyWnd`.
- `src/TrayApp.cs` : icône, panneau, alertes, réglages (`%APPDATA%\Jus\settings.txt`).
- `src/MenuTheme.cs` : design du panneau, repris du Gestionnaire RGB. Couleurs `Palette.Light` / `Palette.Dark` = variables `--pg-*` de `docs/index.html` : les changer aux deux endroits. `--preview-menu fichier.png` le dessine en clair et en sombre.

## Protocoles

- Requêtes en lecture seule uniquement : ne jamais envoyer de commande qui modifie un réglage d'un appareil, ni de commande non documentée pour « voir ce que ça répond ».
- 8BitDo Ultimate 2C : avec les firmwares d'origine, aucun niveau lisible, elle s'affiche avec `NoLevel`. Pas d'estimation d'après le temps : l'utilisateur ne veut que des valeurs réelles.
  - Avec une manette et un dongle aux firmwares modifiés (faits pour l'utilisateur, hors de ce dépôt) : la manette (1.09) envoie chaque paquet radio sur 22 octets avec le % en octet 18 et `0xBA` en octet 19 ; le dongle (1.00) les garde et les met dans sa réponse d'identification, **octet 8 = %, octet 9 = `0xBA`** (indices côté Windows, ID de rapport `0x02` compris). `XInputSource.EightBitDoLevel` redemande l'identification toutes les 20 s quand la manette est sur le dongle ; sans signature, `NoLevel` comme avant. Par câble : « branchée ».
  - Pourquoi pas sans modification : le dongle 2,4 GHz ne transmet pas la batterie en jeu (8BitDo ne l'envoie qu'en mode « logiciel », où le dongle n'est plus une manette Xbox) ; Windows.Gaming.Input renvoie un faux 1000/1000 ; XInput dit « filaire ». Le firmware de la manette (Telink B80, v1.09) garde le % en RAM `0x843415` et les mV en `0x843424`, mais la lecture `81 05 05` ne lit que la **flash** (testé sur la manette et le dongle). Référence : [arithon-f/8bitdo-ultimate-2c-toolkit](https://github.com/arithon-f/8bitdo-ultimate-2c-toolkit), `docs/firmware-notes.md`.
  - Identités USB : manette en câble et dongle relié = `2DC8:310A` (XInput, page FF7A) ; dongle seul, manette éteinte = `2DC8:301C`. L'identification `81 05 00 21 01` sur FF7A répond `02 22 <version ×100 ?> … <produit LE en octets 6-7>` : `1B 30` par câble, `1C 30` par le dongle. Un dongle d'usine répond `0x64` en octet 2 et n'a pas la 1.03 connue du toolkit.
  - Requêtes autorisées : identification `81 05 00 21 01` (seule utilisée par Jus) et lecture `81 05 05` (longueur octets 6-7, adresse 14-17, 46 octets max, réponse `02 05 00 .. 05`, données en octet 18). Ne **jamais** envoyer : `81 05 c1` (lève la protection d'écriture), `81 05 00 51 00` (mode mise à jour), `81 05 02/03/04/0b/c4` (écrit/efface la flash, adresse MAC comprise), `81 05 00 38`, `81 05 00 61`, `81 xx 66 AA 63/64`.
- Manettes virtuelles (ViGEm : DS4Windows et autres) : `HidInfo.IsVirtual` (un ancêtre a l'ID matériel `ViGEmBus`). XInput les voit comme des manettes filaires en double : `XInputSource` en retire autant.
- Panneau : `AutoClose = false`, il ne se ferme que sur un clic en dehors de sa fenêtre (`PanelMenu.Watch`).
- Logitech HID++ : `softwareId` change à chaque requête, sinon une réponse en retard d'une souris en veille passe pour la suivante.
- Arctis Nova 7 : requête `00 B0`, réponse niveau en `b[3]`, état en `b[4]` (0 éteint, 1 en charge).
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
