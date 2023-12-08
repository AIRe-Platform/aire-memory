using Microsoft.EntityFrameworkCore;
using Aire.Memory.Models;

namespace Aire.Memory
{
    public class DatabaseContext : DbContext
    {
        public DatabaseContext(DbContextOptions<DatabaseContext> options)
            : base(options)
        {}

        public DbSet<ChatHistory> ChatLogs { get; set; }
        
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<ChatHistory>().ToTable("ChatLogs");
        }
    }
}