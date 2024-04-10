using Microsoft.EntityFrameworkCore;
using Aire.Memory.Models.SQL;
using Microsoft.EntityFrameworkCore.Design;

namespace Aire.Memory
{
    [Obsolete("Will be removed after migration to Azure Storage")]
    public class DatabaseContext : DbContext
    {
        public DatabaseContext(DbContextOptions<DatabaseContext> options)
            : base(options)
        {}

        public DbSet<ChatLogEntity> ChatLogs { get; set; }
        public DbSet<QuestionnaireEntity> Questionnaires { get; set; }
        public DbSet<QuestionnaireResultsEntity> QuestionnaireResults { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder
                .UseNpgsql(AireEnvironment.DatabaseConnectionString)
                .UseSnakeCaseNamingConvention();
                
            base.OnConfiguring(optionsBuilder);
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder
                .Entity<ChatLogEntity>()
                .ToTable("ChatLogs")
                .HasKey(e => e.Id);


            modelBuilder
                .Entity<QuestionnaireEntity>()
                .ToTable("Questionnaires")
                .HasKey(e => e.Id);

            modelBuilder
                .Entity<QuestionnaireResultsEntity>()
                .ToTable("QuestionnaireResults")
                .HasKey(e => e.Id);
        }
    }

    public class DatabaseContextFactory : IDesignTimeDbContextFactory<DatabaseContext>
    {
        public DatabaseContext CreateDbContext(string[] args)
        {
            var optionsBuilder = new DbContextOptionsBuilder<DatabaseContext>()
                .UseNpgsql(AireEnvironment.DatabaseConnectionString)
                .UseSnakeCaseNamingConvention();
            return new DatabaseContext(optionsBuilder.Options);
        }
    }
}
