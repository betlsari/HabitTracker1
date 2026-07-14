using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Data;
using Models;
namespace Controllers;


[ApiController]
[Route("api/[controller]")]

public class HabitCompletionsController : ControllerBase
{
    private readonly AppDbContext _context;

    public HabitCompletionsController(AppDbContext context)
    {
        _context = context;
    }

}