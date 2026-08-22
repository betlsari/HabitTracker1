namespace Models;

public static class PetHatching
{
    
    public const int HatchXpThreshold = 30;

   
    public static bool TryHatch(Pet pet)
    {
        if (pet.Stage == PetStage.Egg && pet.Xp >= HatchXpThreshold)
        {
            pet.Stage = PetStage.Hatched;
            pet.HatchedAt = DateTime.UtcNow;
            pet.Mood = "Happy";
            return true;
        }

        return false;
    }
}