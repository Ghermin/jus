# Jus

Le pourcentage de batterie de ta souris, ton casque, tes écouteurs, ta manette et ton clavier, toujours visible dans la barre des tâches de Windows.

**Page du projet et téléchargement : https://ghermin.github.io/jus/**

- Un seul exe d'environ 200 Ko, sans fenêtre administrateur.
- Passe en rouge et prévient sous 20 %.
- Aucun compte, aucune télémétrie : rien ne quitte le PC.

## Appareils

| Famille | Appareils | État |
| --- | --- | --- |
| Logitech | Souris et claviers sur récepteur Bolt, Lightspeed ou Unifying | Testé (Lift, Bolt) |
| SteelSeries | Casques Arctis Nova 7 | Testé |
| Bluetooth | Tout appareil dont Windows connaît la batterie | Lecture testée |
| Xbox et compatibles | Manettes XInput sans fil, par paliers | À confirmer |
| PlayStation | DualSense, DualSense Edge, DualShock 4 | À confirmer |
| Razer | Souris et claviers sans fil | Expérimental |
| HID standard | Appareils qui déclarent leur batterie, onduleurs compris | À confirmer |
| PC portable | La batterie de l'ordinateur | À confirmer |

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
