using Microsoft.EntityFrameworkCore;
using Data;


var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddSwaggerGen();
builder.Services.AddControllers();
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));



var app = builder.Build();

app.UseSwagger();
  app.UseSwaggerUI();

app.UseHttpsRedirection();
app.MapControllers();




app.Run();



