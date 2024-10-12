using System.Text.Json.Serialization;

namespace FreshDeskMigrator
{
    public class Storage
    {
        [JsonPropertyName("representation")]
        public string? Representation { get; set; }
        [JsonPropertyName("value")]
        public string? Value { get; set; }

    }

    public class Body
    {
        [JsonPropertyName("storage")]
        public Storage? Storage { get; set; }
    }

    public class Version
    {
        [JsonPropertyName("number")]
        public int Number { get; set; }

        [JsonPropertyName("message")]
        public string Message { get; set; }
    }

    public class ConfluenceArticle
    {
        [JsonPropertyName("id")]
        public string? ID { get; set; }
        [JsonPropertyName("spaceId")]
        public string? SpaceID { get; set; }
        [JsonPropertyName("parentId")]
        public string? ParentID { get; set; }
        [JsonPropertyName("title")]
        public string? Title { get; set; }
        [JsonPropertyName("status")]
        public string? Status { get; set; }
        [JsonPropertyName("body")]
        public Body? Body { get; set; }

        [JsonPropertyName("version")]
        public Version? Version { get; set; }
    }

    public class LabelCreate
    {
        [JsonPropertyName("prefix")]
        public string? Prefix { get; set; }
        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }
}