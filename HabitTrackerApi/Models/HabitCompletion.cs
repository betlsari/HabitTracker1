using System.ComponentModel.DataAnnotations;


namespace Models;

    public class HabitCompletion : IValidatableObject
    {
        public int Id { get; set; }
        public int HabitId { get; set; }
        public DateTime CompletionDate { get; set; }
       
 [Range(0, int.MaxValue)]
        public int Amount { get; set; }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if(CompletionDate> DateTime.UtcNow)
        {
            yield return new ValidationResult("Tamamlama tarihi gelecekte olamaz.", new[] {nameof(CompletionDate)});
        }
    }

        public Habit? Habit { get; set; } 

        public int XpEarned {get; set;}

        
        public int PetStreakBonusXp { get; set; }
    }