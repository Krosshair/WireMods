# Publishing Wire's mods

## One-time setup
Double-click `setup.cmd` (or run it in a terminal). It installs Git and the GitHub CLI if missing,
lets you log in to GitHub in your browser, asks for your upload tokens and creates the GitHub repo.

Tokens it asks for (stored as your Windows user environment variables, never in the repo):
- `THUNDERSTORE_TOKEN`: thunderstore.io > team "Wire" > Service Accounts > add one, copy its token.
- `HEXIUM_TOKEN`: hexium.gg > team "Wire" > settings > API token.
- `NEXUSMODS_API_KEY`: https://www.nexusmods.com/settings/api-keys

## Releasing a mod
1. Make your changes in `<Mod>\src`, and edit `<Mod>\package\README.md` / `CHANGELOG.md` as you like.
2. Try it first:  `publish WiresRecycler -Version 1.0.3 -DryRun`
3. Release it:     `publish WiresRecycler -Version 1.0.3`

`-Version` only changes `version_number` in the manifest. Nothing else in the manifest, README or icon is touched.
Only some sites: `-Sites thunderstore,hexium`. Skip the build: `-SkipBuild`.

What `publish` does: builds the mod, copies the DLL into `package\`, zips the package into `_releases\`,
checks the version is newer than what each site has, uploads to Thunderstore, Hexium and Nexus,
then commits, tags (`WiresRecycler-v1.0.3`) and creates a GitHub release with the zip.
The changelog text comes from the matching `## 1.0.3` section of `package\CHANGELOG.md` if there is one.

## Per-mod settings: tools\mods.json
- `publish`: false blocks publishing (Aldr is false while in development).
- `thunderstore`, `thunderstore_categories`: Thunderstore listing and its categories.
- `hexium`: upload to Hexium.
- `nexus_mod_id`: the number in the Nexus URL (`/valheim/mods/4202`). Empty = Nexus skipped.
- `nexus_file_id`: only needed if a Nexus mod page has more than one active file.

A mod that does not exist on a site yet is skipped unless you add `-Force` (so nothing gets created by accident).

## Limits
- Nexus can only add new versions to files that already exist; brand new Nexus pages are made by hand once.
- The Nexus description page is not updated automatically (only the file and its changelog).
