using System;
using System.Collections.Generic;
using System.Text;

namespace OrbitClient.Scripts
{
    public class SteamAccount
    {
        public string SteamId { get; set; } = "";
        public string AccountName { get; set; } = "";
        public string PersonalName { get; set; } = "";

        public override string ToString()
        {
            return $"{PersonalName} ({AccountName})";
        }
    }
}
