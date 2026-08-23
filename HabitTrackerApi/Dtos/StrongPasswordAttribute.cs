// HabitTrackerApi/Dtos/StrongPasswordAttribute.cs
using System.ComponentModel.DataAnnotations;

namespace Dtos;


public sealed class StrongPasswordAttribute : ValidationAttribute
{
    public const int MinimumLength = 12;

    public StrongPasswordAttribute()
    {
        ErrorMessage = $"Şifre en az {MinimumLength} karakter olmalı ve en az bir büyük harf, bir küçük harf ve bir rakam içermelidir.";
    }

    public override bool IsValid(object? value)
    {
        if (value is not string password)
        {
            
            return true;
        }

        if (password.Length < MinimumLength)
        {
            return false;
        }

        bool hasUpper = false, hasLower = false, hasDigit = false;
        foreach (var c in password)
        {
            if (char.IsUpper(c)) hasUpper = true;
            else if (char.IsLower(c)) hasLower = true;
            else if (char.IsDigit(c)) hasDigit = true;
        }

        return hasUpper && hasLower && hasDigit;
    }
}