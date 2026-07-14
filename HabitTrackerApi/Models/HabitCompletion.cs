namespace Models;

    public class HabitCompletion
    {
        public int Id { get; set; }
        public int HabitId { get; set; }
        public DateTime CompletionDate { get; set; }

        public int Amount { get; set; }

        public Habit Habit { get; set; } // Navigation property to the Habit entity
    }
