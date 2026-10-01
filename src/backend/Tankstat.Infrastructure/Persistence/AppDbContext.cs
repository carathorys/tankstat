using Microsoft.EntityFrameworkCore;

namespace Tankstat.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options);
