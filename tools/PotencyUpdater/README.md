PotencyUpdater

- Reads local FFXIV sqpack data to identify combat actions and status names.
- Fetches English action descriptions from https://beta.xivapi.com to parse potencies.
- Rewrites SkillPot and DotPot in DalamudACT/Potency.cs between auto-generated markers.

Usage
  dotnet run --project tools/PotencyUpdater -c Release -- --game "D:\\...\\sqpack"

Notes
- If --game is omitted, it attempts to resolve the path from XIVLauncherCN argReader.log.
- Missing DOT status mappings are listed in DalamudACT/Potency.update.warnings.txt.
- Add manual overrides in tools/PotencyUpdater/Program.cs (ActionDotStatusOverrides) if needed.
