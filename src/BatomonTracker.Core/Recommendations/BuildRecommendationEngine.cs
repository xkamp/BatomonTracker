using BatomonTracker.Core.Models;

namespace BatomonTracker.Core.Recommendations;

public sealed class BuildRecommendationEngine
{
    public BuildRecommendation Evaluate(RunState state)
    {
        var ownedUnits = state.Team
            .Concat(state.Bench)
            .ToArray();
        var ownedCounts = ownedUnits
            .GroupBy(unit => unit.SpeciesId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

        ShopSlot? matchingSlot = state.Shop
            .Where(slot => slot.Unit is not null && ownedCounts.ContainsKey(slot.Unit.SpeciesId))
            .OrderByDescending(slot => ownedCounts[slot.Unit!.SpeciesId])
            .ThenByDescending(slot => slot.Unit!.Level ?? 0)
            .FirstOrDefault();
        if (matchingSlot?.Unit is not null)
        {
            int copies = ownedCounts[matchingSlot.Unit.SpeciesId];
            string reason = copies >= 2
                ? $"Compra {matchingSlot.Unit.SpeciesId}: hai gia {copies} copie, possibile merge."
                : $"Compra {matchingSlot.Unit.SpeciesId}: completa una specie gia presente nella build.";
            return new BuildRecommendation(
                RecommendationAction.Buy,
                matchingSlot.Unit.SpeciesId,
                reason,
                EventConfidence.Deduced);
        }

        if (state.Team.Count == 0)
        {
            ShopSlot? firstUnit = state.Shop.FirstOrDefault(slot => slot.Unit is not null);
            if (firstUnit?.Unit is not null)
            {
                return new BuildRecommendation(
                    RecommendationAction.Buy,
                    firstUnit.Unit.SpeciesId,
                    $"Compra {firstUnit.Unit.SpeciesId}: la build non ha ancora un Batomon attivo.",
                    EventConfidence.Deduced);
            }
        }

        return new BuildRecommendation(
            RecommendationAction.Hold,
            null,
            "Nessuna priorita certa con i dati disponibili.",
            EventConfidence.Deduced);
    }
}
