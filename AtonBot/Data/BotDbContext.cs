using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using MrBot.Models;

namespace MrBot.Data
{
	public class BotDbContext : IdentityDbContext<ApplicationUser, ApplicationRole, string>
	{
		public BotDbContext(DbContextOptions<BotDbContext> options)           // Injected Options from Startup
			: base(options)
		{

		}
		public DbSet<Customer> Customers { get; set; }                    // Clients
		public DbSet<ChattingLog> ChattingLogs { get; set; }              // Messages
		public DbSet<ExternalAccount> ExternalAccounts { get; set; }      // Messages
		public DbSet<ApplicationUser> ApplicationUsers { get; set; }	  // Agents and administrators
		public DbSet<Group> Groups { get; set; }

	}
}
