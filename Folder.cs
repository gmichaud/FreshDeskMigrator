using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FreshDeskMigrator
{
    internal class Folder
    {
        public long Id { get; set; }
        public string Name { get; set; }
        public string ConfluenceId { get; set; }
        public Folder Parent { get; set;}
    }
}
