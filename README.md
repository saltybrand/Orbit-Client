<img width="1983" height="793" alt="exec-9d79665b-605f-43c5-aafe-cfe070369a98" src="https://github.com/user-attachments/assets/c463bcc8-8a4c-4bbd-aa15-7eb7dede9414" />

# Orbit Beta

**Your Gorilla Tag setup, in one place.**

Orbit is a Windows desktop launcher and mod manager for Gorilla Tag. Launch the game, manage your mods, customize your profile, and keep your cape collection together with the Orbit companion.

[Download beta builds](https://github.com/saltybrand/Orbit-Client/releases) · [Report a bug](https://github.com/saltybrand/Orbit-Client/issues)

> Orbit is in beta. Features and compatibility may change between releases.

## Features

- **Game launcher** — Launch and stop Gorilla Tag from the Home page.
- **Discover** — Browse mod listings and install mods from the catalog.
- **Installed mods** — Inspect your plugins and enable or disable them.
=======
- **Installed mods** — Inspect your plugins.
- **BepInEx setup** — Install or repair the loader while preserving existing plugins.
- **Orbit profiles** — Choose a username, profile picture, and bio.
- **Capes** — Browse the cape shop, manage owned capes, and redeem cape keys.
- **In-game companion** — Access Orbit in Gorilla Tag and display your equipped cape.
- **Snapshots** — Back up and restore your mod setup and Orbit preferences.
- **Steam accounts** — View and switch between local Steam accounts.
- **Appearance** — Choose themes, accents, backgrounds, text scaling, and compact navigation.
- **News and service status** — Read announcements and check whether Orbit services are available.

## Requirements

- A **64-bit Windows PC**.
- An installed PC copy of **Gorilla Tag** through Steam.
- Administrator permission to run the installer.
- An internet connection for accounts, catalogs, downloads, and other online features.

The packaged installer includes the .NET runtime. You do not need to install the .NET SDK to use Orbit.

## Installation

1. Download `OrbitSetup.exe` from the [Releases page](https://github.com/saltybrand/Orbit-Client/releases).
2. Run the installer and choose **Install**.
3. Open **Orbit Client** from the Start menu.
4. Open **Settings** and check your Gorilla Tag installation folder.
5. Use the **BepInEx** page to install or repair the loader if needed.
6. Return to **Home** and launch Gorilla Tag.

Keep Gorilla Tag closed when installing or updating mods and the Orbit companion. If you change your game folder or install BepInEx, restart Orbit with the game closed so it can check the companion installation.

The current installer is unsigned, so Windows may display an unknown-publisher warning. Check that the installer came from this repository's Releases page. A SHA-256 checksum is included with packaged builds for file verification.

## Getting started

### Mods

Open **Browse mods** to explore Discover, then use **Installed mods** to manage your plugins. Create a snapshot before making major changes to your setup. Mod compatibility depends on the game version and each mod's requirements.

Community mod downloads are checked against their approved SHA-256 hashes before installation. This verifies the downloaded file matches the approved release; it does not guarantee compatibility or safety in every situation.

=======
### Profiles and capes

Open **Profile** to create an Orbit account or sign in. Complete email confirmation when prompted, then customize your profile.

Use **Capes** to browse available capes, redeem a key, and manage your collection. Cape ownership and redemption are verified by the backend. Cape keys are single-use.

With the companion installed, press **F4** in Gorilla Tag to open Orbit's in-game panel.

### Backups

Use **Snapshots** to save your current setup before changing mods or configuration. Restoring a snapshot also creates a recovery snapshot of the setup being replaced.

## Beta limitations

- Discord sign-in and password recovery depend on backend configuration and may be unavailable.
- Online features require access to Orbit's services.
- Updates to Gorilla Tag or third-party mods can affect compatibility.
- The installer is currently unsigned.

## Reporting bugs

Open an [issue](https://github.com/saltybrand/Orbit-Client/issues) and include:

- Your Orbit version and Windows version.
- What you were trying to do.
- Steps to reproduce the problem.
- What you expected and what happened instead.
- Relevant screenshots or error messages.
- Remove passwords, tokens, redemption keys, and other private information before attaching logs or screenshots. Avoid posting exploit details publicly; **use GitHub's private vulnerability reporting**.
=======

## Uninstalling

Remove **Orbit Client** through **Windows Settings → Apps**. Orbit stores preferences and sign-in data separately from the application installation.

---

Orbit is an independent community project and is not affiliated with or endorsed by Gorilla Tag, Another Axiom, Steam, or Valve.
