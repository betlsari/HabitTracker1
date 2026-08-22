namespace Models;

public enum PetStage
{
    // Pet henüz yumurta aşamasında: Level artmaz, Mood hesaplanmaz.
    Egg = 0,

    // Yumurta açıldı, pet normal şekilde büyüyüp mood kazanabilir.
    Hatched = 1
}