PotencyUpdater

- Reads local FFXIV sqpack data to identify combat actions and status names.
- Fetches English action descriptions from https://beta.xivapi.com to parse potencies.
- Rewrites SkillPot and DotPot in DalamudACT/Potency.cs between auto-generated markers.
- (Optional) Updates DotPot from ff14mcp's dots_by_job.json without contacting XIVAPI.

Usage
  dotnet run --project tools/PotencyUpdater -c Release -- --game "D:\\...\\sqpack"

Update DotPot from ff14mcp
  dotnet run --project tools/PotencyUpdater -c Release -- --ff14mcp-dots "C:\\Users\\<you>\\ff14-mcp" --potency "DalamudACT\\Potency.cs"

Notes
- If --game is omitted, it attempts to resolve the path from XIVLauncherCN argReader.log.
- Missing DOT status mappings are listed in DalamudACT/Potency.update.warnings.txt.
- Add manual overrides in tools/PotencyUpdater/Program.cs (ActionDotStatusOverrides) if needed.
