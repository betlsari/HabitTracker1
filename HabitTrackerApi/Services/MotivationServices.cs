namespace Services;


public static class MotivationMessages
{
    private static readonly Random _random = new();

    private static readonly string[] GoalReachedTemplates =
    {
        "{0} için hedefini tutturdun. Tebrikler!",
        "Harika gidiyorsun! {0} hedefini bugün de tamamladın.",
        "{0} için bugünü de kazandın, devam et!",
        "Bir adım daha! {0} hedefine ulaştın.",
        "{0} konusunda kendine söz verdin ve tuttun. Tebrikler!",
        "Emek işe yaradı: {0} hedefi tamam!"
    };

    private static readonly string[] BookGoalReachedTemplates =
    {
        "{0} için bugünkü okuma hedefini tutturdun. Tebrikler!",
        "Sayfalar seni bekliyordu, {0} hedefini tamamladın!",
        "Bugün de okudun! {0} için hedefine ulaştın.",
        "{0} ile aran çok iyi, hedefi yine tuttun!"
    };

    private static readonly string[] BookCompletedTemplates =
    {
        "{0} kitabını bitirdin. Tebrikler!",
        "Son sayfayı da çevirdin: {0} tamamlandı!",
        "{0} kitabını başarıyla bitirdin. Yeni bir kitaba ne dersin?",
        "Bir kitap daha raftan indi: {0} tamamlandı!"
    };

    private static readonly string[] StreakKeptTemplates =
    {
        "{0} alışkanlığında {1} {2} zincirini sürdürüyorsun!",
        "{1} {2} boyunca hiç aksatmadın: {0}. Devam et!",
        "Serin büyüyor: {0} için {1} {2} art arda başarı!"
    };

    public static string GoalReached(string habitName) => Format(GoalReachedTemplates, habitName);

    public static string BookGoalReached(string bookTitle) => Format(BookGoalReachedTemplates, bookTitle);

    public static string BookCompleted(string bookTitle) => Format(BookCompletedTemplates, bookTitle);

    public static string StreakKept(string habitName, int streak, string periodLabel)
    {
        var template = StreakKeptTemplates[_random.Next(StreakKeptTemplates.Length)];
        return string.Format(template, habitName, streak, periodLabel);
    }

    private static string Format(string[] templates, string arg)
    {
        var template = templates[_random.Next(templates.Length)];
        return string.Format(template, arg);
    }
}