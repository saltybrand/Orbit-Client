namespace OrbitClient.DataModels
{
    public class UpdateData
    {
        public int version { get; set; }
        public string? updatename { get; set; }
        public string? description { get; set; }
        public string? updatelogs { get; set; }
        public string? releasedate { get; set; }
        public bool isbeta { get; set; }
        public string? newupdateurl { get; set; }
    }
}