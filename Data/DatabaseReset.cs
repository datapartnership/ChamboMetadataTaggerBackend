using Microsoft.EntityFrameworkCore;

namespace MetadataTagging.Data;

public static class DatabaseReset
{
    /// <summary>
    /// Removes every table (including the migrations history) so the next MigrateAsync()
    /// rebuilds the schema from scratch. On PostgreSQL the tables are dropped individually
    /// rather than dropping the database, because the app's (managed identity) role
    /// typically may not drop or create databases, only the tables it owns.
    /// </summary>
    public static async Task DropAllTablesAsync(ApplicationDbContext dbContext)
    {
        if (!dbContext.Database.IsNpgsql())
        {
            await dbContext.Database.EnsureDeletedAsync();
            return;
        }

        await dbContext.Database.ExecuteSqlRawAsync("""
            DO $$
            DECLARE r record;
            BEGIN
                FOR r IN SELECT tablename FROM pg_tables WHERE schemaname = 'public' LOOP
                    EXECUTE format('DROP TABLE IF EXISTS public.%I CASCADE', r.tablename);
                END LOOP;
                FOR r IN SELECT sequence_name FROM information_schema.sequences WHERE sequence_schema = 'public' LOOP
                    EXECUTE format('DROP SEQUENCE IF EXISTS public.%I CASCADE', r.sequence_name);
                END LOOP;
            END $$;
            """);
    }
}
