# Jus

Le pourcentage de batterie de ta souris, ton casque, tes écouteurs, ta manette et ton clavier, toujours visible dans la barre des tâches de Windows.

**Page du projet et téléchargement : https://ghermin.github.io/jus/**

- Un seul exe d'environ 200 Ko, sans fenêtre administrateur.
- Sur la barre des tâches de chaque écran.
- Passe en rouge et prévient sous 20 %.
- Aucun compte, aucune télémétrie : rien ne quitte le PC.

## Appareils

Souris, claviers, casques, écouteurs, manettes. La batterie du PC portable, Windows l'affiche déjà. Jus repère tout seul ce qui est connecté, et un appareil éteint ou retiré disparaît de la barre.

8BitDo Ultimate 2C : en 2,4 GHz, son dongle ne transmet pas la batterie. Jus propose un petit patch du firmware officiel de la manette et du dongle, en un clic, puis affiche le vrai niveau ([détails, retour en arrière](https://github.com/Ghermin/8bitdo-2c-battery-patch)).

Un appareil manque ? Lance `Jus.exe --scan jus-scan.txt` et joins le fichier à une issue, après l'avoir relu : il liste tes appareils.

## Compiler

Il faut PowerShell 7, qui fournit le compilateur C#.

```bash
pwsh -File build.ps1
```

L'exe arrive dans `out\Jus.exe`. Pour l'essayer sans l'installer :

```bash
out\Jus.exe --portable
```

## Licence

MIT. Un projet du [Bored Dev Playground](https://ghermin.github.io/).
