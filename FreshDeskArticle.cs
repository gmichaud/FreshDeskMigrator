namespace FreshDeskMigrator
{
    public class SeoData
    {
        public string? MetaTitle { get; set; }
        public string? MetaDescription { get; set; }
    }

    public class HierarchyData
    {
        public long Id { get; set; }
        public string? Name { get; set; }
        public string? Language { get; set; }
    }

    public class Hierarchy
    {
        public int Level { get; set; }
        public string? Type { get; set; }
        public HierarchyData? Data { get; set; }
    }

    public class FreshDeskArticle
    {
        public long Id { get; set; }
        public int Type { get; set; }
        public int Status { get; set; }
        public long AgentId { get; set; }
        public DateTime CreatedAt { get; set; }
        public long CategoryId { get; set; }
        public long FolderId { get; set; }
        public string? Title { get; set; }
        public DateTime UpdatedAt { get; set; }
        public string? Description { get; set; }
        public string? DescriptionText { get; set; }
        public SeoData? SeoData { get; set; }
        public List<string>? Tags { get; set; }
        public List<object>? Attachments { get; set; }
        public List<object>? CloudFiles { get; set; }
        public int ThumbsUp { get; set; }
        public int ThumbsDown { get; set; }
        public int Hits { get; set; }
        public int Suggested { get; set; }
        public int FeedbackCount { get; set; }
        public List<Hierarchy>? Hierarchy { get; set; }
    }

}
