using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FreshDeskMigrator
{
    internal class Category
    {
        public long Id { get; set; }
        public string Name { get; set; }
        public string[] Labels { get; set; }
        public Dictionary<long, Folder> Folders { get; set; } = new();
        public string ConfluenceId { get; set; }
    }
}
