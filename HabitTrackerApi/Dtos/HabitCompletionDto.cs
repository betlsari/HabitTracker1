namespace Dtos;

    public class HabitCompletionDto
    {
        public int Id { get; set; }
        public int HabitId { get; set; }
        public DateTime CompletionDate { get; set; }

        public int Amount { get; set; }

        
    }
