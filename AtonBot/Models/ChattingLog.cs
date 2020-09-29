using System;

namespace MrBot.Models
{
	public enum MessageSource           // Indicates which role sent message: customer, bot, agent
	{
		Bot,
		Customer,
		Agent
	}
	public enum ChatMsgType             // Type of Message
	{
		Text,                           // Text
		Voice,                          // Voce - audio Ogg/Wav
		Image,                          // Images - Gif, Jpg, Png
		PDF,                            // Docs - pdf
		Word,                           // Docs - doc, docx
		Excel,                          // Excel - xls,xlsx,csv
		File,                           // Other files
		Location,						// Geografic Location
		Contacts						// Contact Card
	}
	public enum MsgStatus
	{
		None,
		Enqueued,
		Failed,
		Sent,
		Delivered,
		Read
	}
	public class ChattingLog                // Chat Message
	{
		public long Id { get; set; }                            // Primary Key, Indentity ( auto generated )
		public ChatMsgType Type { get; set; }                   // Type of Message
		public string Text { get; set; }                        // Text - in case type is text
		public string Filename { get; set; }                    // Filename - in case type is voice, image or other file format
		public MessageSource Source { get; set; }               // Indicates which role sent message: user / bot / agent
		public bool Read { get; set; }                          // Indicates if the message was read; Only used for messages destinated to Agents.
		public DateTime Time { get; set; }                      // Date and time message was sent
		public string CustomerId { get; set; }                  // Customer who sent or receved the message
		public string ApplicationUserId { get; set; }           // In case message was sent by Agent, Agent ID is saved here
		public string ActivityId { get; set; }					// ActivityID
		public MsgStatus Status { get; set; }					// None, Enqueued, Failed, Sent, Delivered, Read
		public DateTime StatusTime { get; set; }                // Time when status was last update
		public int GroupId { get; set; }                        // CustomerID.GroupID - Denormalization for Dashboard
		public string QuotedActivityId { get; set; }            // When a message is "quoted" ( cited ), this shows the ActivityID of the message beeing quoted
		public bool IsHsm { get; set; }                         // Tru when sending a HSM ( template ) WhatsApp Message

	}
}
