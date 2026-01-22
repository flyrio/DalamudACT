using System;
using System.Collections.Generic;

namespace DalamudACT;

internal sealed class ActMcpEncounterSnapshot
{
    public string Zone { get; }
    public string Title { get; }
    public long StartTimeMs { get; }
    public long EndTimeMs { get; }
    public IReadOnlyDictionary<string, ActMcpCombatantSnapshot> CombatantsByName { get; }

    public ActMcpEncounterSnapshot(
        string zone,
        string title,
        long startTimeMs,
        long endTimeMs,
        IReadOnlyDictionary<string, ActMcpCombatantSnapshot> combatantsByName)
    {
        Zone = zone;
        Title = title;
        StartTimeMs = startTimeMs;
        EndTimeMs = endTimeMs;
        CombatantsByName = combatantsByName;
    }

    public float DurationSeconds()
    {
        if (StartTimeMs <= 0 || EndTimeMs <= 0) return 1f;
        var deltaMs = EndTimeMs - StartTimeMs;
        if (deltaMs <= 0) return 1f;
        var seconds = (float)(deltaMs / 1000.0);
        return seconds < 1f ? 1f : seconds;
    }

    public bool TryGetCombatant(string name, out ActMcpCombatantSnapshot combatant)
        => CombatantsByName.TryGetValue(name, out combatant);
}

internal readonly struct ActMcpCombatantSnapshot
{
    public long Damage { get; }
    public double EncDps { get; }
    public double Dps { get; }

    public ActMcpCombatantSnapshot(long damage, double encDps, double dps)
    {
        Damage = damage;
        EncDps = encDps;
        Dps = dps;
    }
}

