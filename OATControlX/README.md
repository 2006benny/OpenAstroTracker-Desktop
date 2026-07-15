# OATControlX — utilitaire de mise en service OAT/OAE pour Linux

Application native Linux (.NET 8 + Avalonia 11) pour la mise en service, la
calibration et le diagnostic des montures OpenAstroTracker / OpenAstroExplorer.
Le contrôle du télescope pour l'imagerie (GOTO, suivi, alignement polaire assisté)
est délégué à KStars/Ekos via le driver INDI `lx200_OpenAstroTech`.

![Thème sombre + mode rouge vision nocturne]

## Fonctionnalités

- **Connexion** série (`/dev/ttyUSB*`, `/dev/ttyACM*`, baud configurable) ou TCP (`ip:port`)
- **Contrôle manuel des axes** : N/S/E/W maintenus (`:Mx#`/`:Qx#`), 4 vitesses, arrêt d'urgence
- **Position en direct** : RA/DEC, positions steppers, état des moteurs, suivi on/off (poll `:GX#` à 1 Hz)
- **Nudges AZ/ALT** en arcminutes pour l'alignement polaire motorisé (`:MAZ`/`:MAL`)
- **Homing** : go home, set home, park, autohome par capteurs Hall RA/DEC avec offsets
- **Calibration** : pas/degré RA & DEC, facteur de vitesse (lecture/écriture EEPROM)
- **Diagnostic** : firmware, carte, steppers, drivers, addons, température
- **Console** LX200/OAT brute avec choix du type de réponse
- **Maintenance** : reset EEPROM (avec confirmation)
- **Thèmes** : sombre astronomie + **mode rouge vision nocturne** (bascule à chaud)

## Installation

```bash
git clone https://github.com/OpenAstroTech/OpenAstroTracker-Desktop.git
cd OpenAstroTracker-Desktop
./OATControlX/install.sh
```

Le script vérifie le SDK .NET 8 (et indique comment l'installer selon la distro),
compile en Release self-contained, installe dans `~/.local/share/oatcontrolx`,
crée le lanceur `oatcontrolx` dans `~/.local/bin`, l'entrée de menu
(Applications → Science, avec le logo OAT) et vérifie l'appartenance au groupe
`dialout` pour l'accès au port série. Aucun sudo requis.

Désinstallation : `./OATControlX/install.sh --uninstall`

## Développement

```bash
dotnet build OATControlX.sln
dotnet run --project OATControlX
```

## Accès au port série sous Linux

```bash
sudo usermod -aG dialout $USER   # puis se déconnecter/reconnecter
```

## Paramètres

Stockés dans `~/.config/OpenAstroTracker/OATControlX.json` (thème, dernier
périphérique, baud). Les logs vont dans `~/.config/OpenAstroTracker/`.

## Architecture

- `OATCommunications` — bibliothèque protocole existante (netstandard2.0), inchangée
- `OATCommunications.CrossPlatform` — handler série multi-plateforme (System.IO.Ports)
  et factory sans dépendance WPF
- `OATControlX` — interface Avalonia (ce projet)

## Tester sans matériel

Un simulateur de monture minimal est fourni :

```bash
python3 OATControlX/tools/oat_sim.py    # écoute sur 127.0.0.1:4030
```

Puis dans OATControlX, renseigner `127.0.0.1:4030` dans le champ TCP et connecter.
