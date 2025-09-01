using BotApp.Models;
using Microsoft.EntityFrameworkCore;

namespace BotApp.Data
{
    public class BotDbContext : DbContext
    {
        public BotDbContext(DbContextOptions<BotDbContext> options) : base(options) { }

        public DbSet<Session> Sessions => Set<Session>();
        public DbSet<Message> Messages => Set<Message>();
        public DbSet<EventLog> Events => Set<EventLog>();
        public DbSet<Denuncia> Denuncias => Set<Denuncia>();
        public DbSet<Expediente> Expedientes => Set<Expediente>();
        public DbSet<SyncRun> SyncRuns => Set<SyncRun>();
        public DbSet<SyncRunError> SyncRunErrors => Set<SyncRunError>();

        protected override void OnModelCreating(ModelBuilder b)
        {
            // snake_case para Postgres (opcional pero recomendable)
            b.HasPostgresExtension("pgcrypto");
            b.UseSerialColumns(); // si querés identity

            b.Entity<Session>(e =>
            {
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()"); // si usás pgcrypto
                e.HasIndex(x => new { x.Channel, x.ChannelUserId });
                e.Property(x => x.CreatedAtUtc).HasColumnType("timestamptz");
                e.Property(x => x.LastActivityUtc).HasColumnType("timestamptz");
            });

            b.Entity<Message>(e =>
            {
                e.HasKey(x => x.Id);
                e.HasIndex(x => new { x.SessionId, x.CreatedAtUtc });
                e.Property(x => x.CreatedAtUtc).HasColumnType("timestamptz");
            });

            b.Entity<EventLog>(e =>
            {
                e.HasKey(x => x.Id);
                e.HasIndex(x => new { x.Type, x.CreatedAtUtc });
                e.Property(x => x.CreatedAtUtc).HasColumnType("timestamptz");
            });

            b.Entity<Denuncia>(e =>
            {
                e.HasKey(x => x.Id);
                e.HasIndex(x => x.CreatedAtUtc);
                e.Property(x => x.CreatedAtUtc).HasColumnType("timestamptz");
            });

            b.Entity<Expediente>(e =>
            {
                e.HasKey(x => x.Numero); // tu clave natural
                e.HasIndex(x => x.LastModifiedUtc);
                e.Property(x => x.LastModifiedUtc).HasColumnType("timestamptz");
            });

            b.Entity<SyncRun>(e =>
            {
                e.HasKey(x => x.Id);
                e.HasIndex(x => x.StartedAtUtc);
            });

            b.Entity<SyncRunError>(e =>
            {
                e.HasKey(x => x.Id);
                e.HasIndex(x => x.SyncRunId);
            });
        }
    }
}
