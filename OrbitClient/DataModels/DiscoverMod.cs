using System;
using System.Collections.Generic;
using System.Text;

namespace OrbitClient.DataModels
{
    public class DiscoverMod
    {
        public string id { get; set; } = "";
        public string name { get; set; } = "";
        public string description { get; set; } = "";
        public string version { get; set; } = "";
        public string author { get; set; } = "";
        public string category { get; set; } = "";
        public string image { get; set; } = "";
        public string download { get; set; } = "";
        public string sha256 { get; set; } = "";
    }
}
