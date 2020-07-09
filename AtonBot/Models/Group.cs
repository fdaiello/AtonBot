using System;

namespace MrBot.Models
{
    /**
     * This is User's Group Model.
     * Id:   index
     * Name: group name
     */
    public class Group
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public DateTime CreatedDate { get; set; }
        public string BotName { get; set; }
        public string WhatsAppNumber { get; set; }
        public int UserCount { get; set; }
    }
}
