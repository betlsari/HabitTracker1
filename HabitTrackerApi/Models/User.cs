using Microsoft.AspNetCore.Identity;

namespace Models;

public class User :IdentityUser // bu sınıf direkt id,username, email gibi alanları IdentityUser sınıfından alır.
{

    public DateTime CreatedAt { get; set; }

    public List<Habit> Habits { get; set; } = new List<Habit>();

    public int TotalXp { get; set; }

    public List<Pet> Pets { get; set; } = new List<Pet>();
}