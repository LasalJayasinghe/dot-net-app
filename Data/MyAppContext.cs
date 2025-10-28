using dotnetApp.Models;
using Microsoft.EntityFrameworkCore;

namespace dotnetApp.data
{
    public class MyAppContext : DbContext
    {
        public MyAppContext(DbContextOptions<MyAppContext> options): base(options)
        {
        }

        public DbSet<Item> Items { get; set; }
    }
}