using Microsoft.AspNetCore.Identity;
using System;
using System.Collections.Generic;

namespace MrBot.Models
{
	public class ApplicationUser : IdentityUser
	{
		public virtual string FriendlyName
		{
			get
			{
				string friendlyName = string.IsNullOrWhiteSpace(FullName) ? UserName : FullName;

				if (!string.IsNullOrWhiteSpace(JobTitle))
					friendlyName = $"{JobTitle} {friendlyName}";

				return friendlyName;
			}
		}

		public string JobTitle { get; set; }
		public string FullName { get; set; }
		public string NickName { get; set; }
		public string Configuration { get; set; }
		public bool IsEnabled { get; set; }
		public bool IsLockedOut => this.LockoutEnabled && this.LockoutEnd >= DateTimeOffset.UtcNow;
		public string CreatedBy { get; set; }
		public string UpdatedBy { get; set; }
		public DateTime CreatedDate { get; set; }
		public DateTime UpdatedDate { get; set; }


		/// <summary>
		/// Navigation property for the roles this user belongs to.
		/// </summary>
		public virtual ICollection<IdentityUserRole<string>> Roles { get; }

		/// <summary>
		/// Navigation property for the claims this user possesses.
		/// </summary>
		public virtual ICollection<IdentityUserClaim<string>> Claims { get; }


		// Agent specific properties
		public DateTime LastActivity { get; set; }                  // Date and time of last activity
		public string LastText { get; set; }                        // Last text send or receaved
		public ICollection<ChattingLog> ChatMessages { get; }       // list of all Messages sent or receaved by this user
		public string WebPushId { get; set; }                       // webpush subscriber Id
		public int GroupId { get; set; }							// This field is a Group Model's foeign Key
	}
}
