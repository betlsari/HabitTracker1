namespace Services;


public static class PetLeveling
{
    public static void Apply(Models.Pet pet, int maxLevel)
    {
        if (pet.Stage != Models.PetStage.Hatched)
        {
            return;
        }

        var maxXp = maxLevel * 100;
        if (pet.Xp > maxXp)
        {
            pet.Xp = maxXp;
        }

        pet.Level = Math.Min(pet.Xp / 100, maxLevel);
    }
}