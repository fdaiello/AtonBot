using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MrBot.Models
{
	public class ExternalAccount
	{
		[Key]
		public Guid Id { get; set; }

		[ForeignKey("Customer")]
		public string CustomerId { get; set; }
		public bool Autenticated { get; set; }
		public bool Selected { get; set; }
		public string Email { get; set; }
		public string Phone { get; set; }
		public Guid UserId { get; set; }

	}
}
