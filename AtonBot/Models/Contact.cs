using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Reflection;

namespace MrBot.Models
{
	// Customer status says who the customer is talking to: bot / wating transfer / agent
	public enum ContactStatus
	{
		TalkingToBot,
		TalkingToAgent,
		WatingForAgent,
	}
	public enum ChannelType
	{
		None,
		WhatsApp,
		WebChat,
		Facebook,
		others
	}
	// User who connects to send and receave messages
	public class Contact
	{
		[Key]
		public string Id { get; set; }                                      // Primary Key, NOT identiy
		public int GroupId { get; set; }									// Group Id , foreign Key
		public string Name { get; set; }                                    // Name
		public string FullName { get; set; }                                // Name
		public string NickName { get; set; }                                // Name
		public string MobilePhone { get; set; }                             // Mobile phone
		public string Email { get; set; }

		[DataType(DataType.Date)]
		public DateTime FirstActivity { get; set; }                        // Date and time of first activity - creation

		[DataType(DataType.Date)]
		public DateTime LastActivity { get; set; }                         // Date and time of last activity

		public string LastText { get; set; }                               // Last message sent or receaved

		public ICollection<ChattingLog> ChatMessages { get; }              // list of all Messages sent or receaved by this 
		public ICollection<ExternalAccount> ExternalAccounts { get; }      // list of all externa accounts this user can access on company database
		public ContactStatus Status { get; set; }                         // indicate who is talking with customer: Bot or Agent
		public int UnAnsweredCount { get; set; }                           // ananswered messages - by Agent - counter
		public string ApplicationUserId { get; set; }                      // When an Agent talks to customer, Agent ID is saved here
		public ChannelType ChannelType { get; set; }					   // it will whatsApp Number, bot save 
		public string Channel { get; set; }
		public string Address { get; set; }
		public string StreetAddressNumber { get; set; }
		public string Neighborhood { get; set; }
		public string City { get; set; }
		public string Zip { get; set; }
		public string State { get; set; }
		public string Country { get; set; }
		public string Tag1 { get; set; }
		public string Tag2 { get; set; }
		public string Tag3 { get; set; }

		public void CopyFrom(Contact contact)
        {
			foreach (PropertyInfo property in typeof(Contact).GetProperties().Where(p => p.CanWrite))
			{
				property.SetValue(this, property.GetValue(contact, null), null);
			}
		}

	}

}
